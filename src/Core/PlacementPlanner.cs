using System.Collections.Generic;
using UnityEngine;

namespace OptimisedCraftingSystem
{
    /// <summary>
    /// 产物存放/装备的**预测与执行**。
    ///
    /// 原模组只做"事后存放"（`StorageLogic.AutoPickup` 在产物生成后重新挑容器），
    /// 玩家在按下合成前无法知道东西会去哪；而且它的 `CompareStorageDesirability`
    /// 里 `compare_storage_full` 的实现与文案相反（实际选最空的容器）。这里两头一起解决：
    ///   · `Predict*` —— 合成前就能算出来"这件产物会进哪个槽位 / 哪个容器 / 穿到哪"，
    ///     用于合成界面提示与独立面板；
    ///   · `Execute/ Pour` —— 合成时按同一套算法真的那么做（`ocs.store.honor` 控制），
    ///     预测与实际一致。
    ///
    /// 关键约束（来自 `Container.LoadItem` / `Body.AutoPickUpItem` 的原版实现）：
    ///   · 容器装载要求距离 &lt; 10，且**不能**嵌套（容器里的容器装不进去）；
    ///   · `CanHoldItem` = 单件重量上限 + 总重量上限 + 标签限制三者同时满足；
    ///   · 产物是"还没生成的物品"，所以对容器的适配判定必须自己复刻一份
    ///     （见 <see cref="Fits"/>），不能调用需要真实 `Item` 的 `CanHoldItem`。
    /// </summary>
    internal static class PlacementPlanner
    {
        internal enum Destination
        {
            None = 0,
            Slot,        // 身上的空槽位
            Container,   // 某个容器
            Wear,        // 穿戴到肢体（原版行为）
            Floor,       // 留在地上
            Bottle,      // 倒进已有液体容器
            TempBottle,  // 现场做一个临时瓶
        }

        internal struct Plan
        {
            internal Destination Kind;
            internal int Slot;
            internal Container Container;
            internal WaterContainerItem Bottle;   // 液体目标（Kind == Bottle 时有效）
            internal string Text;
            internal bool Valid;
        }

        // ————————————————————————————————————————————————————————————
        // 固体产物
        // ————————————————————————————————————————————————————————————

        /// <summary>预测连续 <paramref name="count"/> 件相同产物的去向（后一件会看到前一件占掉的重量）。</summary>
        internal static List<Plan> PlanSequence(ItemInfo stats, float condition, int count)
        {
            List<Plan> plans = new List<Plan>();
            if (stats == null || count <= 0) return plans;

            Dictionary<Container, float> reserved = new Dictionary<Container, float>();
            for (int i = 0; i < count; i++)
                plans.Add(PredictOne(stats, condition, reserved));
            return plans;
        }

