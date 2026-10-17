using System.Collections.Generic;
using UnityEngine;

namespace OptimisedCraftingSystem
{
    /// <summary>一次合成对某个材料的消耗方式。</summary>
    internal enum ConsumeKind
    {
        None = 0,
        LiquidMl = 1,         // 液体：倒掉若干 mL
        ConditionPoints = 2,  // 固体工具：扣若干耐久（0~1）
        WholeItem = 3,        // 固体：整件消失
    }

    /// <summary>
    /// 某个材料满足某个槽位时的"消耗明细" —— 智能选材算法的输入。
    /// 这里刻意**复刻 RecipeItem.UseItem 的真实公式**，不是估算：
    ///   液体：ml = 匹配到的那一层的量 × clamp01(需求 / 该层提供的性质)
    ///   固体：耐久 -= clamp01(需求 / 该件提供的性质)，destroyItem 时整件消失
    /// 因此界面显示的数字 = 实际会被扣掉的数字。
    /// </summary>
    internal struct Consumption
    {
        internal ConsumeKind Kind;
        internal float Amount;      // 液体：mL；耐久：耐久点数（0~1）
        internal float Value;       // 本次真正损失的价值（游戏的价值单位）
        internal float Fraction;    // 相对"整件"的比重（0~1）
        internal int UsesLeft;      // 这件材料还能满足该槽位几次
        internal string LiquidId;   // 液体专用：实际会被倒出的液体 id

        internal bool Valid => Kind != ConsumeKind.None;
    }

    /// <summary>性质（CraftingQuality）/ 消耗量的统一模型。</summary>
    internal static class QualityModel
    {
        /// <summary>性质的显示名：游戏语言表里的 "cq"+id（如 cqcutting）。</summary>
        internal static string QualityName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "?";
            try
            {
                string s = Locale.GetOther("cq" + id);
                return string.IsNullOrEmpty(s) ? id : s;
            }
            catch { return id; }
        }

        /// <summary>计算"用这件材料满足这个槽位"的消耗明细；不满足时 Kind = None。</summary>
        internal static Consumption Of(RecipeItem slot, Item item)
        {
            Consumption c = default;
            if (slot == null || item == null) return c;

            if (slot.isLiquid) return Liquid(slot, item);
            return Solid(slot, item);
        }

        // ————————————————————————————————————————————————————————————
        // 固体
        // ————————————————————————————————————————————————————————————

        private static Consumption Solid(RecipeItem slot, Item item)
        {
            Consumption c = default;
            ItemInfo stats = item.Stats;
            if (stats == null) return c;

            float take;
            if (slot.specific)
            {
                if (item.id != slot.specificId) return c;
                if (item.condition < slot.minimumCondition) return c;
                take = slot.minimumCondition;
            }
            else
            {
                if (slot.quality == null) return c;
                CraftingQuality matched = Item.GetQualityThatMeetsCriteria(slot.quality, stats.qualities);
                if (matched == null) return c;
                if (item.condition < slot.minimumCondition) return c;
                take = Mathf.Clamp01(slot.quality.amount / Mathf.Max(0.0001f, matched.amount));
            }

            if (slot.destroyItem)
            {
                c.Kind = ConsumeKind.WholeItem;
                c.Amount = take;
                c.Fraction = 1f;
                c.UsesLeft = 1;
                c.Value = CurrentValue(item);
            }
            else
            {
                c.Kind = ConsumeKind.ConditionPoints;
                c.Amount = take;
                c.Fraction = Mathf.Clamp01(take);
                c.UsesLeft = take > 0.0001f ? Mathf.Max(1, Mathf.FloorToInt(item.condition / take)) : int.MaxValue;
                c.Value = stats.DefaultValue() * take;
            }
            return c;
        }

        // ————————————————————————————————————————————————————————————
        // 液体
        // ————————————————————————————————————————————————————————————

        private static Consumption Liquid(RecipeItem slot, Item item)
        {
            Consumption c = default;
            WaterContainerItem wc = item.GetComponent<WaterContainerItem>();
            if (wc == null || wc.stack == null) return c;

            string liquidId;
            float ml;
            float availableMl;

            if (slot.specific)
            {
                availableMl = wc.AmountOf(slot.specificId);
                if (availableMl < slot.minimumCondition) return c;
                liquidId = slot.specificId;
                ml = slot.minimumCondition;
            }
            else
            {
                if (slot.quality == null) return c;
                // 复刻 UseItem：按 stack 顺序取第一个满足性质的那一层
                bool found = false;
                liquidId = null; ml = 0f; availableMl = 0f;
                for (int i = 0; i < wc.stack.Count; i++)
                {
                    LiquidStack st = wc.stack[i];
                    if (st == null) continue;
                    if (!Liquids.Registry.TryGetValue(st.liquidId, out LiquidType type)) continue;
                    CraftingQuality matched = Item.GetQualityThatMeetsCriteria(slot.quality, type.GetScaledQualities(st.amount));
                    if (matched == null) continue;
                    float t = Mathf.Clamp01(slot.quality.amount / Mathf.Max(0.0001f, matched.amount));
                    liquidId = st.liquidId;
                    availableMl = st.amount;
                    ml = st.amount * t;
                    found = true;
                    break;
                }
                if (!found) return c;
            }

            c.Kind = ConsumeKind.LiquidMl;
            c.Amount = ml;
            c.LiquidId = liquidId;
            c.Fraction = wc.CurrentTotal > 0.0001f ? Mathf.Clamp01(ml / wc.CurrentTotal) : 0f;
            c.Value = LiquidValue(liquidId, ml);
            c.UsesLeft = ml > 0.0001f ? Mathf.Max(1, Mathf.FloorToInt(availableMl / ml)) : int.MaxValue;
            return c;
        }

