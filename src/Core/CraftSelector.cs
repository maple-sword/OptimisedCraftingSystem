using System.Collections.Generic;
using UnityEngine;

namespace OptimisedCraftingSystem
{
    /// <summary>候选材料的评分（字段越小越优先）。</summary>
    internal struct Candidate
    {
        internal Item Item;
        internal Consumption Use;
        internal bool Scarcity;    // 是别的槽位唯一可用的材料
        internal int Place;        // 位置偏好分（越小越好）
        internal float Cost;       // 这一次会损失多少价值
        internal float Fraction;   // 消耗掉整件材料的比重（越小 = 性质越充足）
        internal float Condition;  // 耐久（越小 = 越旧）
        internal float Value;      // 整件价值
        internal float Weight;     // 整件重量
        internal bool ConsumeUp;   // 这个槽位会把材料整件用掉（固体 + destroyItem）
        internal float Liquid;     // 整件用掉时瓶里还剩多少液体（mL），空的为 0
        internal bool Valid;
    }

    /// <summary>
    /// 智能选材 —— 本模组相对原模组的核心算法升级。
    ///
    /// 原模组的做法：把 7 个开关按固定顺序拼成一条比较器链，逐级比较布尔/数值，
    /// 取 `List.Sort` 后的 `Last()`。问题有三个：
    ///   ① 顺序是硬编码的，"优先地面"永远压过"优先低价值"，无法表达"成本相近才考虑位置"；
    ///   ② 只比 `condition` 和 `GetValue`，比不出"用一件 5 点性质的工具只掉 20% 耐久"这种真实性价比；
    ///   ③ 平局时依赖 `List.Sort` 的不稳定顺序，同一堆材料可能每次选不同的那件。
    ///
    /// 这里改成"**真实成本优先 + 成本相近才比偏好**"的两段式：
    ///   第 0 段：稀缺保护 —— 不用掉别的槽位唯一的材料（设置可关）；
    ///   第 1 段：真实成本 —— `QualityModel.Of` 复刻游戏真正扣掉的东西（整件价值 / 耐久×价值 / 倒掉的 mL 估值），
    ///           差距超过 25% 就直接选便宜的，这是"省钱"的主判据；
    ///   第 2 段：成本相近（≤25%）时，再按位置偏好 → 性质充足度 → 低价值 → 低耐久 依次比较，
    ///           全部相同则用 InstanceID 兜底，保证**结果稳定可复现**。
    ///
    /// 兼容性：全部输入都来自运行时游戏数据（`RecipeItem`、`ItemInfo.qualities`、
    /// `Liquids.Registry`），因此其它模组新增的物品、工具、液体、性质、配方都会**自动**参与计算，
    /// 不需要任何白名单。
    /// </summary>
    internal static class CraftSelector
    {
        /// <summary>成本"差不多"的相对带宽：差距在 25% 以内视为等价，改由偏好决定。</summary>
        private const float CostBandRatio = 0.25f;

        /// <summary>成本带宽的绝对下限，避免成本接近 0 时（比如不要钱的液体）带宽退化成 0。</summary>
        private const float CostBandMin = 0.1f;

        // ————————————————————————————————————————————————————————————
        // 候选筛选（与 RecipeItem.GetMatchingItem 的原版规则保持一致）
        // ————————————————————————————————————————————————————————————

        /// <summary>这件材料能不能满足这个槽位（含原版的收藏、ignoredId、有内容容器等硬规则）。</summary>
        internal static bool Usable(RecipeItem slot, Item item)
        {
            if (slot == null || item == null) return false;

            // 原版硬规则：收藏的物品永不消耗（教学关就是靠这个保护小刀）
            if (item.favourited) return false;

            // 原版硬规则：不能用产物的同类物品当材料（RecipeItem.ignoredId）
            if (!slot.isLiquid && !string.IsNullOrEmpty(slot.ignoredId) && item.id == slot.ignoredId) return false;

            // 原版硬规则：装了东西的容器不能当固体材料（液体槽不适用，因为它只倒液体）
            if (!slot.isLiquid)
            {
                Container c = item.container;
                if (c != null && c.itemCount > 0)
                {
                    // 本模组放开这一条（玩家需求）：「合成容器时被消耗的容器必须全空」是原版在
                    // `RecipeItem.GetMatchingItem`（`RecipeItem.cs:58-61`）里直接 `continue` 实现的。
                    // 只要这个槽位**会把容器整件用掉**、且设置开着，就让它参与选择 ——
                    // 里面的东西会在消耗前搬出来（`ContainerRescue`），合成完装进新容器、装不下掉地上。
                    // 不整件消耗的槽位（当工具用）仍按原版拒绝：那种情况下它本来也不该被"用掉"。
                    if (!ModSettings.CarryContainerContents || !ConsumesUp(slot)) return false;
                }
            }

            return QualityModel.Of(slot, item).Valid;
        }