        /// <summary>预测单件固体产物的去向（`reserved` 用于模拟同一批产物的累计占用）。</summary>
        internal static Plan PredictOne(ItemInfo stats, float condition, Dictionary<Container, float> reserved)
        {
            Plan p = default;
            if (stats == null) return p;

            Body body = CraftingScanner.PlayerBody;
            if (body == null) return p;

            // 玩家在"材料总览"界面里手动指定的容器优先（放得下才用，否则回退自动）
            if (CraftSession.ManualSolidSet && CraftSession.ManualSolidContainer != null)
            {
                Container manual = CraftSession.ManualSolidContainer;
                if (Fits(manual, stats, condition, reserved))
                {
                    Reserve(reserved, manual, CraftingScanner.WeightOf(stats, condition));
                    return Make(Destination.Container,
                        Loc.T("ocs.store.container", Label(manual), FreeSpace(manual)), manual);
                }
            }

            // 原版：带 noautopickup 标签的物品不自动捡起
            if (stats.HasTag("noautopickup"))
                return Make(Destination.Floor, Loc.T("ocs.store.floor"));

            float weight = CraftingScanner.WeightOf(stats, condition);

            // 可穿戴 → 原版会直接穿上（或替换同槽位的旧装备）
            if (stats.wearable)
            {
                Item worn = null;
                try { worn = body.GetWearableBySlotID(stats.wearSlotId); } catch { }
                string limb = LimbName(body, stats.desiredWearLimb);
                return Make(Destination.Wear, worn != null
                    ? Loc.T("ocs.store.wear.replace", limb, CraftingScanner.NameOf(worn))
                    : Loc.T("ocs.store.wear", limb));
            }

            if (ModSettings.StoreInContainersFirst)
            {
                Container c = BestContainer(stats, condition, reserved);
                if (c != null)
                {
                    Reserve(reserved, c, weight);
                    return Make(Destination.Container, Loc.T("ocs.store.container", Label(c), FreeSpace(c)), c);
                }
            }

            int? slot = null;
            try { slot = body.FirstEmptySlot(); } catch { }
            if (slot.HasValue)
                return MakeSlot(slot.Value);

            if (!ModSettings.StoreInContainersFirst)
            {
                Container c = BestContainer(stats, condition, reserved);
                if (c != null)
                {
                    Reserve(reserved, c, weight);
                    return Make(Destination.Container, Loc.T("ocs.store.container", Label(c), FreeSpace(c)), c);
                }
            }

            return Make(Destination.Floor, Loc.T("ocs.store.none"));
        }

