using System.Collections.Generic;
using HarmonyLib;

namespace OptimisedCraftingSystem
{
    /// <summary>
    /// 把本模组的设置项挂进游戏自己的设置界面。
    ///
    /// 为什么不用 BepInEx 的配置文件：实测原模组 `Main.cs` 全文没有任何 `Config.Bind`，
    /// 它的 19 个设置项就是游戏原生 `SettingBool`，只存游戏自己的 `settings.json`；
    /// 本模组沿用同一套路（`Settings.LoadSettings` 按名字回填），于是"改了就生效、重启还记得"是免费得到的，
    /// 也不存在"两份持久化谁是权威"的问题。
    ///
    /// 注意：`Settings.DefaultSettings()` 每次调用都返回**全新**的列表（"恢复默认"也会重建），
    /// 所以这里是重建 + 幂等追加，而不是只加一次。
    /// </summary>
    [HarmonyPatch(typeof(Settings), nameof(Settings.DefaultSettings))]
    internal static class PatchDefaultSettings
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(ref List<Setting> __result)
        {
            ModSettings.Attach(__result);
        }
    }

    /// <summary>
    /// 设置界面的"恢复默认"按钮（`SettingsMenu.cs:35-41`：`Settings.settings = Settings.DefaultSettings();
    /// Settings.ApplyAll(); Settings.SaveSettings(); SelectTab(0);`）。
    ///
    /// 这里只置一个标记：`ModSettings.Attach` 见到它就会丢弃"记忆值"、按默认值重建本模组的设置项。
    /// 不加这一步的话，点"恢复默认"只会把游戏自己的项复原，本模组的项会保持原样
    /// —— 旧模组 `Patches.cs:87-89` 就有这个缺陷。
    ///
    /// **前缀必须用高于普通优先级的值（`Priority.HigherThanNormal` = 500 &gt; 别人常用的 400）**：实测 `QoL Unknown` 的
    /// `ModOptionsMenu.SettingsMenuResetToDefaultPrefix` 会 `return false` 接管这次点击，
    /// 而 Harmony 在前缀返回 false 时会跳过原方法**以及其余前缀** —— 我们放在 p200 的标记
    /// 就永远置不上，"恢复默认"对本模组的 32 项静默失效。
    /// 取名次靠前是安全的：本前缀是 void、永不 return false、不改变任何结果，属"通知型"前缀；
    /// 真正会接管逻辑的前缀（`Recipe.GetItemsForRecipe` / `TryMake` / `AutoPickUpItem` 等）
    /// 一律留在 p200 退让，两条规则并不冲突。后缀 <see cref="Postfix"/> 是最后一道保险。
    /// </summary>
    [HarmonyPatch(typeof(SettingsMenu), nameof(SettingsMenu.ResetToDefault))]
    internal static class PatchSettingsMenuResetToDefault
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.HigherThanNormal)]
        private static void Prefix()
        {
            ModSettings.BeginReset();
        }

        /// <summary>
        /// 后缀一定会执行（哪怕原方法被别人的前缀短路掉），所以这里能查出"标记是不是没置上"：
        /// 正常路径下 `Attach` 已经置过 <see cref="ModSettings.ResetAlreadyApplied"/>，
        /// 兜底路径下则补做一次完整重置。
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(SettingsMenu __instance)
        {
            if (ModSettings.ResetAlreadyApplied)
            {
                ModSettings.ClearResetMark();
                return;
            }
            ModSettings.ForceReset(__instance);
        }
    }

    /// <summary>
    /// 语言切换后重新注入设置项文案（`SettingsMenu` 用 `Locale.GetOther("gameset" + name)` 取标题，
    /// 取不到就显示键名）。已存在的键绝不覆盖，所以汉化模组的翻译优先级仍然最高。
    /// </summary>
    [HarmonyPatch(typeof(Locale), nameof(Locale.LoadLanguage))]
    internal static class PatchLocaleLoadLanguage
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void Postfix()
        {
            try
            {
                Loc.InvalidateLanguage();
                ModSettings.InjectLocaleKeys();
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning("[Locale] 语言切换后处理失败（忽略）：" + e.Message);
            }
        }
    }
}