        /// <summary>池子里所有能填这个槽位的材料（顺序即池子顺序，界面可原样展示）。</summary>
        internal static List<Item> Candidates(RecipeItem slot, List<Item> pool)
        {
            List<Item> list = new List<Item>();
            if (pool == null) return list;
            foreach (Item it in pool)
            {
                if (Usable(slot, it)) list.Add(it);
            }
            return list;
        }

        // ————————————————————————————————————————————————————————————
        // 整个配方的分配
        // ————————————————————————————————————————————————————————————

        /// <summary>
        /// 为一个配方的每个槽位挑材料。返回与 `recipe.items` 等长的列表，缺料的位置是 `null`
        /// （和原版 `GetItemsForRecipeThorough` 的约定一致）。
        /// </summary>
        internal static List<Item> SelectForRecipe(Recipe recipe, List<Item> pool)
        {
            List<Item> result = new List<Item>();
            if (recipe?.items == null) return result;

            int n = recipe.items.Count;
            List<Item>[] candidates = new List<Item>[n];
            for (int i = 0; i < n; i++)
            {
                candidates[i] = Candidates(recipe.items[i], pool);
                result.Add(null);
            }

            // 稀缺材料：某个槽位只有这一个候选，且该槽位会把它用掉 → 别处不要再抢
            HashSet<Item> scarce = new HashSet<Item>();
            if (ModSettings.ReserveScarce)
            {
                for (int i = 0; i < n; i++)
                {
                    if (candidates[i].Count != 1) continue;
                    if (!ConsumesUp(recipe.items[i])) continue;
                    scarce.Add(candidates[i][0]);
                }
            }

            // 先满足"最挑剔"的槽位（候选最少者优先），避免先到先得吃掉别人的唯一材料
            List<int> order = new List<int>(n);
            for (int i = 0; i < n; i++) order.Add(i);
            order.Sort((a, b) => candidates[a].Count.CompareTo(candidates[b].Count));

            HashSet<Item> taken = new HashSet<Item>();
            foreach (int i in order)
            {
                RecipeItem slot = recipe.items[i];
                Item best = null;
                Candidate bestScore = default;

                foreach (Item it in candidates[i])
                {
                    if (taken.Contains(it)) continue;
                    Candidate sc = Score(slot, it, scarce);
                    if (!sc.Valid) continue;
                    if (best == null || Compare(sc, bestScore) < 0)
                    {
                        best = it;
                        bestScore = sc;
                    }
                }

                result[i] = best;
                if (best != null && ConsumesUp(slot)) taken.Add(best);
            }

            return result;
        }

        /// <summary>这个槽位是否会把材料彻底用掉（固体 + destroyItem；液体只倒一部分，瓶子还在）。</summary>
        internal static bool ConsumesUp(RecipeItem slot)
        {
            return slot != null && slot.destroyItem && !slot.isLiquid;
        }

        /// <summary>在给定池子里为单个槽位挑最优（界面"换一个"用）。</summary>
        internal static Item Best(RecipeItem slot, List<Item> pool, HashSet<Item> scarce = null)
        {
            Item best = null;
            Candidate bestScore = default;
            foreach (Item it in pool)
            {
                if (!Usable(slot, it)) continue;
                Candidate sc = Score(slot, it, scarce);
                if (!sc.Valid) continue;
                if (best == null || Compare(sc, bestScore) < 0)
                {
                    best = it;
                    bestScore = sc;
                }
            }
            return best;
        }

        // ————————————————————————————————————————————————————————————
        // 评分与比较
        // ————————————————————————————————————————————————————————————

        /// <summary>算出某件材料对某个槽位的评分明细。</summary>
        internal static Candidate Score(RecipeItem slot, Item item, HashSet<Item> scarce)
        {
            Candidate c = default;
            Consumption use = QualityModel.Of(slot, item);
            if (!use.Valid) return c;

            c.Valid = true;
            c.Item = item;
            c.Use = use;
            c.Scarcity = scarce != null && scarce.Contains(item);
            c.Cost = use.Value;
            c.Fraction = use.Fraction;
            c.Condition = item.condition;
            c.Value = QualityModel.CurrentValue(item);
            c.Weight = item.totalWeight;
            c.Place = PlaceScore(item);

            // 这个槽位会把材料整件用掉时，记下瓶里还剩多少液体：空的优先（见 `Compare` 的第 0.5 段）
            c.ConsumeUp = ConsumesUp(slot);
            if (c.ConsumeUp) c.Liquid = BottleMl(item);
            return c;
        }