        /// <summary>
        /// 这件材料在这个槽位上**实际会被倒出**的那种液体 id（非液体消耗、或取不到时返回 null）。
        /// 与界面上的 mL 数字取自同一次 `Of()` 计算，所以「名字」与「数字」永远对得上同一层液体。
        /// </summary>
        internal static string LiquidOf(RecipeItem slot, Item item)
        {
            if (slot == null || item == null || !slot.isLiquid) return null;
            try
            {
                Consumption c = Of(slot, item);
                return c.Kind == ConsumeKind.LiquidMl ? c.LiquidId : null;
            }
            catch { return null; }
        }

        /// <summary>液体的价值：与 ItemInfo.GetValue 内部对内容物的估法一致（每升 valuePerLiter）。</summary>
        internal static float LiquidValue(string liquidId, float ml)
        {
            if (string.IsNullOrEmpty(liquidId) || ml <= 0f) return 0f;
            try
            {
                if (Liquids.Registry.TryGetValue(liquidId, out LiquidType type) && type != null)
                    return ml * 0.001f * type.valuePerLiter;
            }
            catch { }
            return 0f;
        }

        // ————————————————————————————————————————————————————————————
        // 展示与统计
        // ————————————————————————————————————————————————————————————

        /// <summary>物品当前价值（含内容物），与游戏一致。</summary>
        internal static float CurrentValue(Item item)
        {
            try { return item?.Stats != null ? item.Stats.GetValue(item) : 0f; }
            catch { return 0f; }
        }

        /// <summary>
        /// 消耗明细的显示文本（"120 mL" / "耐久 -20%（还能用 4 次）" / "整件消耗"）。
        /// 可反复使用的工具会附上"这件还能用几次"，这是玩家最需要、而原版完全不给的信息。
        /// </summary>
        internal static string Describe(RecipeItem slot, Item item)
        {
            Consumption c = Of(slot, item);
            switch (c.Kind)
            {
                case ConsumeKind.LiquidMl:
                    return Loc.T("ocs.unit.ml", c.Amount);
                case ConsumeKind.ConditionPoints:
                    string text = Loc.T("ocs.unit.cond", c.Amount * 100f);
                    // 只剩这一次用量时不必再说"还能用 1 次"（`UsesLeft` 含本次，且被 clamp 到 ≥1）
                    if (c.UsesLeft >= 2 && c.UsesLeft < 1000)
                        text += Loc.T("ocs.craft.uses", c.UsesLeft);
                    return text;
                case ConsumeKind.WholeItem:
                    return Loc.T("ocs.craft.destroyed");
                default:
                    return "-";
            }
        }

        /// <summary>"每点性质的成本" —— 性价比列用的数字（越小越划算）。</summary>
        internal static float CostPerQuality(RecipeItem slot, Item item)
        {
            Consumption c = Of(slot, item);
            if (!c.Valid) return float.MaxValue;
            float need = 1f;
            if (slot.quality != null) need = Mathf.Max(0.0001f, slot.quality.amount);
            else if (slot.isLiquid) need = Mathf.Max(0.0001f, slot.minimumCondition);
            return c.Value / need;
        }

        /// <summary>
        /// 这件材料能提供的全部性质（固体直接取 Stats.qualities；
        /// 液体把每层的"每 mL 性质"乘上该层 mL 换算成总量）。
        /// </summary>
        internal static List<CraftingQuality> QualitiesOf(Item item)
        {
            List<CraftingQuality> result = new List<CraftingQuality>();
            if (item == null) return result;

            ItemInfo stats = item.Stats;
            if (stats?.qualities != null)
            {
                foreach (CraftingQuality q in stats.qualities)
                {
                    if (q != null) result.Add(new CraftingQuality(q.id, q.amount));
                }
            }

            WaterContainerItem wc = item.GetComponent<WaterContainerItem>();
            if (wc?.stack != null)
            {
                foreach (LiquidStack st in wc.stack)
                {
                    if (st == null) continue;
                    if (!Liquids.Registry.TryGetValue(st.liquidId, out LiquidType type) || type?.qualities == null) continue;
                    foreach (CraftingQuality q in type.GetScaledQualities(st.amount))
                    {
                        if (q == null) continue;
                        AddQuality(result, q.id, q.amount);
                    }
                }
            }
            return result;
        }

        /// <summary>性质列表里累加某个性质的数量（没有就新增）。</summary>
        internal static void AddQuality(List<CraftingQuality> list, string id, float amount)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].id == id)
                {
                    list[i].amount += amount;
                    return;
                }
            }
            list.Add(new CraftingQuality(id, amount));
        }

        /// <summary>格式化性质列表，例如 "切割 5 · 锤击 2"。</summary>
        internal static string FormatQualities(List<CraftingQuality> list, int max = 4)
        {
            if (list == null || list.Count == 0) return "-";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (CraftingQuality q in list)
            {
                if (q == null) continue;
                if (n >= max) { sb.Append(" …"); break; }
                if (n > 0) sb.Append(" · ");
                sb.Append(QualityName(q.id)).Append(' ').Append(q.amount.ToString("0.#"));
                n++;
            }
            return sb.ToString();
        }
    }
}
