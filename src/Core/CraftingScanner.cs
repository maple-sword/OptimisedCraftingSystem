using System.Collections.Generic;
using UnityEngine;

namespace OptimisedCraftingSystem
{
    /// <summary>物品相对玩家的位置，用于排序（"优先用哪里的材料"）与界面显示。</summary>
    internal enum ItemPlace
    {
        Unknown = 0,
        Body = 1,       // 身上槽位（含手中）
        Worn = 2,       // 已穿戴
        Container = 3,  // 装在容器（背包/箱子）内部
        Floor = 4,      // 手边地面上（10 米内、可拾取）
    }

    /// <summary>
    /// 材料扫描器 —— 本模组的性能核心，也是原模组最严重的卡顿来源的解药。
    ///
    /// 原模组 BetterInventoryLogic 的问题：每次调用 GetItemsForRecipe 都会
    ///   ① 遍历身上所有物品（含容器内容物），② 做一次 Physics2D.OverlapCircleAll 地面扫描。
    /// 而 PlayerCamera 每帧要为**每个可见配方**调用一次它（132 个配方 → 每帧上百次物理查询）。
    ///
    /// 这里按「帧号 + 是否含地面」缓存：同一帧内无论被多少配方、多少界面代码调用，
    /// 真正的扫描只做一次。返回给调用方的**始终是新列表**，调用方可以随意增删，
    /// 不会污染缓存（原模组就是因为共享了同一个列表，才不得不每次 .ToList() 复制）。
    /// </summary>
    internal static class CraftingScanner
    {
        /// <summary>地面扫描半径：与原版 Recipe.GetItemsForRecipeThorough 保持一致。</summary>
        internal const float FloorRadius = 10f;

        private static int _scanFrame = -1;
        private static bool _scanWithFloor;
        private static List<Item> _scanCache;
        private static int _floorMask = -1;

        private static int _placeFrame = -1;
        private static readonly Dictionary<Item, ItemPlace> _places = new Dictionary<Item, ItemPlace>();

        /// <summary>
        /// 收集当前可用于合成的全部物品（身上 + 容器内容物 + 可穿戴；可选含手边地面）。
        /// 返回新列表，调用方随意修改。
        /// </summary>
        internal static List<Item> Gather(bool includeFloor)
        {
            if (_scanCache != null && _scanFrame == Time.frameCount && _scanWithFloor == includeFloor)
                return new List<Item>(_scanCache);

            List<Item> list = new List<Item>();
            Body body = PlayerBody;
            if (body != null)
            {
                try
                {
                    List<Item> thorough = body.GetAllItemsThorough();
                    if (thorough != null) list.AddRange(thorough);
                }
                catch (System.Exception e)
                {
                    Plugin.Log?.LogWarning("[Scanner] 收集身上物品失败：" + e.Message);
                }

                if (includeFloor)
                {
                    try { AppendFloor(body, list); }
                    catch (System.Exception e) { Plugin.Log?.LogWarning("[Scanner] 地面扫描失败：" + e.Message); }
                }
            }

            _scanCache = list;
            _scanFrame = Time.frameCount;
            _scanWithFloor = includeFloor;
            return new List<Item>(list);
        }

        /// <summary>把 10 米内、通过可拾取判定（DoPickupCheck，静默模式）的地面物品加进列表。</summary>
        private static void AppendFloor(Body body, List<Item> list)
        {
            if (_floorMask == -1) _floorMask = LayerMask.GetMask("Item");
            if (_floorMask == 0) return;

            Collider2D[] cols = Physics2D.OverlapCircleAll(body.transform.position, FloorRadius, _floorMask);
            for (int i = 0; i < cols.Length; i++)
            {
                Collider2D col = cols[i];
                if (col == null) continue;
                Item item = col.GetComponent<Item>();
                if (item == null || list.Contains(item)) continue;

                bool ok;
                try { ok = body.DoPickupCheck(item, true); }
                catch { ok = false; }
                if (ok) list.Add(item);
            }
        }

        /// <summary>扫描缓存对应的帧号（调试用）。</summary>
        internal static int CachedFrame => _scanFrame;

        // ————————————————————————————————————————————————————————————
        // 位置判定（同样按帧缓存：一次 GetAllWearables + 一次槽位遍历就够全帧用）
        // ————————————————————————————————————————————————————————————

        private static void EnsurePlaces()
        {
            if (_placeFrame == Time.frameCount) return;
            _placeFrame = Time.frameCount;
            _places.Clear();

            Body body = PlayerBody;
            if (body == null) return;

            // 容器内容物（先标 Container，后面槽位/穿戴会覆盖成更"近"的位置）
            List<Item> thorough = null;
            try { thorough = body.GetAllItemsThorough(); } catch { }
            if (thorough != null)
            {
                foreach (Item it in thorough)
                {
                    if (it == null) continue;
                    if (_places.ContainsKey(it)) continue;
                    _places[it] = it.ParentContainer() != null ? ItemPlace.Container : ItemPlace.Body;
                }
            }

            try
            {
                List<Item> wearables = body.GetAllWearables();
                if (wearables != null)
                {
                    foreach (Item it in wearables)
                    {
                        if (it != null) _places[it] = ItemPlace.Worn;
                    }
                }
            }
            catch { }

            if (body.slots != null)
            {
                for (int i = 0; i < body.slots.Length; i++)
                {
                    Item it = null;
                    try { it = body.GetItem(i); } catch { }
                    if (it != null) _places[it] = ItemPlace.Body;
                }
            }
        }