        /// <summary>按预测结果真的把产物放好；成功返回 true（调用方就不再走原版逻辑）。</summary>
        internal static bool Execute(Item item, Plan plan)
        {
            if (item == null || !plan.Valid) return false;
            Body body = CraftingScanner.PlayerBody;
            if (body == null) return false;

            try
            {
                switch (plan.Kind)
                {
                    case Destination.Container:
                        if (plan.Container != null && plan.Container.CanHoldItem(item))
                        {
                            plan.Container.LoadItem(item);
                            // LoadItem 失败时只会弹 alert，所以再确认一次父子关系
                            if (item.ParentContainer() == plan.Container)
                            {
                                CraftSession.NoteStored(Label(plan.Container));
                                return true;
                            }
                        }
                        return false;

                    case Destination.Slot:
                        if (plan.Slot >= 0 && plan.Slot < body.slots.Length && body.GetItem(plan.Slot) == null)
                        {
                            body.PickUpItem(item, plan.Slot, force: true);
                            if (body.GetItem(plan.Slot) == item)
                            {
                                CraftSession.NoteStored(Loc.T("ocs.store.slot", plan.Slot));
                                return true;
                            }
                        }
                        return false;

                    case Destination.Floor:
                        CraftSession.NoteStored(Loc.T("ocs.store.floor"));
                        return true;   // 什么都不做 = 留在原地

                    default:
                        return false;  // 穿戴等交给原版
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning("[Place] 存放失败：" + e.Message);
                return false;
            }
        }

        // ————————————————————————————————————————————————————————————
        // 液体产物
        // ————————————————————————————————————————————————————————————

        /// <summary>预测液体产物的去向。</summary>
        internal static Plan PredictLiquid(string liquidId, float ml)
        {
            List<WaterContainerItem> full = LiquidCandidates(liquidId, ml, allowPartial: false);
            WaterContainerItem best = BestBottle(full, liquidId);
            if (best != null)
                return Make(Destination.Bottle, Loc.T("ocs.store.bottle", BottleName(best), best.SpaceLeft), null, best);

            List<WaterContainerItem> any = LiquidCandidates(liquidId, ml, allowPartial: true);
            if (any.Count > 0)
                return Make(Destination.Bottle, Loc.T("ocs.store.spread", BottleName(any[0]), any.Count), null, BestBottle(any, liquidId));

            if (ModSettings.AllowTempBottleTarget)
                return Make(Destination.TempBottle, Loc.T("ocs.store.tempbottle.warn"));

            return Make(Destination.None, Loc.T("ocs.store.lost"));
        }

        /// <summary>
        /// 把液体倒进目标瓶，余量依次尝试：其它合适的瓶子（"余量续瓶"）→ 自建临时瓶 → 原版挑中的瓶子。
        /// 返回实际倒出的总量。调用方负责防重入。
        /// </summary>
        internal static float Pour(string liquidId, float ml, WaterContainerItem preferred, WaterContainerItem fallback)
        {
            float total = 0f;
            float left = ml;

            if (preferred != null && BlockedReason(preferred, liquidId).Length == 0)
            {
                float got = Apply(preferred, liquidId, left);
                total += got;
                left -= got;
                if (got > 0f) CraftSession.NoteStored(BottleName(preferred));
            }

            if (left > 0.5f)
            {
                foreach (WaterContainerItem bottle in LiquidCandidates(liquidId, left, allowPartial: true))
                {
                    if (bottle == null || bottle == preferred) continue;
                    float got = Apply(bottle, liquidId, left);
                    if (got <= 0f) continue;
                    total += got;
                    left -= got;
                    CraftSession.NoteStored(BottleName(bottle));
                    if (left <= 0.5f) break;
                }
            }

            if (left > 0.5f && ModSettings.AllowTempBottleTarget)
            {
                float got = CreateTempBottle(liquidId, left);
                total += got;
                left -= got;
            }

            if (left > 0.5f && fallback != null)
            {
                if (SkipFavorited(fallback))
                {
                    // 原版挑中的那个瓶子正好是玩家收藏的：宁可不倒，也不动它 —— 这部分液体就此流失。
                    // 去向不是偷偷变的：`PredictLiquid` 用的是同一套 `LiquidCandidates`，合成前界面上
                    // 那一行就会显示「临时瓶」或「液体将流失」。
                    Plugin.Evidence("favorited:" + liquidId,
                        "收藏保护：" + BottleName(fallback) + " 已收藏，为了不动它，" +
                        CraftText.LiquidName(liquidId) + " " + Loc.T("ocs.unit.ml", left) + " 没有存放。");
                }
                else
                {
                    float got = Apply(fallback, liquidId, left);
                    total += got;
                    left -= got;
                    CraftSession.NoteStored(BottleName(fallback));
                }
            }

            return total;
        }

        private static float Apply(WaterContainerItem bottle, string liquidId, float ml)
        {
            if (bottle == null || ml <= 0f) return 0f;
            if (SkipFavorited(bottle)) return 0f;   // 最后一道闸：任何调用路径都不许往收藏的容器里倒
            try
            {
                if (bottle.SpaceLeft <= 0f) return 0f;
                return bottle.AddLiquid(liquidId, ml);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning("[Pour] " + e.Message);
                return 0f;
            }
        }

        /// <summary>
        /// 现场做一个临时瓶装液体。
        /// **与原版一致**：临时瓶一律 `Object.Destroy(go, 300f)` —— **5 分钟后连瓶子带里面剩的液体一起消失**
        /// （原版行为见 `RecipeResult.cs:59-64`），所以合成完要尽快把液体倒进正式容器。
        /// 唯一的例外：一滴都没装进去的空瓶（说明这次没用到它）30 秒后就清掉，不留垃圾。
        /// </summary>
        /// <param name="note">
        /// 是否记进本次合成的"已存放"清单（决定 `[合成]` 日志箭头右边写什么、以及结束时的提示）。
        /// 正常倒液体时为 true；**从即将被消耗掉的瓶子里抢救液体**时为 false ——
        /// 那种情况下临时瓶不是产物的落点，写进去会让"→ 临时瓶子"看起来像是产物的去处。
        /// </param>
        internal static float CreateTempBottle(string liquidId, float ml, bool note = true)
        {
            Body body = CraftingScanner.PlayerBody;
            if (body == null) return 0f;

            try
            {
                GameObject go = Utils.Create("craftingbottle", body.transform.position, 0f);
                if (go == null) return 0f;

                WaterContainerItem wc = go.GetComponent<WaterContainerItem>();
                Item it = go.GetComponent<Item>();
                if (wc == null || it == null) { Object.Destroy(go); return 0f; }

                float got = wc.AddLiquid(liquidId, ml);
                it.condition = wc.Capacity > 0f ? wc.CurrentTotal / wc.Capacity : it.condition;

                if (got <= 0f)
                {
                    Object.Destroy(go, 30f);
                    Plugin.Evidence("tempbottle.empty",
                        "临时瓶：做了一个但一滴都没装进去，30 秒后清掉（不占地方）。");
                    return 0f;
                }

                // 与原版一致：5 分钟后销毁，瓶中液体一并消失（`RecipeResult.cs:64`）
                Object.Destroy(go, 300f);

                body.AutoPickUpItem(it);   // 走原版捡起（我们的 AutoPickUpItem 前缀在倒液体期间会让路）
                if (note) CraftSession.NoteStored(BottleName(wc));
                Plugin.Evidence("tempbottle:" + liquidId,
                    "临时瓶：现场做了一个，装入 " + CraftText.LiquidName(liquidId) + " " +
                    Loc.T("ocs.unit.ml", got) + "；按原版规则 300 秒后连瓶带液体一起销毁。");
                return got;
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning("[TempBottle] " + e.Message);
                return 0f;
            }
        }

        /// <summary>身上（含容器内）可以被倒进液体的容器；`allowPartial` 为真时允许只装一部分。</summary>
        internal static List<WaterContainerItem> LiquidCandidates(string liquidId, float ml, bool allowPartial)
        {
            List<WaterContainerItem> list = new List<WaterContainerItem>();
            Body body = CraftingScanner.PlayerBody;
            if (body == null) return list;

            foreach (Item it in CraftingScanner.Gather(includeFloor: false))
            {
                if (it == null) continue;
                WaterContainerItem wc = it.GetComponent<WaterContainerItem>();
                if (wc == null || wc.Capacity <= 0f) continue;
                if (SkipFavorited(wc)) continue;   // 收藏的容器不接液体（`ocs.liquid.skip_favorited`）

                if (!ModSettings.CanFillNewBottles && wc.CurrentTotal <= 0f) continue;
                if (allowPartial ? wc.SpaceLeft <= 0.5f : wc.SpaceLeft < ml) continue;

                bool hasSame = wc.HasLiquid(liquidId);
                int others = OtherLiquids(wc, liquidId);
                if (others > 0)
                {
                    if (!ModSettings.CanMixLiquids) continue;
                    if (!hasSame) continue;   // 沿用原模组规则：混装瓶只接受它已有的那种液体
                }

                list.Add(wc);
            }
            return list;
        }

        /// <summary>
        /// 手动选目标瓶用的**完整**列表：身上（含容器内、穿戴中）与手边地面上的所有液体容器，
        /// **不做任何可用性过滤**（玩家要求"选择液体放置时应该显示所有瓶子"）。
        /// 「自动」预测仍然走过滤后的 `LiquidCandidates`，两条路径互不影响：
        /// 被自动跳过的瓶子照样列出来、并写明原因，但整行置灰点不动（见 `BlockedReason`）——
        /// 「看得见」和「绕得过设置」是两件事。能点的那些，点谁就用谁。
        /// </summary>
        internal static List<WaterContainerItem> AllBottles()
        {
            List<WaterContainerItem> list = new List<WaterContainerItem>();
            try
            {
                foreach (Item it in CraftingScanner.Gather(includeFloor: true))
                {
                    if (it == null) continue;
                    WaterContainerItem wc = it.GetComponent<WaterContainerItem>();
                    if (wc == null || wc.Capacity <= 0f) continue;
                    if (!list.Contains(wc)) list.Add(wc);
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning("[Bottles] 收集瓶子列表失败：" + e.Message);
            }
            return list;
        }

        /// <summary>候选表里一整行的文本：名称(填充%)（内容物 · 余量 N mL）＋ 位置 ＋"自动为什么不选它"。</summary>
        internal static string BottleDetail(WaterContainerItem wc, string liquidId)
        {
            if (wc == null) return "?";

            string text = Loc.T("ocs.store.bottle.contents", BottleName(wc), ContentsText(wc), Mathf.Max(0f, wc.SpaceLeft));
            try
            {
                Item it = wc.GetComponent<Item>();
                if (it != null) text += "  " + CraftingScanner.WhereOf(it);
            }
            catch { }

            string why = BlockedReason(wc, liquidId);
            return string.IsNullOrEmpty(why) ? text : text + "  " + why;
        }

        /// <summary>瓶里现在装着什么（"空" / "脂肪 50 mL" / "水 20 mL + 脂肪 10 mL"）。</summary>
        internal static string ContentsText(WaterContainerItem wc)
        {
            if (wc == null || wc.stack == null) return Loc.T("ocs.store.contents.empty");

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (LiquidStack s in wc.stack)
            {
                if (s == null || s.amount <= 0f) continue;
                if (sb.Length > 0) sb.Append(" + ");
                sb.Append(CraftText.LiquidName(s.liquidId)).Append(' ').Append(Loc.T("ocs.unit.ml", s.amount));
            }
            return sb.Length == 0 ? Loc.T("ocs.store.contents.empty") : sb.ToString();
        }

        /// <summary>
        /// 这一瓶会被「自动」跳过的原因（没问题就返回空串）。
        /// 判定条件与 `LiquidCandidates` 的过滤逐条对应，所以「非空」＝「自动不会选它」。
        /// 候选表用它在整行上**置灰**（否则手选等于绕过 `CanMixLiquids`/`CanFillNewBottles`/容量这些设置），
        /// `Pour` 的手动分支也拿它兜一道，防「选中之后瓶子状态又变了」。
        /// </summary>
        internal static string BlockedReason(WaterContainerItem wc, string liquidId)
        {
            if (wc == null) return string.Empty;
            if (SkipFavorited(wc)) return Loc.T("ocs.store.bottle.favorited");

            if (wc.SpaceLeft <= 0.5f) return Loc.T("ocs.store.bottle.full");
            if (wc.CurrentTotal <= 0f && !ModSettings.CanFillNewBottles) return Loc.T("ocs.store.bottle.newempty");

            bool hasSame = wc.HasLiquid(liquidId);
            if (OtherLiquids(wc, liquidId) > 0 && (!ModSettings.CanMixLiquids || !hasSame))
                return Loc.T("ocs.store.bottle.mix");
            return string.Empty;
        }

        /// <summary>瓶子之间的优劣（返回 &lt; 0 表示 x 更好）：不混装 → 已有同类 → 自重/容量比小。</summary>
        internal static int CompareBottle(WaterContainerItem x, WaterContainerItem y, string liquidId)
        {
            if (x == null || y == null) return x == y ? 0 : (x == null ? 1 : -1);

            if (ModSettings.CompareLiquidUnmixed)
            {
                int ox = OtherLiquids(x, liquidId), oy = OtherLiquids(y, liquidId);
                if (ox != oy) return ox.CompareTo(oy);
            }
            if (ModSettings.CompareLiquidStacking)
            {
                float sx = x.AmountOf(liquidId), sy = y.AmountOf(liquidId);
                if (!Mathf.Approximately(sx, sy)) return sy.CompareTo(sx);
            }
            if (ModSettings.CompareLiquidWeightRatio)
            {
                float rx = Ratio(x), ry = Ratio(y);
                if (!Mathf.Approximately(rx, ry)) return rx.CompareTo(ry);
            }
            return 0;
        }

        private static float Ratio(WaterContainerItem wc)
        {
            Item it = wc.GetComponent<Item>();
            float cap = wc.Capacity;
            if (it == null || cap <= 0.0001f) return float.MaxValue;
            return it.totalWeight / cap;
        }

        /// <summary>这件液体容器是不是被玩家打了星标（收藏），见 `ModSettings.SkipFavoritedBottles`。</summary>
        internal static bool Favorited(WaterContainerItem wc)
        {
            try
            {
                Item it = wc != null ? wc.GetComponent<Item>() : null;
                return it != null && it.favourited;
            }
            catch { return false; }
        }

        /// <summary>
        /// 「收藏的容器不装液体」这条规则此刻对这件容器生效吗。
        /// 之所以要单独包一层：调用点有四处在判定（`LiquidCandidates` / `BlockedReason` / `Apply` / `Pour` 的兜底分支），
        /// 全都必须与设置项同步，写散了迟早漏一个。
        /// </summary>
        private static bool SkipFavorited(WaterContainerItem wc)
        {
            return ModSettings.SkipFavoritedBottles && Favorited(wc);
        }

        private static int OtherLiquids(WaterContainerItem wc, string liquidId)
        {
            if (wc?.stack == null) return 0;
            int n = 0;
            foreach (LiquidStack s in wc.stack)
            {
                if (s != null && s.liquidId != liquidId) n++;
            }
            return n;
        }

        private static WaterContainerItem BestBottle(List<WaterContainerItem> list, string liquidId)
        {
            if (list == null || list.Count == 0) return null;
            WaterContainerItem best = list[0];
            for (int i = 1; i < list.Count; i++)
            {
                if (CompareBottle(list[i], best, liquidId) < 0) best = list[i];
            }
            return best;
        }

        // ————————————————————————————————————————————————————————————
        // 容器适配（复刻 Container.CanHoldItem，用于尚未生成的产物）
        // ————————————————————————————————————————————————————————————

        /// <summary>`Container.CanHoldItem` 的等价实现（可以传"还没生成的产物"的信息）。</summary>
        internal static bool Fits(Container c, ItemInfo stats, float condition, Dictionary<Container, float> reserved = null, float extra = 0f)
        {
            if (c == null || stats == null) return false;

            float w = CraftingScanner.WeightOf(stats, condition);
            if (w > c.maxWeightPerItem) return false;

            float used = 0f;
            try { used = c.GetHoldingWeight(); } catch { }
            if (reserved != null && reserved.TryGetValue(c, out float r)) used += r;
            used += extra;
            if (used + w > c.maxWeight) return false;

            if (c.tagRestriction != null && c.tagRestriction.Length != 0)
            {
                string[] tags = stats.GetTags();
                if (tags == null || tags.Length == 0) return false;
                bool ok = false;
                foreach (string t in tags)
                {
                    if (string.IsNullOrEmpty(t)) continue;
                    foreach (string r2 in c.tagRestriction)
                    {
                        if (r2 == t) { ok = true; break; }
                    }
                    if (ok) break;
                }
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>身上所有能装下这件产物的容器（含穿戴中的背包）。</summary>
        internal static List<Container> ContainerCandidates(ItemInfo stats, float condition, Dictionary<Container, float> reserved = null, float extra = 0f)
        {
            List<Container> list = new List<Container>();
            Body body = CraftingScanner.PlayerBody;
            if (body == null) return list;

            try
            {
                foreach (Item it in body.GetSurfaceInventoryItems())
                {
                    if (it == null) continue;
                    Container c = it.container;
                    if (c == null || list.Contains(c)) continue;
                    if (!Fits(c, stats, condition, reserved, extra)) continue;
                    list.Add(c);
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning("[Container] " + e.Message);
            }
            return list;
        }

        internal static Container BestContainer(ItemInfo stats, float condition, Dictionary<Container, float> reserved = null)
        {
            List<Container> list = ContainerCandidates(stats, condition, reserved);
            if (list.Count == 0) return null;

            Container best = list[0];
            for (int i = 1; i < list.Count; i++)
            {
                if (CompareContainer(list[i], best, stats, condition) < 0) best = list[i];
            }
            return best;
        }

        /// <summary>
        /// 容器优劣（返回 &lt; 0 表示 x 更好）。顺序与原模组的比较器链一一对应，
        /// 但修掉了 `compare_storage_full` 的反向实现（它文案是"优先更满的容器"，
        /// 实际却用剩余空间升序 + `Last()` 选了最空的）。
        /// </summary>
        internal static int CompareContainer(Container x, Container y, ItemInfo stats, float condition)
        {
            if (x == null || y == null) return x == y ? 0 : (x == null ? 1 : -1);
            int r;

            if (ModSettings.CompareStorageType)
            {
                bool mx = Matches(x, stats), my = Matches(y, stats);
                r = Bool(my, mx);            // 标签匹配的优先
                if (r != 0) return r;
            }
            if (ModSettings.CompareStorageReduction)
            {
                r = x.encumberanceMult.CompareTo(y.encumberanceMult);   // 减重更好的优先
                if (r != 0) return r;
            }
            if (ModSettings.CompareStorageFull)
            {
                r = FreeSpace(x).CompareTo(FreeSpace(y));               // 更满的优先（剩余空间小）
                if (r != 0) return r;
            }
            if (ModSettings.CompareStorageCapacity)
            {
                r = y.maxWeight.CompareTo(x.maxWeight);                 // 容量大的优先
                if (r != 0) return r;
            }
            if (ModSettings.CompareStorageBestCondition)
            {
                r = ContainerCondition(y).CompareTo(ContainerCondition(x));   // 容器状态好的优先
                if (r != 0) return r;
            }
            return 0;
        }

        private static bool Matches(Container c, ItemInfo stats)
        {
            if (c.tagRestriction == null || c.tagRestriction.Length == 0) return false;
            string[] tags = stats?.GetTags();
            if (tags == null) return false;
            foreach (string t in tags)
            {
                if (string.IsNullOrEmpty(t)) continue;
                foreach (string r in c.tagRestriction)
                {
                    if (r == t) return true;
                }
            }
            return false;
        }

        private static float ContainerCondition(Container c)
        {
            Item it = c.GetComponent<Item>();
            return it != null ? it.condition : 1f;
        }

        private static int Bool(bool a, bool b) => a == b ? 0 : (a ? 1 : -1);

        internal static float FreeSpace(Container c)
        {
            try { return Mathf.Max(0f, c.maxWeight - c.GetHoldingWeight()); }
            catch { return 0f; }
        }

        // ————————————————————————————————————————————————————————————
        // 显示辅助
        // ————————————————————————————————————————————————————————————

        internal static string Label(Container c)
        {
            if (c == null) return "?";
            Item it = c.GetComponent<Item>();
            return it != null ? CraftingScanner.NameOf(it) : "?";
        }

        internal static string BottleName(WaterContainerItem wc)
        {
            if (wc == null) return "?";
            Item it = wc.GetComponent<Item>();
            if (it == null) return "?";
            // 瓶子：condition ≡ 填充率（WaterContainerItem.UpdateCondition），带上百分比更有用
            return CraftingScanner.NameOf(it) + " (" + Mathf.RoundToInt(it.condition * 100f) + "%)";
        }

        private static string LimbName(Body body, string limbId)
        {
            try
            {
                Limb l = body.LimbByName(limbId);
                if (l != null && !string.IsNullOrEmpty(l.shortName)) return l.shortName;
            }
            catch { }
            return string.IsNullOrEmpty(limbId) ? "?" : limbId;
        }

        private static void Reserve(Dictionary<Container, float> reserved, Container c, float weight)
        {
            if (reserved == null) return;
            reserved.TryGetValue(c, out float cur);
            reserved[c] = cur + weight;
        }

        internal static Plan Make(Destination kind, string text, Container c = null, WaterContainerItem bottle = null)
        {
            return new Plan { Kind = kind, Text = text, Container = c, Bottle = bottle, Valid = kind != Destination.None };
        }

        private static Plan MakeSlot(int slot)
        {
            Body body = CraftingScanner.PlayerBody;
            bool hand = body?.slots != null && slot >= 0 && slot < body.slots.Length
                        && body.slots[slot] != null && body.slots[slot].isHand;
            return new Plan
            {
                Kind = Destination.Slot,
                Slot = slot,
                Valid = true,
                Text = hand ? Loc.T("ocs.store.hand", slot) : Loc.T("ocs.store.slot", slot),
            };
        }
    }
}
