using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace OptimisedCraftingSystem
{
    /// <summary>
    /// 联机前置（Casualties Together / 程序集 `CasualtiesMP`、GUID `CasualtiesMP`）的运行时桥。
    ///
    /// 为什么必须用反射而不是直接引用 `CasualtiesMP.dll`：
    ///   ① 本模组对 Together 是**软依赖** —— 没装联机模组的玩家也要能正常用单机功能，
    ///      直接 `using Together;` 会让程序集加载期就抛 `FileNotFoundException`，整个插件不加载；
    ///   ② Together 的版本迭代很快（记录时 5.2.0，目标游戏版本 7.0.1），
    ///      成员改名/挪命名空间时会变成 `MissingMethodException` —— 反射可以逐项探测、逐项降级，
    ///      打一行清清楚楚的日志，而不是让玩家的游戏直接崩。
    ///
    /// 这里只负责"问状态"与"取物品的联机身份"，**不装补丁**；补丁在 `Mp\PatchTogetherCraft.cs`。
    /// 所有反射结果都缓存，热路径（每帧每配方）不会反复查找成员。
    /// </summary>
    internal static class MpBridge
    {
        /// <summary>Together 的 BepInEx GUID（`[BepInPlugin("CasualtiesMP", "Together", ...)]`）。</summary>
        internal const string Guid = "CasualtiesMP";

        /// <summary>Together 的主程序集名（`plugins\Together\CasualtiesMP.dll`）。</summary>
        internal const string AssemblyName = "CasualtiesMP";

        /// <summary>记录本次适配所依据的 Together 版本；版本变了要重新取证（见 notes\demo\krokmp-compatibility.md）。</summary>
        internal const string KnownVersion = "5.2.0";

        // ——— 反射缓存 ———
        private static bool _probed;
        private static bool _present;
        private static Type _netType;
        private static Type _itemSyncType;
        private static Type _craftCommitType;
        private static PropertyInfo _propIsRunning;
        private static PropertyInfo _propIsServer;
        private static PropertyInfo _propIsDedicated;
        private static MethodInfo _mTryGetSyncInfo;
        private static MethodInfo _mReach;

        /// <summary>补丁安装结果（自检日志用）。</summary>
        internal static readonly List<string> PatchReport = new List<string>();

        /// <summary>最近一次探测失败的原因（日志用，正常情况下是 null）。</summary>
        internal static string ProbeError { get; private set; }

        /// <summary>
        /// Together 程序集是否在场。**每次调用都会重新探测**（直到成功为止），
        /// 因为 BepInEx 的插件加载顺序不保证：我们 Awake 时 Together 的 `PluginInfos` 可能还没登记。
        /// </summary>
        internal static bool Present
        {
            get
            {
                if (_probed && _present) return true;
                return Probe();
            }
        }

        /// <summary>联机会话是否正在进行（`Together.Net.IsRunning`）。</summary>
        internal static bool Active
        {
            get
            {
                if (!Present) return false;
                // 按帧缓存：`CraftSelector.Usable` 这类热路径一帧内会被调用上千次
                // （278 个配方 × 每个槽位的候选），每次走反射取属性会变成可见的卡顿。
                if (_activeFrame == Time.frameCount) return _active;
                _activeFrame = Time.frameCount;
                try { _active = _propIsRunning != null && (bool)_propIsRunning.GetValue(null, null); }
                catch (Exception e)
                {
                    ProbeError = "读 Net.IsRunning 失败：" + e.Message;
                    _active = false;
                }
                return _active;
            }
        }

        private static int _activeFrame = -1;
        private static bool _active;

        /// <summary>本进程是否是权威端（`Together.Net.IsServer`：主机与专用服务器都为 true）。</summary>
        internal static bool IsHost
        {
            get
            {
                if (!Active) return false;
                if (_hostFrame == Time.frameCount) return _host;
                _hostFrame = Time.frameCount;
                try { _host = _propIsServer != null && (bool)_propIsServer.GetValue(null, null); }
                catch { _host = false; }
                return _host;
            }
        }

        private static int _hostFrame = -1;
        private static bool _host;

        /// <summary>专用服务器（没有本地玩家、没有 UI）。</summary>
        internal static bool IsDedicatedServer
        {
            get
            {
                if (!Active) return false;
                try { return _propIsDedicated != null && (bool)_propIsDedicated.GetValue(null, null); }
                catch { return false; }
            }
        }

        /// <summary>本模组的联机适配是否应当生效（装了 Together + 总开关开着 + 真的在联机里）。</summary>
        internal static bool Enabled => Present && ModSettings.MpEnabled && Active;

        /// <summary>只有权威端才能决定"消耗什么、产物去哪"；客户端只能发出请求。</summary>
        internal static bool Authoritative => Enabled && IsHost && !IsDedicatedServer;

        /// <summary>
        /// 联机里"本模组是否真的在替玩家挑材料"。
        /// 三个条件同时成立才算：联机适配开着（`ocs.mp.enable`）、智能选材开着（`ocs.mp.smart_pick`）、
        /// 单机那套智能选材也开着（`ocs.pick.smart`）。任一关闭 → 联机里按 Together 原版匹配，
        /// 我们连界面提示也不加（否则会显示一套不会被提交的选材结果）。
        /// </summary>
        internal static bool SmartPickActive => Enabled && ModSettings.MpSmartPick && ModSettings.PickEnabled;

        /// <summary>面向玩家/日志的一句话状态。</summary>
        internal static string StateText
        {
            get
            {
                if (!Present) return Loc.T("ocs.mp.state.absent");
                if (!Active) return Loc.T("ocs.mp.state.offline");
                if (IsDedicatedServer) return Loc.T("ocs.mp.state.dedicated");
                return IsHost ? Loc.T("ocs.mp.state.host") : Loc.T("ocs.mp.state.client");
            }
        }

        /// <summary>物品是否已经有联机身份（`ItemSync.TryGetSyncInfo` 命中且非空）。</summary>
        internal static bool Synced(Item item)
        {
            if (item == null || _mTryGetSyncInfo == null) return false;
            try
            {
                object[] args = { item, null };
                if (!(bool)_mTryGetSyncInfo.Invoke(null, args)) return false;
                return args[1] != null;
            }
            catch
            {
                // 反射失败时按"已同步"处理：宁可让 Together 自己的校验去拒绝，也不要凭空少掉候选材料
                return true;
            }
        }

        /// <summary>玩家够不够得着这件物品（`ItemSync.CheckIfBodyReachThisItem`）。</summary>
        internal static bool Reachable(Body body, Item item)
        {
            if (body == null || item == null) return false;
            if (_mReach == null)
            {
                // **fail-open**：Together 升级把这个成员改名时，宁可让候选材料留在列表里
                // （点下去由 Together 自己回 Missing），也不要静默过滤掉所有地面物品 ——
                // 后者表现为"界面永远缺料、但其实能合成"，是最难排查的一种坏。
                ProbeError = "找不到 ItemSync.CheckIfBodyReachThisItem：地面候选不做够得着过滤";
                return true;
            }
            try
            {
                // 与 Together 的池子一致：默认 20 米、不做遮挡检查（第 3/4 个参数用默认值）
                object[] args = { item, body, 20f, false };
                return (bool)_mReach.Invoke(null, args);
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// 联机下的"可用来合成"判定：Together 只认**已同步**的物品，
        /// 地面物品还要求**够得着**（`CraftCommit.Collect` 的 SyncedReachable 池）。
        ///
        /// 本模组的扫描器与 Together 的池子必须一致，否则会出现
        /// 「界面说这件能做、点下去服务器回 Missing」这种最难解释的联机 bug。
        /// </summary>
        internal static bool Usable(Item item, bool onFloor, Body body)
        {
            if (item == null) return false;
            if (!Synced(item)) return false;
            if (onFloor && body != null && !Reachable(body, item)) return false;
            return true;
        }

        /// <summary>Together 的合成提交类型（补丁安装用；未探测到时为 null）。</summary>
        internal static Type CraftCommitType
        {
            get
            {
                if (!Present) return null;
                return _craftCommitType;
            }
        }

        /// <summary>探测 Together 的类型与成员；成功后缓存，失败时下次再试。</summary>
        internal static bool Probe()
        {
            if (_probed && _present) return _present;
            _probed = true;
            _present = false;
            try
            {
                // 用 BepInEx 的插件表确认"作者声明了这个插件"，而不是只看程序集是否加载：
                // 别人也可能带一个同名程序集，但 GUID 才是 BepInEx 的身份。
                if (Chainloader.PluginInfos == null || !Chainloader.PluginInfos.ContainsKey(Guid))
                {
                    ProbeError = null;
                    return false;
                }

                _netType = AccessTools.TypeByName("Together.Net");
                _itemSyncType = AccessTools.TypeByName("Together.ItemSync");
                _craftCommitType = AccessTools.TypeByName("Together.CraftCommit");
                if (_netType == null || _itemSyncType == null || _craftCommitType == null)
                {
                    ProbeError = "找不到 Together.Net / ItemSync / CraftCommit（Together 版本可能变了）";
                    return false;
                }

                _propIsRunning = AccessTools.Property(_netType, "IsRunning");
                _propIsServer = AccessTools.Property(_netType, "IsServer");
                _propIsDedicated = AccessTools.Property(_netType, "IsDedicatedServer");

                // `out SyncInfo` 里的 SyncInfo 是 Together 自己的类型，本工程不能直接写出来，
                // 所以按「名字 + 第一个参数是 Item + 第二个是 byref」扫描，而不是用 AccessTools.Method 指签名。
                // （5.2.0 的元数据里它显示成 `TryGetSyncInfo(Item, ref SyncInfo)`，out/ref 都接受。）
                foreach (MethodInfo m in _itemSyncType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != "TryGetSyncInfo") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length == 2 && ps[0].ParameterType == typeof(Item) && ps[1].ParameterType.IsByRef)
                    {
                        _mTryGetSyncInfo = m;
                        break;
                    }
                }

                _mReach = AccessTools.Method(_itemSyncType, "CheckIfBodyReachThisItem",
                    new[] { typeof(Item), typeof(Body), typeof(float), typeof(bool) });

                if (_propIsRunning == null || _propIsServer == null || _mTryGetSyncInfo == null)
                {
                    ProbeError = "Together.Net.IsRunning / IsServer 或 ItemSync.TryGetSyncInfo 缺失（Together 版本可能变了）";
                    return false;
                }

                _present = true;
                ProbeError = null;
                return true;
            }
            catch (Exception e)
            {
                ProbeError = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        /// <summary>
        /// 启动期探测并安装联机补丁（幂等；由 `Plugin.Update` 每次调用，成功后不再做任何事）。
        ///
        /// 为什么不在 `Awake` 里一次性做完：BepInEx 先 load 全部程序集、再逐个 Awake，
        /// 但 `Chainloader.PluginInfos` 的可见时机在不同版本上略有差异；留一个"每帧重试一次"的入口，
        /// 代价只是一个 `if`，换来的是"晚加载的 Together 也能被适配"。
        /// </summary>
        internal static void EnsureInstalled()
        {
            if (_installed) return;
            if (!Present) return;

            // 先记"已尝试"再装：`Install` 是逐个目标挂补丁的，若中途抛异常而下次又重跑一遍，
            // 已经挂成功的目标会被**重复挂载**（同一前缀执行两次）。宁可部分生效 + 日志里写清楚，
            // 也不要重复挂载。装完不抛异常（`Install` 内部逐项 try/catch），这里再兜一层。
            _installed = true;
            try
            {
                PatchTogetherCraft.Install(Plugin.HarmonyInstance);
            }
            catch (Exception e)
            {
                PatchReport.Add("安装异常=" + e.GetType().Name);
                Plugin.Log?.LogError("[联机] 安装联机补丁时抛异常（部分功能可能没挂上）：" + e);
            }

            Plugin.Log?.LogInfo("[联机] 检测到 " + KnownVersion + " 系列联机模组（CasualtiesMP / " + Guid + "）：" +
                                "状态=" + StateText + "；" + DescribePatches());
            if (ProbeError != null) Plugin.Log?.LogWarning("[联机] 桥探测有问题：" + ProbeError);

            // 证据行：联机适配是本模组新增的一整块能力，日志里留痕后"装没装、装在哪"可以直接核对
            Plugin.Evidence("mp:probe",
                "联机适配已挂载（依据 Together " + KnownVersion + "）：" + DescribePatches() +
                "；当前" + StateText + "。");
        }

        private static bool _installed;

        /// <summary>把补丁安装结果拼成一行（自检与日志共用）。</summary>
        internal static string DescribePatches()
        {
            if (PatchReport.Count == 0) return "联机补丁：无";
            return "联机补丁：" + string.Join("，", PatchReport.ToArray());
        }

        /// <summary>读取本机安装的 Together 版本（拿不到就返回 null）。</summary>
        internal static string InstalledVersion
        {
            get
            {
                try
                {
                    if (Chainloader.PluginInfos != null && Chainloader.PluginInfos.ContainsKey(Guid))
                    {
                        PluginInfo info = Chainloader.PluginInfos[Guid];
                        return info != null && info.Metadata != null ? info.Metadata.Version.ToString() : null;
                    }
                }
                catch { }
                return null;
            }
        }
    }
}