        internal static ItemPlace PlaceOf(Item item)
        {
            if (item == null) return ItemPlace.Unknown;
            EnsurePlaces();
            if (_places.TryGetValue(item, out ItemPlace place)) return place;
            return item.ParentContainer() != null ? ItemPlace.Container : ItemPlace.Unknown;
        }

        internal static bool IsWorn(Item item) => PlaceOf(item) == ItemPlace.Worn;

        internal static bool IsInContainer(Item item) => PlaceOf(item) == ItemPlace.Container;

        internal static bool IsOnBody(Item item) => PlaceOf(item) == ItemPlace.Body;

        internal static bool IsOnFloor(Item item) => PlaceOf(item) == ItemPlace.Floor;

        /// <summary>物品是否已通过本次扫描确认为"手边地面"上的东西（列表里没扫到地面时用来兜底）。</summary>
        internal static bool MarkedAsFloor(Item item) => item != null && _floorSeen.Contains(item);

        private static readonly HashSet<Item> _floorSeen = new HashSet<Item>();

        /// <summary>记录一批"这一帧确实在地面上（且可拾取）"的物品，供 PlaceOf 兜底判定。</summary>
        internal static void NoteFloorItems(List<Item> items)
        {
            _floorSeen.Clear();
            if (items == null) return;
            foreach (Item it in items) if (it != null) _floorSeen.Add(it);
        }

        /// <summary>物品在哪个槽位（找不到返回 -1）。</summary>
        internal static int SlotIndexOf(Item item)
        {
            Body body = PlayerBody;
            if (body?.slots == null || item == null) return -1;
            for (int i = 0; i < body.slots.Length; i++)
            {
                Item it = null;
                try { it = body.GetItem(i); } catch { }
                if (it == item) return i;
            }
            return -1;
        }

        /// <summary>面向玩家的位置描述："背包槽 3" / "已穿戴" / "帆布包 内" / "手边地面"。</summary>
        internal static string WhereOf(Item item)
        {
            if (item == null) return "?";

            int slot = SlotIndexOf(item);
            if (slot >= 0)
            {
                Body body = PlayerBody;
                bool hand = body?.slots != null && slot < body.slots.Length && body.slots[slot] != null && body.slots[slot].isHand;
                return hand ? Loc.T("ocs.win.slot.hand", slot) : Loc.T("ocs.win.slot", slot);
            }

            if (IsWorn(item)) return Loc.T("ocs.win.worn");

            Container parent = item.ParentContainer();
            if (parent != null) return Loc.T("ocs.win.inside", NameOf(parent.GetComponent<Item>()));

            if (IsOnFloor(item) || MarkedAsFloor(item)) return Loc.T("ocs.win.floor");
            return Loc.T("ocs.win.floor");
        }

        /// <summary>
        /// 物品显示名（未识别物品按游戏规则隐藏真名）。
        ///
        /// **不要用 `Item.fullName`**：它带 TextMeshPro 的富文本标签
        /// （`<sprite index=22>` 收藏星标、`<color=#7d9ed1>` 湿标记、`<color=#787878>0%</color>` 耐久），
        /// 在 IMGUI 面板和日志里会原样显示成一堆标签（真机日志里出现过
        /// `垃圾袋 (&lt;color=#787878&gt;0%&lt;/color&gt;)`）。这里给纯文本，百分比由各自的列显示。
        /// </summary>
        internal static string NameOf(Item item)
        {
            if (item == null) return "?";
            try
            {
                ItemInfo stats = item.Stats;
                if (stats != null && !stats.rec.recognizable) return Locale.GetOther("unknownobject");

                string name = stats != null ? stats.fullName : item.id;
                if (string.IsNullOrEmpty(name)) name = item.id;
                if (item.isWet) name = Locale.GetOther("wet") + " " + name;
                return name;
            }
            catch { }
            return "?";
        }

        /// <summary>复刻 Item.totalWeight 的公式，用于"还没造出来的产物"预估重量。</summary>
        internal static float WeightOf(ItemInfo stats, float condition)
        {
            if (stats == null) return 0f;
            return stats.scaleWeightWithCondition ? Mathf.Lerp(0.1f, stats.weight, condition) : stats.weight;
        }

        /// <summary>临时瓶的物品 id（游戏 `Item.cs` 里注册的 `craftingbottle`，显示名「临时瓶子」）。</summary>
        internal const string TempBottleId = "craftingbottle";

        /// <summary>是不是现场造出来的临时瓶（装了液体 300 秒后连瓶带液体一起销毁）。</summary>
        internal static bool IsTempBottle(Item item)
        {
            return item != null && item.id == TempBottleId;
        }

        /// <summary>
        /// 这件东西是不是"装着液体的容器"。
        /// 这类物品**不能按 id 合并显示**：两个水壶可能装着不同液体/不同余量，合并成一行就分不出哪个是哪个了。
        /// </summary>
        internal static bool HoldsLiquid(Item item)
        {
            try
            {
                WaterContainerItem wc = item != null ? item.GetComponent<WaterContainerItem>() : null;
                if (wc == null || wc.stack == null) return false;
                foreach (LiquidStack s in wc.stack)
                {
                    if (s != null && s.amount > 0f) return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>玩家身体（拿不到就返回 null）。</summary>
        internal static Body PlayerBody => PlayerCamera.main != null ? PlayerCamera.main.body : null;
    }
}