        /// <summary>这件材料是液体容器时，里面一共有多少 mL（不是液体容器就返回 0）。</summary>
        private static float BottleMl(Item item)
        {
            if (item == null) return 0f;
            WaterContainerItem wc = item.GetComponent<WaterContainerItem>();
            return wc != null ? wc.CurrentTotal : 0f;
        }

        /// <summary>位置偏好分（越小越好）：按设置里"优先地面 / 优先身上 / 不用容器里 / 不用穿戴的"打分。</summary>
        private static int PlaceScore(Item item)
        {
            bool floor = CraftingScanner.IsOnFloor(item) || CraftingScanner.MarkedAsFloor(item);
            bool inContainer = CraftingScanner.IsInContainer(item);
            bool worn = CraftingScanner.IsWorn(item);
            bool onBody = !floor && !inContainer && !worn;

            int score = 0;
            if (ModSettings.PreferOnFloor && floor) score -= 2;
            if (ModSettings.PreferOnBody && onBody) score -= 2;
            if (ModSettings.PreferNotContainer && inContainer) score += 2;
            if (ModSettings.PreferNotWearable && worn) score += 2;
            return score;
        }

        /// <summary>两段式比较：返回值 &lt; 0 表示 x 更该被选中（成本优先，成本相近才比偏好）。</summary>
        internal static int Compare(Candidate x, Candidate y)
        {
            if (ModSettings.ReserveScarce && x.Scarcity != y.Scarcity) return x.Scarcity ? 1 : -1;

            // ——— 第 0 段：临时瓶里的"同类液体"先用掉 ———
            // 临时瓶装了液体 300 秒后会连瓶带液体一起销毁（`PlacementPlanner.CreateTempBottle`），
            // 里面的液体本来就是"注定要没的"，所以同类液体优先倒它 —— 这一条要压过下面的成本带。
            int fresh = PreferTempBottle(x, y);
            if (fresh != 0) return fresh;

            // ——— 第 0.5 段：要整件消耗掉的液体容器，优先挑里面液体最少的 ———
            // 玩家需求：「优先不消耗装有液体的瓶子」。液体是玩家的资源，不该因为"这个瓶子便宜"
            // 就把里面的水一起烧掉；空瓶天然最优先。真要消耗（只剩它有液体、别的都不合适）时，
            // 瓶里的液体会在消耗前被倒进现场做的临时瓶（`ContainerRescue.RescueLiquid`）。
            int dry = PreferEmpty(x, y);
            if (dry != 0) return dry;

            float maxCost = Mathf.Max(x.Cost, y.Cost);
            float band = Mathf.Max(CostBandMin, maxCost * CostBandRatio);
            bool similar = Mathf.Abs(x.Cost - y.Cost) <= band;

            // ——— 第 1 段：成本差得明显，直接选便宜的 ———
            if (!similar) return x.Cost.CompareTo(y.Cost);

            // ——— 第 2 段：成本相近，按玩家偏好 ———
            if (x.Place != y.Place) return x.Place.CompareTo(y.Place);

            if (ModSettings.PreferHighQuality && !Mathf.Approximately(x.Fraction, y.Fraction))
                return x.Fraction.CompareTo(y.Fraction);

            if (ModSettings.PreferLowValue && !Mathf.Approximately(x.Value, y.Value))
                return x.Value.CompareTo(y.Value);

            if (ModSettings.PreferLowCondition && !Mathf.Approximately(x.Condition, y.Condition))
                return x.Condition.CompareTo(y.Condition);

            // 全部相同：用成本、再用 InstanceID 兜底，保证同一堆材料每次都选同一件
            int r = x.Cost.CompareTo(y.Cost);
            if (r != 0) return r;
            if (x.Item != null && y.Item != null) return x.Item.GetInstanceID().CompareTo(y.Item.GetInstanceID());
            return 0;
        }

        /// <summary>
        /// 「同类液体优先消耗临时瓶里的」（玩家需求）：两边都要是**同一层液体**
        /// （`LiquidId` 相同，即真的会倒出同一种东西），且一件在临时瓶里、另一件不在时才生效。
        /// 返回 &lt; 0 表示 x 优先；不同液体、或都是/都不是临时瓶时返回 0，交给后面的成本判据。
        /// </summary>
        private static int PreferTempBottle(Candidate x, Candidate y)
        {
            if (x.Use.Kind != ConsumeKind.LiquidMl || y.Use.Kind != ConsumeKind.LiquidMl) return 0;
            if (string.IsNullOrEmpty(x.Use.LiquidId) || x.Use.LiquidId != y.Use.LiquidId) return 0;

            bool xt = CraftingScanner.IsTempBottle(x.Item);
            bool yt = CraftingScanner.IsTempBottle(y.Item);
            if (xt == yt) return 0;
            return xt ? -1 : 1;
        }

