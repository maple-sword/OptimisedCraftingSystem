using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace OptimisedCraftingSystem
{
    /// <summary>
    /// 模组入口。
    ///
    /// 本模组是 BetterInventoryLogic（GUID com.balaur.BetterLogic）的重写与扩展，
    /// **两者不能同时安装**：它们补同一批方法（Recipe.GetItemsForRecipe / RecipeResult.SpawnResult /
    /// Settings.DefaultSettings / Locale.LoadLanguage），同时存在时补丁会互相覆盖，
    /// 而且设置项会重名（本模组刻意沿用了原模组的设置名以保住汉化）。
    /// 所以进游戏时会检测原模组是否还在，若在就在日志里报错提醒删除。
    ///
    /// 1.1.0 起增加**联机适配**（Casualties Together / 程序集 CasualtiesMP，软依赖）：
    /// 联机时游戏把合成整条链路换成了"客户端提交 ids、主机权威执行"，所以本模组另挂一组补丁，
    /// 见 `Mp\PatchTogetherCraft.cs`；没装联机模组时这一整套完全不加载。
    /// </summary>
    [BepInPlugin(Guid, "Optimised Crafting System", Version)]
    // CCL 是本工作区的默认前置（软依赖：没装也能跑，本模组不写注册表）。
    [BepInDependency("net.cucorelib", BepInDependency.DependencyFlags.SoftDependency)]
    // 联机模组：同样是软依赖。真正的挂钩在 `Mp\MpBridge.cs` 里用反射探测，避免直接引用其程序集。
    [BepInDependency(MpBridge.Guid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        /// <summary>BepInEx 插件 GUID（全小写、作者.模组名，已在 plugins 目录与本工作区确认未占用）。</summary>
        public const string Guid = "maple.optimisedcraftingsystem";

        /// <summary>模组版本，仅用于日志与问题定位。</summary>
        public const string Version = "1.1.0";

        /// <summary>原模组 GUID：安装检查用。</summary>
        private const string LegacyGuid = "com.balaur.BetterLogic";

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        /// <summary>本插件的 Harmony 实例（联机补丁需要用它动态安装，见 `Mp\MpBridge.cs`）。</summary>
        internal static Harmony HarmonyInstance;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            WarnIfLegacyPluginLoaded();

            HarmonyInstance = new Harmony(Guid);
            HarmonyInstance.PatchAll(typeof(Plugin).Assembly);

            Log.LogInfo("OptimisedCraftingSystem " + Version + " 已加载（Harmony 补丁已应用）。");
        }

        /// <summary>每帧处理热键；GUI 绘制交给 OnGUI。</summary>
        private void Update()
        {
            Hotkeys.Tick();
            // 联机补丁：BepInEx 不能保证我们 Awake 时联机模组已经登记进 PluginInfos，
            // 所以放在每帧入口重试一次（装好后只是一个 if，成本可忽略）。
            // 必须排在 SelfCheck 之前：自检要报告"联机补丁挂了哪几个"。
            MpBridge.EnsureInstalled();
            // 合成会话看门狗：任何异常路径都不该让"被摘出来的内容物"永远冻着（见 Mp\PatchTogetherCraft.Tick）
            PatchTogetherCraft.Tick();
            SelfCheck.Tick();
        }

        /// <summary>自建 GUI（IMGUI）：独立材料总览窗口 + 合成界面上的紧凑覆盖层。</summary>
        private void OnGUI()
        {
            UI.MaterialWindow.Draw();
            UI.CraftOverlay.Draw();
        }

        private void OnDestroy()
        {
            // HarmonyX 已弃用 UnpatchAll(string guid)，用 UnpatchSelf 精确撤销自己的补丁
            HarmonyInstance?.UnpatchSelf();
            HarmonyInstance = null;
        }

        /// <summary>
        /// "实测证据"日志的去重表：同一个 key 只写一行。
        ///
        /// 为什么需要它：本模组的界面类功能（C 键面板的消耗块、材料总览窗口、Shift 内容物提示、
        /// 恢复默认）**本身不产生任何日志**，光看 BepInEx 日志没法证实它们真的生效了 ——
        /// 而 `docs/改进前后对比.md` 的实测表正需要这种证据。所以这些功能各自在"确实执行了"
        /// 的那一行上记一条 `[实测]`；用 key 去重是因为物品提示每帧都在刷新，不去重会刷屏。
        /// </summary>
        private static readonly HashSet<string> _evidence = new HashSet<string>();

        /// <summary>记一行 `[实测]` 证据日志（同 key 只记一次）。任何情况下都不抛异常。</summary>
        internal static void Evidence(string key, string line)
        {
            try
            {
                if (key != null && !_evidence.Add(key)) return;
                Log?.LogInfo("[实测] " + line);
            }
            catch
            {
                // 记日志失败绝不该影响游戏
            }
        }

        /// <summary>同 <see cref="Evidence"/>，但每次调用都记（用于"一次交互一行"的事件，例如联机提交）。</summary>
        internal static void EvidenceAlways(string line)
        {
            try { Log?.LogInfo("[实测] " + line); } catch { }
        }

        /// <summary>检测冲突模组：原版 BetterInventoryLogic 与本模组功能重叠，必须二选一。</summary>
        private void WarnIfLegacyPluginLoaded()
        {
            try
            {
                if (Chainloader.PluginInfos != null && Chainloader.PluginInfos.ContainsKey(LegacyGuid))
                {
                    Log.LogError("检测到旧模组 BetterInventoryLogic（" + LegacyGuid + "）仍在运行！" +
                                 "它与本模组补丁重叠、设置项同名，请删除 BepInEx/plugins/CraftingLogic.dll 后重启游戏，否则行为不可预测。");
                }
            }
            catch
            {
                // 拿不到插件列表不影响本模组运行，忽略
            }
        }
    }
}
