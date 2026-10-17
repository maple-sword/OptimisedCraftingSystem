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
    /// </summary>
    [BepInPlugin(Guid, "Optimised Crafting System", Version)]
    public class Plugin : BaseUnityPlugin
    {
        /// <summary>BepInEx 插件 GUID（全小写、作者.模组名，已在 plugins 目录与本工作区确认未占用）。</summary>
        public const string Guid = "maple.optimisedcraftingsystem";

        /// <summary>模组版本，仅用于日志与问题定位。</summary>
        public const string Version = "1.0.0";

        /// <summary>原模组 GUID：安装检查用。</summary>
        private const string LegacyGuid = "com.balaur.BetterLogic";

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            WarnIfLegacyPluginLoaded();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);

            Log.LogInfo("OptimisedCraftingSystem " + Version + " 已加载（Harmony 补丁已应用）。");
        }

        /// <summary>每帧处理热键；GUI 绘制交给 OnGUI。</summary>
        private void Update()
        {
            Hotkeys.Tick();
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
            _harmony?.UnpatchSelf();
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