        /// <summary>
        /// 「优先消耗空瓶子」（玩家需求）：两边都会**被整件消耗掉**时，瓶里液体少的优先。
        /// 空的（0 mL）排在最前；两边差不多（0.5 mL 以内，比如都是空瓶）就返回 0 交给后面的成本判据。
        /// </summary>
        private static int PreferEmpty(Candidate x, Candidate y)
        {
            if (!ModSettings.PreferEmptyBottle) return 0;
            if (!x.ConsumeUp || !y.ConsumeUp) return 0;
            if (Mathf.Abs(x.Liquid - y.Liquid) <= 0.5f) return 0;
            return x.Liquid < y.Liquid ? -1 : 1;
        }

        // ————————————————————————————————————————————————————————————
        // 兼容模式：关闭 ocs.pick.smart 时用的"原模组顺序比较"（修正版）
        // ————————————————————————————————————————————————————————————

        /// <summary>
        /// 原模组的比较链，按同样的顺序执行，但修掉了原实现的三个 bug
        /// （OnBody/OnFloor 忘记乘方向符号、液体比较里 `break` 应为 `continue`、未注册液体用索引器会抛异常）。
        /// 返回值 &lt; 0 表示 x 更该被选中。
        /// </summary>
        internal static int LegacyCompare(Item x, Item y, RecipeItem slot)
        {
            int r;

            // 「优先消耗空瓶子」（与智能模式的 `PreferEmpty` 同一条规则，两种模式行为一致）
            if (ModSettings.PreferEmptyBottle && ConsumesUp(slot))
            {
                float lx = BottleMl(x);
                float ly = BottleMl(y);
                if (Mathf.Abs(lx - ly) > 0.5f) return lx < ly ? -1 : 1;
            }

            // 临时瓶里的同类液体优先用掉（与智能模式的 `PreferTempBottle` 同一条规则，两种模式行为一致）
            if (slot != null && slot.isLiquid)
            {
                string lx = QualityModel.LiquidOf(slot, x);
                string ly = QualityModel.LiquidOf(slot, y);
                if (!string.IsNullOrEmpty(lx) && lx == ly)
                {
                    r = Bool(CraftingScanner.IsTempBottle(y), CraftingScanner.IsTempBottle(x));
                    if (r != 0) return r;
                }
            }

            if (ModSettings.PreferOnFloor)
            {
                r = Bool(CraftingScanner.IsOnFloor(y), CraftingScanner.IsOnFloor(x));
                if (r != 0) return r;
            }
            if (ModSettings.PreferNotContainer)
            {
                r = Bool(CraftingScanner.IsInContainer(y), CraftingScanner.IsInContainer(x));
                if (r != 0) return r;
            }
            if (ModSettings.PreferNotWearable)
            {
                r = Bool(CraftingScanner.IsWorn(y), CraftingScanner.IsWorn(x));
                if (r != 0) return r;
            }
            if (ModSettings.PreferOnBody)
            {
                r = Bool(CraftingScanner.IsOnBody(x), CraftingScanner.IsOnBody(y));
                if (r != 0) return r;
            }
            if (ModSettings.PreferHighQuality)
            {
                r = MatchedQuality(slot, x).CompareTo(MatchedQuality(slot, y));
                if (r != 0) return r;
            }
            if (ModSettings.PreferLowValue)
            {
                r = QualityModel.CurrentValue(x).CompareTo(QualityModel.CurrentValue(y));
                if (r != 0) return r;
            }
            if (ModSettings.PreferLowCondition)
            {
                r = x.condition.CompareTo(y.condition);
                if (r != 0) return r;
            }
            return 0;
        }

        private static int Bool(bool a, bool b) => a == b ? 0 : (a ? 1 : -1);

        /// <summary>匹配到的性质点数（液体按该层的"每 mL × mL"计算）。</summary>
        private static float MatchedQuality(RecipeItem slot, Item item)
        {
            if (slot == null || item == null) return 0f;

            if (slot.isLiquid)
            {
                if (slot.specific) return 0f;   // specific 槽位只比 mL，不比性质
                WaterContainerItem wc = item.GetComponent<WaterContainerItem>();
                if (wc?.stack == null || slot.quality == null) return 0f;
                float best = 0f;
                foreach (LiquidStack st in wc.stack)
                {
                    if (st == null) continue;
                    if (!Liquids.Registry.TryGetValue(st.liquidId, out LiquidType type) || type == null) continue;
                    CraftingQuality q = Item.GetQualityThatMeetsCriteria(slot.quality, type.GetScaledQualities(st.amount));
                    if (q != null && q.amount > best) best = q.amount;
                }
                return best;
            }

            if (slot.specific || slot.quality == null || item.Stats == null) return 0f;
            CraftingQuality matched = Item.GetQualityThatMeetsCriteria(slot.quality, item.Stats.qualities);
            return matched?.amount ?? 0f;
        }
    }
}
