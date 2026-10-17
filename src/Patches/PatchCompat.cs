using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;

namespace OptimisedCraftingSystem
{
    /// <summary>
    /// 第三方兼容垫片：`RecipeItem.DoesUseItemType`。
    ///
    /// 起因（真机日志，19:43 那轮）：在装了 Crafting Tweaks 的机器上按覆盖层的 `[◀][▶]` 手动换材料时，
    /// 本模组会调一次 `PlayerCamera.RefreshCurrentlySelectedRecipe()` 让 C 键面板立刻跟上，
    /// 而 Crafting Tweaks 在这个方法的**后缀**里会遍历身上所有物品、对每个槽位调
    /// `ingredient.DoesUseItemType(item)` 统计「(库存/需求)」：
    ///
    /// <code>
    /// [Warning:Optimised Crafting System] [UI] 材料总览界面绘制失败（已停止重复报告）：System.NullReferenceException
    ///   at (wrapper dynamic-method) RecipeItem.DMD&lt;RecipeItem::DoesUseItemType&gt;(RecipeItem,Item)
    ///   at CraftingTweaks.CraftingInventory.IngredientCountAvailable (IEnumerable`1[T] items, RecipeItem ingredient)
    ///   at CraftingTweaks.CraftingTweaksUi.EnhanceIngredientPreviews (PlayerCamera camera, Recipe recipe)
    ///   at CraftingTweaks.RefreshSelectedRecipePatch.Postfix (PlayerCamera __instance)
    ///   at (wrapper dynamic-method) PlayerCamera.DMD&lt;PlayerCamera::RefreshCurrentlySelectedRecipe&gt;(PlayerCamera)
    ///   at OptimisedCraftingSystem.UI.MaterialWindow.RefreshPanel ()
    /// </code>
    ///
    /// 原版 `RecipeItem.DoesUseItemType`（`_GameSource\Assembly-CSharp\RecipeItem.cs:78-116`）会直接解引用传入的物品：
    /// `:82 item.TryGetComponent&lt;WaterContainerItem&gt;`、`:106 Item.HasCommonQuality(quality, item.Stats.qualities)`、
    /// `:111 item.id`，以及液体分支 `:93-96` 里 `Liquids.Registry.TryGetValue(id, out var value)` 查不到时的 `value.qualities`
    /// —— 传进来的物品为 null、`item.Stats` 为 null（`Item.cs:64 ItemInfo Stats => GetItem(id)`，id 没注册过就返回 null），
    /// 或者容器里装着注册表里没有的液体 id，都会抛 NRE。
    ///
    /// 这里挂一个 finalizer：**只吞掉这个方法抛出的异常**，并把它记成一条信息完整的日志
    /// （物品 id / 有没有 Stats / 容器里的液体 id 是否注册 / 槽位需求），返回值退化成 `false`
    /// （＝「这件物品不满足该槽位」，正是原版判不出来时的语义）。
    ///
    /// 为什么值得挂：Harmony 的异常会打断**调用方整条链** —— 上游是 Crafting Tweaks 的库存计数，
    /// 下游还有我们自己注册在 `PlayerCamera.RefreshCurrentlySelectedRecipe` 上的复位后缀。
    /// 吞掉这一条异常，第三方界面不再被打断，我们也能在日志里拿到根因；正常路径完全不经过这里，
    /// 所以不影响原版与其它模组的判定。
    /// </summary>
    [HarmonyPatch(typeof(RecipeItem), nameof(RecipeItem.DoesUseItemType))]
    internal static class PatchDoesUseItemTypeCompat
    {
        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Low)]
        private static Exception Finalizer(RecipeItem __instance, Item item, Exception __exception)
        {
            if (__exception == null) return null;   // 正常情况什么都不做

            try
            {
                // 真机日志里同一件物品（血袋装着第三方液体 `Blood O+`）+ 同一个槽位会被反复判定，
                // 每次刷新 C 键面板都可能再抛一次；这里按「物品/液体/槽位/异常类型」去重，
                // 同一个组合只在本次游戏里记第一条，日志不会被刷屏。
                string desc = Describe(item, __instance);
                string key = desc + "|" + __exception.GetType().Name;
                if (FirstTime(key))
                {
                    Plugin.Log?.LogWarning(
                        "[Compat] RecipeItem.DoesUseItemType 抛异常（已吞掉，返回值按「不满足」处理）："
                        + desc
                        + " → " + __exception.GetType().Name + ": " + __exception.Message
                        + "（同一组合只记第一条）");
                }
            }
            catch
            {
                // 连诊断都失败的话就只吞异常，绝不能把异常从这里再抛出去
            }

            return null;   // 返回 null = 吞掉异常，方法以默认返回值 false 结束
        }

        /// <summary>已报告过的「物品/液体/槽位/异常类型」组合（只在本次运行内有效，进程退出即清空）。</summary>
        private static readonly HashSet<string> ReportedKeys = new HashSet<string>();
        private static readonly object ReportedGate = new object();

        /// <summary>首次出现返回 true；字典本身出错时返回 true（宁可多记一条，也不要漏记）。</summary>
        private static bool FirstTime(string key)
        {
            try
            {
                lock (ReportedGate) return ReportedKeys.Add(key);
            }
            catch
            {
                return true;
            }
        }

        /// <summary>把出问题时的关键状态拼成一行，供日志核对（尽量不抛异常）。</summary>
        private static string Describe(Item item, RecipeItem slot)
        {
            StringBuilder sb = new StringBuilder();

            if (item == null)
            {
                sb.Append("物品=null");
            }
            else
            {
                try { sb.Append("物品=").Append(item.id); } catch { sb.Append("物品=?（读 id 就失败）"); }
                try { sb.Append(item.Stats == null ? " Stats=null(未注册的 id)" : " Stats=正常"); } catch { sb.Append(" Stats=读取失败"); }
                try
                {
                    WaterContainerItem wc;
                    if (item.TryGetComponent(out wc) && wc != null && wc.stack != null)
                    {
                        sb.Append(" 液体=[");
                        for (int i = 0; i < wc.stack.Count; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            string id = wc.stack[i].liquidId;
                            sb.Append(id);
                            try
                            {
                                sb.Append(Liquids.Registry != null && Liquids.Registry.ContainsKey(id)
                                    ? "(已注册)" : "(注册表里没有！)");
                            }
                            catch { }
                        }
                        sb.Append(']');
                    }
                }
                catch { sb.Append(" 液体=读取失败"); }
            }

            try
            {
                if (slot == null) sb.Append(" 槽位=null");
                else if (slot.specific) sb.Append(" 槽位=指定:").Append(slot.specificId);
                else sb.Append(" 槽位=性质:").Append(slot.quality != null ? slot.quality.id : "quality=null");
            }
            catch { }

            return sb.ToString();
        }
    }
}
