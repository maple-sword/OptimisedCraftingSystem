using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OptimisedCraftingSystem
{
    /// <summary>
    /// 界面文案的拼装（合成界面追加块、Shift 内容物提示、材料总览面板共用）。
    ///
    /// 游戏自己的名字/性质名一律走 `Locale.GetItem` / `Locale.GetOther` / `CraftingQuality.LocaleName`，
    /// 所以其它模组新增的物品、液体、性质在界面上的显示与新功能都会自动生效。
    /// </summary>
    internal static class CraftText
    {
        /// <summary>
        /// 「当前正在刷新 C 键面板」标记：由 `PatchRefreshCurrentlySelectedRecipe` 的前/后缀置位与复位，
        /// 用来把面板那次 `IngredientTextForRecipe` 调用与右上角「已钉配方」HUD 的调用区分开。
        ///
        /// 标记**带帧号**：只要有人在 `RefreshCurrentlySelectedRecipe` 上挂了后缀并抛异常
        /// （真机上 Crafting Tweaks 就会，见 `Patches\PatchCompat.cs`），Harmony 会跳过排在它后面的后缀
        /// ——也就是我们的复位 —— 标记就会一直留着，下一帧右上角 HUD 重算时会把整块消耗说明糊上去。
        /// 带上帧号后，跨帧的残留标记自动作废。
        /// </summary>
        internal static bool InPanel;

        private static int _inPanelFrame = -1;

        /// <summary>开始一次 C 键面板刷新（`PatchRefreshCurrentlySelectedRecipe` 前缀调用）。</summary>
        internal static void MarkPanelRefresh()
        {
            InPanel = true;
            _inPanelFrame = Time.frameCount;
        }

        /// <summary>结束一次 C 键面板刷新（后缀调用；被别人的异常跳过时也不会留下可用标记）。</summary>
        internal static void ClearPanelRefresh()
        {
            InPanel = false;
        }

        /// <summary>
        /// 这次 `IngredientTextForRecipe` 调用是不是 C 键面板那一次。
        /// 无论结果如何都消费标记（一次刷新只追加一次）。
        /// </summary>
        internal static bool ConsumePanelRefresh()
        {
            if (!InPanel) return false;
            InPanel = false;
            return _inPanelFrame == Time.frameCount;
        }

        // ————————————————————————————————————————————————————————————
        // 槽位描述
        // ————————————————————————————————————————————————————————————

        /// <summary>槽位的名字（与 `PlayerCamera.IngredientTextForRecipe` 的取名规则一致）。</summary>
        internal static string SlotLabel(RecipeItem slot)
        {
            if (slot == null) return "?";
            try
            {
                if (slot.specific && !string.IsNullOrEmpty(slot.specificId))
                    return slot.isLiquid ? Locale.GetOther(slot.specificId) : Locale.GetItem(slot.specificId);

                if (slot.isLiquid) return Locale.GetOther("craftanyliquid");
                if (slot.quality != null && (slot.quality.id == "hammering" || slot.quality.id == "cutting"))
                    return Locale.GetOther("craftanytool");
                return Locale.GetOther("craftanyitem");
            }
            catch
            {
                return "?";
            }
        }

        /// <summary>槽位要求的性质（"可切割 5"）；specific 槽没有性质要求时返回空串。</summary>
        internal static string QualityLabel(RecipeItem slot)
        {
            if (slot == null || slot.specific || slot.quality == null || string.IsNullOrEmpty(slot.quality.id))
                return string.Empty;
            try
            {
                return QualityModel.QualityName(slot.quality.id) + " " + slot.quality.amount.ToString("0.#");
            }
            catch
            {
                return slot.quality.id;
            }
        }

        /// <summary>
        /// 液体的显示名，规则与 `PlayerCamera.ItemHoverDescription`（`PlayerCamera.cs:1348-1355`）一致：
        /// 注册表里查得到就走游戏语言表（`localeFromItem` 时是物品名，否则是 `localeName`）。
        ///
        /// 查不到时**返回 id 本身**：第三方模组可以把没注册进 `Liquids.Registry` 的液体 id 塞进容器
        /// （真机上 `com.deltash.craftingframework` 的 `Blood O+` 就是这种，见 `[Compat]` 日志），
        /// 这时宁可显示原始 id，也不能显示空白或抛异常。
        /// </summary>
        internal static string LiquidName(string liquidId)
        {
            if (string.IsNullOrEmpty(liquidId)) return null;
            try
            {
                if (Liquids.Registry != null && Liquids.Registry.TryGetValue(liquidId, out LiquidType type) && type != null)
                {
                    string name;
                    if (type.localeFromItem) name = Locale.GetItem(liquidId);
                    else if (!string.IsNullOrEmpty(type.localeName)) name = Locale.GetOther(type.localeName);
                    else name = null;

                    if (!string.IsNullOrEmpty(name)) return name;
                }
            }
            catch { }
            return liquidId;
        }

        /// <summary>
        /// 材料在界面上的名字：**液体槽位显示「液体名（容器名）」** —— 玩家要判断的是"倒掉哪种液体"，
        /// 容器只是它的载体；倒出的是哪一层由 `QualityModel.LiquidOf` 给出，与本行 mL 数字同源。
        /// 其余槽位就是物品名本身。
        /// </summary>
        internal static string MaterialLabel(RecipeItem slot, Item item)
        {
            string name = CraftingScanner.NameOf(item);
            if (slot == null || item == null || !slot.isLiquid) return name;

            string liquid = LiquidName(QualityModel.LiquidOf(slot, item));
            return string.IsNullOrEmpty(liquid) ? name : liquid + "（" + name + "）";
        }

        /// <summary>
        /// 容器在列表里的显示名：装着液体时把**里面的液体**带上（`水壶（生化流体 500 mL）`）。
        ///
        /// 两个同名水壶可能装着完全不同的液体/余量，材料总览按 id 合并成一行就分不出来了
        /// （玩家反馈「不要堆叠液体容器」）。空容器与普通物品一样只显示名字。
        /// </summary>
        internal static string ContainerName(Item item)
        {
            string name = CraftingScanner.NameOf(item);
            if (item == null || !CraftingScanner.HoldsLiquid(item)) return name;

            try
            {
                string inside = PlacementPlanner.ContentsText(item.GetComponent<WaterContainerItem>());
                if (!string.IsNullOrEmpty(inside)) return name + "（" + inside + "）";
            }
            catch { }
            return name;
        }

        /// <summary>
        /// 合成界面里追加的"逐槽消耗"块。**不带任何 `&lt;sprite&gt;` 标签**，
        /// 因为 `RefreshCurrentlySelectedRecipe` 是按 sprite 字符出现顺序摆放材料预览图标的，
        /// 多插一个 sprite 就会让图标与槽位错位。
        /// </summary>
        internal static string ConsumptionBlock(Recipe recipe)
        {
            if (recipe == null || recipe.items == null || recipe.items.Count == 0) return null;

            List<Item> selection;
            try { selection = CraftSession.Select(recipe, CraftingScanner.Gather(includeFloor: true)); }
            catch { return null; }

            StringBuilder sb = new StringBuilder();
            sb.Append("\n<color=#9CCB6A>").Append(Loc.T("ocs.craft.needs")).Append("</color>");

            for (int i = 0; i < recipe.items.Count; i++)
            {
                RecipeItem slot = recipe.items[i];
                Item item = selection != null && i < selection.Count ? selection[i] : null;

                sb.Append("\n<color=#999999>").Append(i + 1).Append(". ").Append(SlotLabel(slot));
                string quality = QualityLabel(slot);
                if (!string.IsNullOrEmpty(quality)) sb.Append(" · ").Append(quality);
                sb.Append("</color>  ");

                if (item == null)
                {
                    sb.Append("<color=#C86A6A>").Append(Loc.T("ocs.win.empty")).Append("</color>");
                    continue;
                }

                sb.Append(MaterialLabel(slot, item));

                string desc = QualityModel.Describe(slot, item);
                if (!string.IsNullOrEmpty(desc))
                    sb.Append(" <color=#E0C070>").Append(desc).Append("</color>");

                if (ModSettings.VerboseNumbers)
                    sb.Append(" <color=#808080>").Append(CraftingScanner.WhereOf(item)).Append("</color>");
            }

            string dest = ResultDestination(recipe);
            if (!string.IsNullOrEmpty(dest))
                sb.Append("\n<color=#9CCB6A>").Append(Loc.T("ocs.craft.store")).Append("</color>  ").Append(dest);

            return sb.ToString();
        }

        /// <summary>产物预计去哪（含穿戴与液体去向）。</summary>
        internal static string ResultDestination(Recipe recipe)
        {
            if (recipe == null || recipe.result == null) return null;
            try
            {
                if (recipe.result.isLiquid)
                {
                    PlacementPlanner.Plan liquid = CraftSession.PreviewLiquid(recipe.result.id, recipe.result.resultCondition);
                    return string.IsNullOrEmpty(liquid.Text) ? null : liquid.Text;
                }

                ItemInfo info = Item.GetItem(recipe.result.id);
                if (info == null) return null;
                PlacementPlanner.Plan p = PlacementPlanner.PredictOne(info, recipe.result.resultCondition, null);
                return string.IsNullOrEmpty(p.Text) ? null : p.Text;
            }
            catch
            {
                return null;
            }
        }

        // ————————————————————————————————————————————————————————————
        // 容器内容物（Shift 提示 + 面板）
        // ————————————————————————————————————————————————————————————

        /// <summary>
        /// 容器内容物的一行行汇总（按物品 id 合并计数 = 堆叠显示）。
        /// **例外**：装着液体的容器按「液体 id + 量」分组，装不同液体/不同余量的瓶子各占一行
        /// （玩家反馈「不要堆叠液体容器」），行名是 `水壶（生化流体 500 mL）`。
        /// 范围与 `Container.GetHoldingWeight()` 一致：只算直接子物体。
        /// </summary>
        internal static string ContainerContents(Item containerItem, int dotWidth = 28)
        {
            Container c = containerItem != null ? containerItem.container : null;
            if (c == null) return null;

            Dictionary<string, int> counts = new Dictionary<string, int>();
            Dictionary<string, float> weights = new Dictionary<string, float>();
            Dictionary<string, string> names = new Dictionary<string, string>();
            List<string> order = new List<string>();
            int total = 0;
            float totalWeight = 0f;

            try
            {
                foreach (Transform child in c.transform)
                {
                    if (child == null) continue;
                    Item it = child.GetComponent<Item>();
                    if (it == null) continue;

                    // 装着液体的容器**不按 id 合并**：两个水壶的液体/余量往往不同，合并成一行就分不清谁是谁了
                    // （玩家反馈「不要堆叠液体容器」）。只有液体完全相同（同 id、同量）才并成「×N」。
                    bool liquid = CraftingScanner.HoldsLiquid(it);
                    string key = liquid
                        ? "liq:" + it.id + ":" + PlacementPlanner.ContentsText(it.GetComponent<WaterContainerItem>())
                        : (it.id ?? "?");
                    float w = CraftingScanner.WeightOf(it.Stats, it.condition);

                    total++;
                    totalWeight += w;
                    if (!counts.ContainsKey(key))
                    {
                        counts[key] = 0;
                        weights[key] = 0f;
                        names[key] = liquid ? ContainerName(it) : CraftingScanner.NameOf(it);
                        order.Add(key);
                    }
                    counts[key]++;
                    weights[key] += w;
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning("[Tip] 统计容器内容物失败：" + e.Message);
                return null;
            }

            if (total == 0) return Loc.T("ocs.tip.contents.empty");

            order.Sort((a, b) => counts[b].CompareTo(counts[a]));

            StringBuilder sb = new StringBuilder();
            sb.Append(Loc.T("ocs.tip.contents")).Append(Loc.T("ocs.unit.count", total));
            if (ModSettings.ContainerWeightInTip) sb.Append("  ").Append(Loc.T("ocs.tip.contents.weight", totalWeight));

            int shown = 0;
            foreach (string key in order)
            {
                if (shown++ >= 12) { sb.Append("\n  …"); break; }
                string left = names[key] + " ×" + counts[key];
                string right = ModSettings.ContainerWeightInTip ? weights[key].ToString("0.##") + " kg" : string.Empty;
                sb.Append("\n  ").Append(Dots(left, right, dotWidth));
            }

            if (ModSettings.ContainerWeightInTip)
                sb.Append("\n  ").Append(Loc.T("ocs.tip.free.space", PlacementPlanner.FreeSpace(c)));

            return sb.ToString();
        }

        // ————————————————————————————————————————————————————————————
        // 小工具
        // ————————————————————————————————————————————————————————————

        /// <summary>把多行文案压成一行（`[实测]` 证据日志用，日志里换行会把一条记录拆成两行）。</summary>
        internal static string Flat(string s)
        {
            return string.IsNullOrEmpty(s) ? s : s.Replace("\n", " / ");
        }

        /// <summary>用点线把左右两段撑成一行的样子（"帆布 ×2 .......... 1.5 kg"）。</summary>
        internal static string Dots(string left, string right, int width = 28)
        {
            left = left ?? string.Empty;
            right = right ?? string.Empty;
            int used = VisualWidth(left) + VisualWidth(right);
            int dots = Mathf.Max(2, width - used);
            return left + new string('.', dots) + right;
        }

        /// <summary>按等宽字体估算显示宽度：CJK/全角算 2 列，其余算 1 列。</summary>
        internal static int VisualWidth(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int w = 0;
            foreach (char ch in s)
            {
                if (ch >= 0x1100 && (ch <= 0x115F || ch == 0x2329 || ch == 0x232A
                    || (ch >= 0x2E80 && ch <= 0xA4CF && ch != 0x303F)
                    || (ch >= 0xAC00 && ch <= 0xD7A3)
                    || (ch >= 0xF900 && ch <= 0xFAFF)
                    || (ch >= 0xFE30 && ch <= 0xFE6F)
                    || (ch >= 0xFF00 && ch <= 0xFF60)
                    || (ch >= 0xFFE0 && ch <= 0xFFE6)))
                {
                    w += 2;
                }
                else
                {
                    w += 1;
                }
            }
            return w;
        }
    }
}
