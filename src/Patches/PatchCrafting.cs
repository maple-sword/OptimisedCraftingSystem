using System.Collections.Generic;
using HarmonyLib;

namespace OptimisedCraftingSystem
{
    /// <summary>
    /// 材料选择接管。
    ///
    /// 原模组用 Prefix 返回 false 把 `Recipe.GetItemsForRecipe` / `Recipe.GetItemsForRecipeThorough`
    /// **整段替换**掉（`Patches.cs:12-120`），顺便把"每次调用都重新 `Physics2D.OverlapCircleAll`
    /// 扫一圈地面"这个卡顿根源也复制了进去。这里改成：
    ///   · 扫描走 `CraftingScanner`（同帧只扫一次，132 个配方不会各扫一遍）；
    ///   · 选材走 `CraftSession.Select`（真实成本 + 稀缺保护 + 玩家手动指定）；
    ///   · 两个 getter 用**同一份算法**，所以"合成时真正消耗的"与"界面上显示的"永远一致。
    /// </summary>
    [HarmonyPatch(typeof(Recipe), nameof(Recipe.GetItemsForRecipe))]
    internal static class PatchRecipeGetItemsForRecipe
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Low)]
        private static bool Prefix(Recipe __instance, ref List<Item> __result)
        {
            if (!ModSettings.PickEnabled) return true;
            if (__instance == null || __instance.items == null || __instance.items.Count == 0) return true;

            try
            {
                List<Item> pool = CraftingScanner.Gather(includeFloor: true);
                List<Item> selection = CraftSession.Select(__instance, pool);
                if (selection == null || selection.Count != __instance.items.Count) return true;

                CraftSession.SetSelection(selection);

                // 与原版一致：只要有槽位凑不出来就返回 null（TryMake 会播放拒绝音）
                for (int i = 0; i < selection.Count; i++)
                {
                    if (selection[i] == null)
                    {
                        __result = null;
                        return false;
                    }
                }

                __result = selection;
                return false;
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning("[Recipe] 智能选材失败，退回原版逻辑：" + e.Message);
                return true;
            }
        }
    }

    /// <summary>界面预览用的版本：允许出现 `null`（表示该槽位缺料，界面显示"缺料"图标）。</summary>
    [HarmonyPatch(typeof(Recipe), nameof(Recipe.GetItemsForRecipeThorough))]
    internal static class PatchRecipeGetItemsForRecipeThorough
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Low)]
        private static bool Prefix(Recipe __instance, ref List<Item> __result)
        {
            if (!ModSettings.PickEnabled) return true;
            if (__instance == null || __instance.items == null || __instance.items.Count == 0) return true;

            try
            {
                List<Item> pool = CraftingScanner.Gather(includeFloor: true);
                List<Item> selection = CraftSession.Select(__instance, pool);
                if (selection == null || selection.Count != __instance.items.Count) return true;

                CraftSession.SetSelection(selection);
                __result = selection;
                return false;
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning("[Recipe] 预览选材失败，退回原版逻辑：" + e.Message);
                return true;
            }
        }
    }

    /// <summary>一次合成的开始与结束：前缀开会话（算好产物去向），后缀收尾（播报"已存放"）。</summary>
    [HarmonyPatch(typeof(Recipe), nameof(Recipe.TryMake))]
    internal static class PatchRecipeTryMake
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Low)]
        private static void Prefix(Recipe __instance)
        {
            try { CraftSession.Begin(__instance); }
            catch (System.Exception e) { Plugin.Log?.LogWarning("[Craft] 会话开启失败：" + e.Message); }
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.High)]
        private static void Postfix()
        {
            try { CraftSession.End(); }
            catch { }
        }
    }

    /// <summary>
    /// 「材料被整件消耗掉时，别把它里面装的东西一起烧掉」。
    ///
    /// 原版在这一步只对**固体容器**做了 `Container.UnloadAllItems()`（东西原地倒下、掉在地上），
    /// 对**液体容器**什么都没做 —— 瓶子被 `Object.Destroy` 之后，瓶里的液体直接消失
    /// （`RecipeItem.cs:167-175`）。而且匹配阶段就要求"容器必须全空"，装满东西的袋子
    /// 连被选中的机会都没有（`RecipeItem.cs:58-61`）。
    ///
    /// 我们改成：消耗前把内容物**搬出来**（固体先冻结、液体先倒进临时瓶），
    /// 于是原版的 `UnloadAllItems()` 成了空操作；等新产物被捡起时再装进新容器
    /// （`Patches\PatchStorage.cs` 里的 `ContainerRescue.Deliver`），装不下的在会话收尾时落地。
    /// 这里只做前缀、不返回 false —— 消耗本身仍由原版执行，我们只是提前把东西挪走。
    /// </summary>
    [HarmonyPatch(typeof(RecipeItem), nameof(RecipeItem.UseItem))]
    internal static class PatchRecipeItemUseItem
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Low)]
        private static void Prefix(RecipeItem __instance, Item item)
        {
            try { ContainerRescue.BeforeConsume(__instance, item); }
            catch (System.Exception e) { Plugin.Log?.LogWarning("[Rescue] 消耗前处理失败：" + e.Message); }
        }
    }
}
