using System.Collections.Generic;
using HarmonyLib;

namespace OptimisedCraftingSystem
{
    /// <summary>
    /// C 键合成界面的信息增强。
    ///
    /// 追加的是**纯文本**块（逐槽的消耗量，液体直接给 mL），
    /// 不插入 `&lt;sprite&gt;`：`RefreshCurrentlySelectedRecipe` 按 sprite 字符的出现顺序
    /// 摆放材料预览图标（`PlayerCamera.cs:714`），多一个 sprite 就会让图标与槽位错位。
    ///
    /// `IngredientTextForRecipe` 游戏里只有两个调用点：`PlayerCamera.cs:681`（C 键面板）与
    /// `PlayerCamera.cs:2925`（右上角「已钉配方」HUD，每 0.5 秒重算一次）。
    /// 所以用 `CraftText.InPanel` 把追加限定在面板那次调用上 —— 否则钉一个配方，
    /// 整块消耗说明会糊在 HUD 上。
    /// </summary>
    [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.IngredientTextForRecipe))]
    internal static class PatchIngredientTextForRecipe
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(Recipe recipe, ref string __result)
        {
            // 不是 C 键面板那次调用（例如右上角 HUD）就什么都不做；
            // 标记带帧号，所以即使别人的后缀抛异常把我们的复位跳过，跨帧也不会误判。
            if (!CraftText.ConsumePanelRefresh()) return;
            if (!ModSettings.ShowConsumption) return;
            if (recipe == null || __result == null) return;

            try
            {
                string extra = CraftText.ConsumptionBlock(recipe);
                if (!string.IsNullOrEmpty(extra))
                {
                    __result += extra;
                    // 证据日志：界面类功能本身不留痕，这一行让"C 键面板真的显示了消耗块"可被日志证实
                    Plugin.Evidence(
                        "panel:" + recipe.index + ":" + extra.GetHashCode(),
                        "C 键面板消耗块（" + recipe.simpleName + "）：" + CraftText.Flat(extra));
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning("[UI] 追加消耗提示失败：" + e.Message);
            }
        }
    }

    /// <summary>
    /// 只在「刷新当前选中配方」这段时间里打开 `CraftText.InPanel`，
    /// 用来把它内部那一次 `IngredientTextForRecipe` 调用与 HUD 的调用区分开。
    /// 前缀置位、后缀复位。
    ///
    /// 真机教训：这个方法的**后缀**是按优先级依次执行的，排在前面的后缀抛异常（真机上 Crafting Tweaks
    /// 就会，见 `Patches\PatchCompat.cs`）会让我们这个 Low 后缀**根本不执行** ⇒ 光靠后缀复位不可靠。
    /// 因此标记带帧号（`CraftText.MarkPanelRefresh`），跨帧的残留标记自动作废，HUD 不会被误追加。
    /// </summary>
    [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.RefreshCurrentlySelectedRecipe))]
    internal static class PatchRefreshCurrentlySelectedRecipe
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Low)]
        private static void Prefix()
        {
            CraftText.MarkPanelRefresh();
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void Postfix()
        {
            CraftText.ClearPanelRefresh();
        }
    }

    /// <summary>
    /// 物品提示：**按住 Shift（或游戏自己的 "expanddesc" 键）** 时把容器内容物总量倒出来，
    /// 形如 `帆布 ×2 .......... 1.5 kg`。原版提示只给容器自身的重量，看不到里面装了什么。
    /// </summary>
    [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
    internal static class PatchItemHoverDescription
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(Item item, ref (string, string) __result)
        {
            if (item == null) return;
            if (!ModSettings.ContainerTotalsInTip) return;
            if (!Hotkeys.ExpandHeld()) return;

            try
            {
                string extra = CraftText.ContainerContents(item);
                if (!string.IsNullOrEmpty(extra))
                {
                    __result.Item2 = (__result.Item2 ?? string.Empty) + "\n" + extra;
                    // 证据日志：这个后缀每帧都在跑（悬停刷新），靠 key 去重做到"每种物品只记一行"
                    Plugin.Evidence(
                        "tip:" + item.id + ":" + extra.GetHashCode(),
                        "Shift 内容物提示（" + CraftingScanner.NameOf(item) + "）：" + CraftText.Flat(extra));
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning("[Tip] 追加内容物提示失败：" + e.Message);
            }
        }
    }

    /// <summary>合成界面开/关：把状态同步给自建面板（关闭时收起"选材/选容器"弹层）。</summary>
    [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.OpenCraftScreen))]
    internal static class PatchOpenCraftScreen
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(PlayerCamera __instance)
        {
            try
            {
                bool open = __instance != null && __instance.craftingPanel != null && __instance.craftingPanel.activeSelf;
                UI.MaterialWindow.OnCraftScreenToggled(open);
            }
            catch { }
        }
    }
}
