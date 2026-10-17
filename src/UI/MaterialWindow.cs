using System;
using System.Collections.Generic;
using UnityEngine;

namespace OptimisedCraftingSystem.UI
{
    /// <summary>
    /// 独立材料总览界面（默认热键 M，可在游戏设置里改）。
    ///
    /// 两个标签页：
    ///  · 材料总览：列出身上 + 脚边（10 米内）所有能用于合成的材料，按 id 堆叠成一行，
    ///    显示位置 / 提供的性质 / 耐久 / 价值 / 能用它做多少个配方；点一行看详情与配方清单。
    ///  · 当前配方：C 键界面里当前选中的配方，逐槽显示"会用哪一件材料、消耗多少、还能用几次"，
    ///    并允许玩家自己换材料（自由选择材料）与换产物的存放目标（自动 / 某个容器 / 临时瓶）。
    ///
    /// 为什么用 IMGUI：原版合成界面是 TMP + 场景预制体，往里塞动态控件既要重复挂 Button，
    /// 又会被 `RefreshCurrentlySelectedRecipe` 每次刷新时销毁重建（预览图标就是这么被重建的）。
    /// 独立窗口不受它影响，也不碰原版层级；界面文案走 DLL 内置的中英对照表，不受汉化包影响。
    /// </summary>
    internal static class MaterialWindow
    {
        private const int WindowId = 0x42434C01;
        private const float WinWidth = 880f;
        private const float WinHeight = 560f;

        private static bool _open;
        private static Rect _rect;
        private static bool _rectReady;
        private static int _tab;
        private static Vector2 _scroll;
        private static string _detailKey;
        private static int _detailInstance;
        private static bool _craftOpen;

        private static float _cacheTime = -99f;
        private static readonly Dictionary<int, int> RecipeCount = new Dictionary<int, int>();
        private static readonly Dictionary<int, List<Recipe>> RecipesFor = new Dictionary<int, List<Recipe>>();

        // 覆盖层（C 键界面左下角那块）每帧都要重画，而"挑材料 + 预测落点"是 O(槽位 × 候选) 的活。
        // 显示内容是静止的，没必要每帧重算 ⇒ 0.25 秒一次；按了 ◀/▶/自动/临时瓶 立刻重算（RefreshPanel）。
        private static float _compactTime = -99f;
        private static bool _compactDirty = true;
        private static int _compactRecipeIndex = -1;
        private static List<Item> _compactSelection;
        private static string _compactTarget;

        // 「点击更换」列表（覆盖层与 M 窗口共用同一套状态）：
        //   _pickSlot >= 0 表示正在给该槽位挑材料；_destOpen 表示正在挑存放目标。
        // 两者互斥，点开一个就收起另一个，避免屏幕上同时摊开两张表。
        private static int _pickSlot = -1;
        private static bool _destOpen;
        private static Vector2 _pickScroll;

        private const float RowH = 28f;       // 标题 / 槽位 / 存放行的高度预算
        private const float PickRowH = 24f;   // 候选列表每行
        private const int PickRowsMax = 6;    // 候选列表最多摊开几行，超出靠滚动条
        private const float CompactListWidth = 600f;   // 覆盖层里候选表的宽度

        /// <summary>覆盖层（画在 C 键界面右侧）的宽度：够放「序号+槽位 / 材料名▾ / 说明」和存放行。</summary>
        internal const float CompactWidth = 660f;

        private static Font _font;
        private static bool _fontTried;
        private static GUIStyle _title, _head, _label, _small, _button, _tabOn, _tabOff, _box;

        internal static bool IsOpen => _open;

        /// <summary>自检用：实际拿到的中文字体名（null 表示没找到，界面会退回默认字体）。</summary>
        internal static string FontName
        {
            get
            {
                Font f = ChineseFont();
                return f != null ? f.name : "无（中文可能显示为方框）";
            }
        }

        /// <summary>可写的显示状态（热键/设置里强制关闭用）。</summary>
        internal static bool Visible
        {
            get => _open;
            set
            {
                if (_open == value) return;
                _open = value;
                if (_open) _cacheTime = -99f;
            }
        }

        internal static void Toggle()
        {
            _open = !_open;
            if (_open) _cacheTime = -99f;
        }

        internal static void Close() => _open = false;

        /// <summary>合成界面开关时被调（用来在合成时刷新缓存，不必每帧重算）。</summary>
        internal static void OnCraftScreenToggled(bool open)
        {
            _craftOpen = open;
            _cacheTime = -99f;
        }

        private static float UiScale => Mathf.Clamp(Screen.height / 1080f, 0.85f, 1.8f);

        internal static void Draw()
        {
            if (!ModSettings.WindowEnabled)
            {
                _open = false;
                return;
            }

            if (!_open) return;

            Matrix4x4 prev = GUI.matrix;
            try
            {
                float s = UiScale;
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
                if (!_rectReady)
                {
                    _rect = new Rect(Mathf.Max(10f, Screen.width / s - WinWidth - 24f), 48f, WinWidth, WinHeight);
                    _rectReady = true;
                }
                EnsureStyles();
                _rect = GUI.Window(WindowId, _rect, DrawWindow, Loc.T("ocs.win.title"), _title);
            }
            catch (Exception e)
            {
                LogOnce(e);
            }
            finally
            {
                GUI.matrix = prev;
            }
        }

        private static bool _loggedError;

        private static void LogOnce(Exception e)
        {
            if (_loggedError) return;
            _loggedError = true;
            Plugin.Log?.LogWarning("[UI] 材料总览界面绘制失败（已停止重复报告）：" + e);
        }

        private static void DrawWindow(int id)
        {
            GUILayout.BeginVertical(_box);

            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("ocs.win.title"), _title, GUILayout.Width(220f));
            bool mats = GUILayout.Toggle(_tab == 0, "  " + Loc.T("ocs.win.tab.materials") + "  ", _tab == 0 ? _tabOn : _tabOff);
            bool rec = GUILayout.Toggle(_tab == 1, "  " + Loc.T("ocs.win.tab.recipes") + "  ", _tab == 1 ? _tabOn : _tabOff);
            if (mats && _tab != 0) { _tab = 0; _scroll = Vector2.zero; }
            if (rec && _tab != 1) { _tab = 1; _scroll = Vector2.zero; }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Loc.T("ocs.win.close"), _button, GUILayout.Width(70f))) _open = false;
            GUILayout.EndHorizontal();

            GUILayout.Label(Loc.T("ocs.win.hint"), _small);

            _scroll = GUILayout.BeginScrollView(_scroll);
            if (_tab == 0) DrawMaterialsTab();
            else DrawRecipeTab();
            GUILayout.EndScrollView();

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0f, 0f, WinWidth - 90f, 24f));
        }

        // ── 标签页 1：材料总览 ────────────────────────────────────────────────

        private struct Row
        {
            internal string Key;
            internal Item Rep;
            internal int Count;
            internal string Name;
            internal string Where;
        }

        private static void DrawMaterialsTab()
        {
            List<Item> pool = CraftingScanner.Gather(ModSettings.WindowIncludeFloor);
            if (pool == null || pool.Count == 0)
            {
                GUILayout.Label(Loc.T("ocs.win.empty"), _label);
                return;
            }

            RefreshCache(pool);
            List<Row> rows = BuildRows(pool);

            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("ocs.win.col.item"), _head, GUILayout.Width(250f));
            GUILayout.Label(Loc.T("ocs.win.col.where"), _head, GUILayout.Width(170f));
            GUILayout.Label(Loc.T("ocs.win.col.qualities"), _head, GUILayout.Width(220f));
            GUILayout.Label(Loc.T("ocs.win.col.uses"), _head, GUILayout.Width(70f));
            GUILayout.Label(Loc.T("ocs.win.col.matched"), _head, GUILayout.Width(70f));
            GUILayout.Label(" ", _head);
            GUILayout.EndHorizontal();

            foreach (Row row in rows)
            {
                if (row.Rep == null) continue;
                GUILayout.BeginHorizontal();

                string name = row.Count > 1 ? row.Name + " ×" + row.Count : row.Name;
                bool selected = row.Key == _detailKey;
                if (GUILayout.Button(name, selected ? _tabOn : _label, GUILayout.Width(250f)))
                {
                    _detailKey = row.Key;
                    _detailInstance = row.Rep.GetInstanceID();
                    _cacheTime = -99f;
                }
                GUILayout.Label(row.Where, _label, GUILayout.Width(170f));
                GUILayout.Label(SafeQualities(row.Rep), _label, GUILayout.Width(220f));
                GUILayout.Label(Mathf.RoundToInt(row.Rep.condition * 100f) + "%", _label, GUILayout.Width(70f));
                int n = 0;
                RecipeCount.TryGetValue(row.Rep.GetInstanceID(), out n);
                GUILayout.Label(n.ToString(), _label, GUILayout.Width(70f));
                GUILayout.Label(ItemInfoText(row.Rep), _small);
                GUILayout.EndHorizontal();
            }

            if (_detailInstance != 0)
            {
                GUILayout.Space(8f);
                DrawDetail(pool);
            }
        }

        private static string ItemInfoText(Item it)
        {
            try
            {
                string val = Loc.T("ocs.win.prop") + " " + QualityModel.CurrentValue(it) + "  " +
                             Loc.T("ocs.win.weight") + " " + CraftingScanner.WeightOf(it.Stats, it.condition).ToString("0.##") + " kg";
                Container c = it.container;
                if (c != null && c.itemCount > 0) val += "  " + Loc.T("ocs.tip.contents") + " " + c.itemCount;
                return val;
            }
            catch { return string.Empty; }
        }

        private static void DrawDetail(List<Item> pool)
        {
            Item it = null;
            foreach (Item x in pool)
                if (x != null && x.GetInstanceID() == _detailInstance) { it = x; break; }
            if (it == null) { _detailKey = null; _detailInstance = 0; return; }

            GUILayout.Label("— " + CraftingScanner.NameOf(it) + " —", _head);
            GUILayout.Label(CraftingScanner.WhereOf(it), _label);
            GUILayout.Label(Loc.T("ocs.win.col.qualities") + ": " + SafeQualities(it), _label);

            Container cont = it.container;
            if (cont != null)
            {
                GUILayout.Label(CraftText.ContainerContents(it), _small);
                GUILayout.Label(string.Format(Loc.T("ocs.tip.free.space"), Mathf.Max(0f, cont.maxWeight - cont.GetHoldingWeight())), _small);
            }

            List<Recipe> list;
            if (!RecipesFor.TryGetValue(it.GetInstanceID(), out list) || list == null || list.Count == 0)
            {
                GUILayout.Label(Loc.T("ocs.win.col.matched") + ": 0", _label);
                return;
            }

            GUILayout.Label(Loc.T("ocs.win.col.recipes") + ":", _head);
            GUILayout.BeginHorizontal();
            int perRow = 0;
            foreach (Recipe r in list)
            {
                string rn = SafeRecipeName(r);
                if (GUILayout.Button(rn, _button, GUILayout.Width(200f)))
                    SelectRecipe(r);
                if (++perRow % 4 == 0)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                }
            }
            GUILayout.EndHorizontal();
        }

        private static string SafeRecipeName(Recipe r)
        {
            try { return r != null ? r.simpleName : "?"; } catch { return "?"; }
        }

        private static string SafeQualities(Item it)
        {
            try
            {
                if (it == null || it.Stats == null) return string.Empty;
                return QualityModel.FormatQualities(it.Stats.qualities, 3);
            }
            catch { return string.Empty; }
        }

        /// <summary>
        /// 列表行的合并键。普通材料按 id 合并（受「合成界面堆叠显示相同材料」控制），
        /// **装着液体的容器改成按「物品 id + 液体组成」合并**：两个水壶装不同液体或不同余量时不能并成一行，
        /// 否则玩家分不出哪行是哪个瓶子（玩家反馈「不要堆叠液体容器」）；液体完全相同的仍可并成「×N」。
        /// </summary>
        private static string RowKey(Item it)
        {
            if (!ModSettings.StackItems) return "inst:" + it.GetInstanceID();

            if (CraftingScanner.HoldsLiquid(it))
            {
                WaterContainerItem wc = it.GetComponent<WaterContainerItem>();
                return "liq:" + it.id + ":" + PlacementPlanner.ContentsText(wc);
            }
            return "id:" + it.id;
        }

        private static List<Row> BuildRows(List<Item> pool)
        {
            List<Row> rows = new List<Row>();
            Dictionary<string, int> index = new Dictionary<string, int>();
            foreach (Item it in pool)
            {
                if (it == null || it.Stats == null) continue;
                string key = RowKey(it);
                int at;
                if (index.TryGetValue(key, out at))
                {
                    Row r = rows[at];
                    r.Count++;
                    rows[at] = r;
                    continue;
                }
                index[key] = rows.Count;
                rows.Add(new Row
                {
                    Key = key,
                    Rep = it,
                    Count = 1,
                    Name = CraftText.ContainerName(it),
                    Where = CraftingScanner.WhereOf(it)
                });
            }
            return rows;
        }

        /// <summary>缓存"哪件材料能用在哪些配方上"（0.5 秒或材料集合变化时重算，避免每帧 132×N 次判定）。</summary>
        private static void RefreshCache(List<Item> pool)
        {
            if (Time.unscaledTime - _cacheTime < 0.5f) return;
            _cacheTime = Time.unscaledTime;
            RecipeCount.Clear();
            RecipesFor.Clear();

            List<Recipe> all = null;
            try { all = Recipes.recipes; } catch { }
            if (all == null) return;

            foreach (Item it in pool)
            {
                if (it == null || it.Stats == null) continue;
                int id = it.GetInstanceID();
                List<Recipe> mine = new List<Recipe>();
                foreach (Recipe r in all)
                {
                    if (r == null || r.items == null) continue;
                    bool usable = false;
                    for (int i = 0; i < r.items.Count; i++)
                    {
                        if (r.items[i] != null && CraftSelector.Usable(r.items[i], it)) { usable = true; break; }
                    }
                    if (usable) mine.Add(r);
                }
                RecipeCount[id] = mine.Count;
                RecipesFor[id] = mine;
            }
        }

        private static void SelectRecipe(Recipe r)
        {
            try
            {
                PlayerCamera cam = PlayerCamera.main;
                if (cam == null || r == null) return;
                cam.selectedRecipe = r.index;
                cam.RefreshCurrentlySelectedRecipe();
                _tab = 1;
                _scroll = Vector2.zero;
            }
            catch (Exception e) { LogOnce(e); }
        }

        // ── 标签页 2：当前配方（换材料 / 换存放目标） ────────────────────────

        private static Recipe CurrentRecipe()
        {            try
            {
                PlayerCamera cam = PlayerCamera.main;
                List<Recipe> all = Recipes.recipes;
                if (cam == null || all == null || all.Count == 0) return null;
                int idx = Mathf.Clamp(cam.selectedRecipe, 0, all.Count - 1);
                return all[idx];
            }
            catch { return null; }
        }

        private static void DrawRecipeTab()
        {
            if (!_craftOpen)
                GUILayout.Label(Loc.T("ocs.win.empty"), _label);

            Recipe recipe = CurrentRecipe();
            if (recipe == null || recipe.items == null)
            {
                GUILayout.Label(Loc.T("ocs.win.empty"), _label);
                return;
            }

            List<Item> pool = CraftingScanner.Gather(true);
            List<Item> selection = null;
            try { selection = CraftSession.Select(recipe, pool); } catch (Exception e) { LogOnce(e); }

            GUILayout.Label("— " + SafeRecipeName(recipe) + " —", _head);

            for (int i = 0; i < recipe.items.Count; i++)
            {
                RecipeItem slot = recipe.items[i];
                if (slot == null) continue;
                Item chosen = selection != null && i < selection.Count ? selection[i] : null;
                List<Item> cands = CraftSelector.Candidates(slot, pool);

                GUILayout.BeginHorizontal();
                string label = Loc.T("ocs.win.col.needed") + " " + (i + 1) + ": " + CraftText.SlotLabel(slot) +
                               (QualityLabelText(slot).Length > 0 ? " · " + QualityLabelText(slot) : string.Empty);
                GUILayout.Label(label, _label, GUILayout.Width(300f));

                string matName = chosen != null ? CraftText.MaterialLabel(slot, chosen) : "—";
                // 这个槽位只有一种材料能满足时（植物纤维袋只要帆布 + 纤维绳就是典型），候选表里全是
                // 同类物品，换与不换没有区别：不给 ▾ 入口，也不留 ◀/▶/自动 三个没意义的按钮。
                bool canPick = CraftSelector.HasMaterialChoice(slot, pool);
                if (canPick)
                {
                    // 点材料名展开候选表（与覆盖层同一套状态）；◀/▶ 保留，习惯盲翻时仍可用
                    if (GUILayout.Button(matName + "  (" + cands.Count + ")  ▾", _pickSlot == i ? _tabOn : _button,
                                         GUILayout.Width(260f)))
                        TogglePick(i);
                }
                else
                {
                    SuppressPick(recipe.index, i);
                    GUILayout.Label(matName, _label, GUILayout.Width(260f));
                }

                string desc = chosen != null ? SafeDescribe(slot, chosen) : string.Empty;
                GUILayout.Label(desc, _small, GUILayout.Width(200f));

                if (canPick)
                {
                    if (GUILayout.Button("◀", _button, GUILayout.Width(30f)))
                        CyclePick(recipe, i, pool, -1);
                    if (GUILayout.Button("▶", _button, GUILayout.Width(30f)))
                        CyclePick(recipe, i, pool, +1);
                    if (GUILayout.Button(Loc.T("ocs.craft.auto"), _button, GUILayout.Width(110f)))
                    {
                        CraftSession.ClearPick(recipe.index, i);
                        RefreshPanel();
                    }
                }
                GUILayout.EndHorizontal();

                if (chosen != null && ModSettings.VerboseNumbers)
                    GUILayout.Label("    " + CraftingScanner.WhereOf(chosen), _small);

                if (_pickSlot == i) DrawPickList(recipe, i, pool, selection, 820f);
            }

            GUILayout.Space(10f);
            DrawDestination(recipe, pool);
        }

        private static string QualityLabelText(RecipeItem slot)
        {
            try { return CraftText.QualityLabel(slot); } catch { return string.Empty; }
        }

        private static string SafeDescribe(RecipeItem slot, Item it)
        {
            try { return QualityModel.Describe(slot, it); } catch { return string.Empty; }
        }

        private static void CyclePick(Recipe recipe, int slotIndex, List<Item> pool, int dir)
        {
            try
            {
                List<Item> cands = CraftSelector.Candidates(recipe.items[slotIndex], pool);
                if (cands.Count == 0) return;

                List<Item> selection = CraftSession.Select(recipe, pool);
                Item current = selection != null && slotIndex < selection.Count ? selection[slotIndex] : null;
                int at = current != null ? cands.IndexOf(current) : -1;
                int next = at < 0
                    ? (dir > 0 ? 0 : cands.Count - 1)
                    : ((at + dir) % cands.Count + cands.Count) % cands.Count;

                CraftSession.RememberPick(recipe.index, slotIndex, cands[next].GetInstanceID(), recipe.items[slotIndex]);
                RefreshPanel();
            }
            catch (Exception e) { LogOnce(e); }
        }

        private static void DrawDestination(Recipe recipe, List<Item> pool)
        {
            if (recipe.result == null) return;

            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("ocs.craft.store"), _head, GUILayout.Width(120f));
            string target;
            try { target = CraftText.ResultDestination(recipe); } catch { target = "?"; }
            // 点去向文字展开存放目标表（自动 / 临时瓶 / 各容器）
            if (GUILayout.Button(target + "  ▾", _destOpen ? _tabOn : _button, GUILayout.Width(420f)))
                ToggleDest();

            if (recipe.result.isLiquid)
            {
                List<WaterContainerItem> bottles = SafeBottles(recipe.result);
                if (GUILayout.Button("◀", _button, GUILayout.Width(30f))) CycleLiquid(bottles, -1, recipe.result.id);
                if (GUILayout.Button("▶", _button, GUILayout.Width(30f))) CycleLiquid(bottles, +1, recipe.result.id);
                if (GUILayout.Button(Loc.T("ocs.store.tempbottle"), _button, GUILayout.Width(150f)))
                {
                    CraftSession.ForceTempBottle = true;
                    CraftSession.ManualBottleInstance = 0;
                    ClosePickers();
                    RefreshPanel();
                }
            }
            else
            {
                List<Container> containers = SafeContainers(recipe, pool);
                if (GUILayout.Button("◀", _button, GUILayout.Width(30f))) CycleSolid(containers, -1);
                if (GUILayout.Button("▶", _button, GUILayout.Width(30f))) CycleSolid(containers, +1);
            }

            if (GUILayout.Button(Loc.T("ocs.craft.auto"), _button, GUILayout.Width(110f)))
            {
                CraftSession.ResetDestination();
                ClosePickers();
                RefreshPanel();
            }
            GUILayout.EndHorizontal();

            if (_destOpen) DrawDestList(recipe, pool, 820f);

            GUILayout.Label(Loc.T("ocs.craft.change"), _small);
        }

        /// <summary>
        /// 手动选瓶子的列表：**所有**液体容器（身上＋容器内＋穿戴中＋手边地面），不做可用性过滤。
        /// 玩家要求"选择液体放置时应该显示所有瓶子"；不能用时会在行尾说明原因，但仍可手动选中。
        /// （只影响手动选择／◀▶ 循环；「自动」预测仍走 `PlacementPlanner.PredictLiquid`。）
        /// </summary>
        private static List<WaterContainerItem> SafeBottles(RecipeResult result)
        {
            try { return PlacementPlanner.AllBottles(); }
            catch { return new List<WaterContainerItem>(); }
        }

        private static List<Container> SafeContainers(Recipe recipe, List<Item> pool)
        {
            try
            {
                ItemInfo info = Item.GetItem(recipe.result.id);
                return PlacementPlanner.ContainerCandidates(info, recipe.result.resultCondition);
            }
            catch { return new List<Container>(); }
        }

        private static int BottleId(WaterContainerItem wc)
        {
            try
            {
                Item it = wc != null ? wc.GetComponent<Item>() : null;
                return it != null ? it.GetInstanceID() : 0;
            }
            catch { return 0; }
        }

        private static void CycleLiquid(List<WaterContainerItem> bottles, int dir, string liquidId)
        {
            int count = bottles.Count + 2;                  // 自动 / 各瓶 / 临时瓶
            int at = 0;
            if (CraftSession.ForceTempBottle) at = bottles.Count + 1;
            else if (CraftSession.ManualBottleInstance != 0)
            {
                for (int i = 0; i < bottles.Count; i++)
                    if (BottleId(bottles[i]) == CraftSession.ManualBottleInstance) { at = i + 1; break; }
            }

            int next = ((at + dir) % count + count) % count;
            // 候选表里被置灰的那些，◀/▶ 也跳过：它们点了也不会被倒液（`Pour` 的守卫），
            // 停在那儿只会让「将存放于」显示一个假的落点。最多绕一圈，全是灰的就停在「自动」。
            for (int guard = 0; guard < bottles.Count && next > 0 && next <= bottles.Count; guard++)
            {
                WaterContainerItem b = bottles[next - 1];
                if (b != null && PlacementPlanner.BlockedReason(b, liquidId).Length == 0) break;
                next = ((next + dir) % count + count) % count;
            }
            if (next <= 0)
            {
                CraftSession.ManualBottleInstance = 0;
                CraftSession.ForceTempBottle = false;
            }
            else if (next > bottles.Count)
            {
                CraftSession.ForceTempBottle = true;
                CraftSession.ManualBottleInstance = 0;
            }
            else
            {
                CraftSession.ForceTempBottle = false;
                CraftSession.ManualBottleInstance = BottleId(bottles[next - 1]);
            }
            RefreshPanel();
        }

        private static void CycleSolid(List<Container> containers, int dir)
        {
            int count = containers.Count + 1;               // 自动 / 各容器
            int at = 0;
            if (CraftSession.ManualSolidSet && CraftSession.ManualSolidContainer != null)
            {
                for (int i = 0; i < containers.Count; i++)
                    if (containers[i] == CraftSession.ManualSolidContainer) { at = i + 1; break; }
            }

            int next = ((at + dir) % count + count) % count;
            if (next <= 0)
            {
                CraftSession.ManualSolidContainer = null;
                CraftSession.ManualSolidSet = false;
            }
            else
            {
                CraftSession.ManualSolidContainer = containers[next - 1];
                CraftSession.ManualSolidSet = true;
            }
            RefreshPanel();
        }

        /// <summary>改完选择后刷新原版合成界面（材料预览图标与消耗文本都在那里）。</summary>
        private static void RefreshPanel()
        {
            _compactDirty = true;      // 覆盖层立刻重算（不然要等下一次 0.25 秒节流）
            try
            {
                PlayerCamera cam = PlayerCamera.main;
                if (cam != null && cam.craftingPanel != null && cam.craftingPanel.activeSelf)
                    cam.RefreshCurrentlySelectedRecipe();
            }
            catch (Exception e) { LogOnce(e); }
        }

        // ── 合成界面上的紧凑覆盖层（C 键界面打开时显示） ────────────────────

        /// <summary>
        /// 画在 C 键合成界面旁边的小面板：逐槽消耗 + 产物去向 + 换材料/换容器按钮。
        /// 与主窗口共用同一套逻辑（都在本类里），所以两边永远一致。
        /// </summary>
        internal static void DrawCompact(Rect area)
        {
            Recipe recipe = CurrentRecipe();
            if (recipe == null || recipe.items == null) return;

            EnsureStyles();
            GUILayout.BeginArea(area, _box);

            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("ocs.craft.needs") + " — " + SafeRecipeName(recipe), _head);
            GUILayout.FlexibleSpace();
            // 覆盖层可以拖动（在本类的 UI\CraftOverlay 里处理鼠标），提示写在标题行最右侧
            GUILayout.Label(Loc.T("ocs.overlay.hint"), _small);
            GUILayout.EndHorizontal();

            List<Item> pool = CraftingScanner.Gather(true);

            // 节流重算（见字段区注释）：配方没变、也没点按钮时，0.25 秒内直接用上次结果。
            int ridx = recipe.index;
            if (_compactDirty || ridx != _compactRecipeIndex || Time.unscaledTime - _compactTime > 0.25f)
            {
                _compactDirty = false;
                _compactTime = Time.unscaledTime;
                _compactRecipeIndex = ridx;
                try { _compactSelection = CraftSession.Select(recipe, pool); }
                catch (Exception e) { LogOnce(e); _compactSelection = null; }
                try { _compactTarget = CraftText.ResultDestination(recipe); }
                catch { _compactTarget = "?"; }
            }

            List<Item> selection = _compactSelection;

            for (int i = 0; i < recipe.items.Count; i++)
            {
                RecipeItem slot = recipe.items[i];
                if (slot == null) continue;
                Item chosen = selection != null && i < selection.Count ? selection[i] : null;

                GUILayout.BeginHorizontal();
                GUILayout.Label((i + 1) + ". " + CraftText.SlotLabel(slot) +
                                (QualityLabelText(slot).Length > 0 ? " · " + QualityLabelText(slot) : string.Empty),
                                _label, GUILayout.Width(190f));

                // 「材料名 ▾」点一下就地摊开候选表（再点收起）——不再用 ◀/▶ 盲翻；
                // 只有一种材料可用的槽位不给入口（理由见 CraftSelector.HasMaterialChoice）。
                string pick = chosen != null ? CraftText.MaterialLabel(slot, chosen) : "—";
                if (CraftSelector.HasMaterialChoice(slot, pool))
                {
                    if (GUILayout.Button(pick + "  ▾", _pickSlot == i ? _tabOn : _button, GUILayout.Width(200f)))
                        TogglePick(i);
                }
                else
                {
                    SuppressPick(recipe.index, i);
                    GUILayout.Label(pick, _label, GUILayout.Width(200f));
                }

                GUILayout.Label(chosen != null ? SafeDescribe(slot, chosen) : string.Empty, _small, GUILayout.Width(180f));
                GUILayout.EndHorizontal();

                if (_pickSlot == i) DrawPickList(recipe, i, pool, selection, CompactListWidth);
            }

            if (recipe.result != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(Loc.T("ocs.craft.store"), _head, GUILayout.Width(80f));
                GUILayout.Label(_compactTarget ?? "?", _label, GUILayout.Width(280f));

                if (GUILayout.Button(Loc.T("ocs.craft.change") + "  ▾", _destOpen ? _tabOn : _button, GUILayout.Width(140f)))
                    ToggleDest();

                if (recipe.result.isLiquid &&
                    GUILayout.Button(Loc.T("ocs.store.tempbottle"), _button, GUILayout.Width(140f)))
                {
                    CraftSession.ForceTempBottle = true;
                    CraftSession.ManualBottleInstance = 0;
                    ClosePickers();
                    RefreshPanel();
                }
                GUILayout.EndHorizontal();

                if (_destOpen) DrawDestList(recipe, pool, CompactListWidth);
            }

            GUILayout.EndArea();
        }

        /// <summary>覆盖层当前需要的总高度：画之前先量一遍，免得摊开候选表时末尾被裁掉。</summary>
        internal static float CompactHeight()
        {
            Recipe recipe = CurrentRecipe();
            if (recipe == null || recipe.items == null) return 120f;

            int slots = 0;
            for (int i = 0; i < recipe.items.Count; i++)
                if (recipe.items[i] != null) slots++;

            float h = 14f + RowH + slots * RowH;
            if (_pickSlot >= 0) h += PickListHeight(PickCount(recipe, _pickSlot));
            if (recipe.result != null)
            {
                h += RowH;
                if (_destOpen) h += PickListHeight(DestCount(recipe));
            }
            h += 10f;

            float max = Screen.height / UiScale - 40f;
            return Mathf.Clamp(h, 110f, Mathf.Max(110f, max));
        }

        /// <summary>候选表高度：count 个条目 + 「自动」一行，超过 PickRowsMax 就靠滚动条。</summary>
        private static float PickListHeight(int count)
        {
            int rows = Mathf.Clamp(count + 1, 1, PickRowsMax);
            return rows * PickRowH + 36f;
        }

        private static int PickCount(Recipe recipe, int slotIndex)
        {
            try
            {
                if (recipe == null || recipe.items == null || slotIndex < 0 || slotIndex >= recipe.items.Count) return 0;
                List<Item> cands = CraftSelector.Candidates(recipe.items[slotIndex], CraftingScanner.Gather(true));
                return cands != null ? cands.Count : 0;
            }
            catch { return 0; }
        }

        private static int DestCount(Recipe recipe)
        {
            try
            {
                if (recipe == null || recipe.result == null) return 0;
                if (recipe.result.isLiquid) return SafeBottles(recipe.result).Count + 2;   // 自动 / 临时瓶 / 各瓶
                return SafeContainers(recipe, CraftingScanner.Gather(true)).Count + 1;     // 自动 / 各容器
            }
            catch { return 0; }
        }

        /// <summary>
        /// 槽位已经没得选了：把候选表收起来，并把以前留下的手动指定清掉。
        ///
        /// 为什么还要清手动指定：玩家可能在这条配方还有两种材料可用时手选过一件，之后材料被用掉，
        /// 候选里只剩一种 id——此时 ▾ 入口消失，「自动」按钮也跟着消失，手动指定就再也没法取消了。
        /// ClearPick 会写一条证据日志，所以这里只在确实还有记忆时调用一次，不能每帧调。
        /// </summary>
        private static void SuppressPick(int recipeIndex, int slotIndex)
        {
            if (_pickSlot == slotIndex) _pickSlot = -1;
            if (CraftSession.PickMemory.TryGetValue(recipeIndex, out Dictionary<int, int> mem) && mem.ContainsKey(slotIndex))
                CraftSession.ClearPick(recipeIndex, slotIndex);
        }

        /// <summary>展开/收起某个槽位的候选材料表（与存放目标表互斥，屏幕上一次只摊一张）。</summary>
        private static void TogglePick(int slotIndex)
        {
            _pickSlot = _pickSlot == slotIndex ? -1 : slotIndex;
            if (_pickSlot >= 0) { _destOpen = false; _pickScroll = Vector2.zero; }
        }

        private static void ToggleDest()
        {
            _destOpen = !_destOpen;
            if (_destOpen) { _pickSlot = -1; _pickScroll = Vector2.zero; }
        }

        private static void ClosePickers()
        {
            _pickSlot = -1;
            _destOpen = false;
        }

        /// <summary>点开后的候选材料表：第一行「自动」，其后每行一件可用物品（点哪件就用哪件）。</summary>
        private static void DrawPickList(Recipe recipe, int slotIndex, List<Item> pool, List<Item> selection, float width)
        {
            if (recipe == null || recipe.items == null || slotIndex < 0 || slotIndex >= recipe.items.Count) return;
            RecipeItem slot = recipe.items[slotIndex];

            List<Item> cands;
            try { cands = CraftSelector.Candidates(slot, pool); }
            catch (Exception e) { LogOnce(e); cands = new List<Item>(); }

            Item chosen = selection != null && slotIndex < selection.Count ? selection[slotIndex] : null;

            GUILayout.BeginVertical(_box);
            GUILayout.Label(Loc.T("ocs.craft.pick.title") + " · " + Loc.T("ocs.win.col.item") + " (" + cands.Count + ")", _small);

            _pickScroll = GUILayout.BeginScrollView(_pickScroll, GUILayout.Width(width),
                                                    GUILayout.Height(PickListHeight(cands.Count) - 36f));

            if (GUILayout.Button(Loc.T("ocs.craft.auto"), chosen == null ? _tabOn : _button, GUILayout.Width(width - 26f)))
            {
                CraftSession.ClearPick(recipe.index, slotIndex);
                ClosePickers();
                RefreshPanel();
            }

            for (int k = 0; k < cands.Count; k++)
            {
                Item c = cands[k];
                // 液体槽位显示「液体名（容器名）」：候选表里最该看清的是倒掉哪种液体
                string label = CraftText.MaterialLabel(slot, c);
                string desc = SafeDescribe(slot, c);
                if (!string.IsNullOrEmpty(desc)) label += "    " + desc;
                if (c == chosen) label = "● " + label;

                if (GUILayout.Button(label, c == chosen ? _tabOn : _tabOff, GUILayout.Width(width - 26f)))
                {
                    CraftSession.RememberPick(recipe.index, slotIndex, c.GetInstanceID(), slot);
                    ClosePickers();
                    RefreshPanel();
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        /// <summary>点开后的存放目标表：自动 / 临时瓶 / 每个可用容器（液体），或 自动 / 每个容器（固体）。</summary>
        private static void DrawDestList(Recipe recipe, List<Item> pool, float width)
        {
            if (recipe == null || recipe.result == null) return;

            int count = DestCount(recipe);
            int listed = Mathf.Max(0, count - (recipe.result.isLiquid ? 2 : 1));

            GUILayout.BeginVertical(_box);
            GUILayout.Label(Loc.T("ocs.craft.store.title") + " (" + listed + ")", _small);

            _pickScroll = GUILayout.BeginScrollView(_pickScroll, GUILayout.Width(width),
                                                    GUILayout.Height(PickListHeight(count) - 36f));

            bool auto = recipe.result.isLiquid
                ? (!CraftSession.ForceTempBottle && CraftSession.ManualBottleInstance == 0)
                : (!CraftSession.ManualSolidSet || CraftSession.ManualSolidContainer == null);
            if (GUILayout.Button(Loc.T("ocs.craft.auto"), auto ? _tabOn : _button, GUILayout.Width(width - 26f)))
            {
                CraftSession.ResetDestination();
                ClosePickers();
                RefreshPanel();
            }

            if (recipe.result.isLiquid)
            {
                List<WaterContainerItem> bottles = SafeBottles(recipe.result);

                if (GUILayout.Button(Loc.T("ocs.store.tempbottle.warn"), CraftSession.ForceTempBottle ? _tabOn : _button,
                                     GUILayout.Width(width - 26f)))
                {
                    CraftSession.ForceTempBottle = true;
                    CraftSession.ManualBottleInstance = 0;
                    ClosePickers();
                    RefreshPanel();
                }

                for (int k = 0; k < bottles.Count; k++)
                {
                    WaterContainerItem b = bottles[k];
                    int id = BottleId(b);
                    bool cur = !CraftSession.ForceTempBottle && id != 0 && CraftSession.ManualBottleInstance == id;
                    string label = (cur ? "● " : string.Empty) + PlacementPlanner.BottleDetail(b, recipe.result.id);

                    // 「自动」不选它的瓶子照样列出来（玩家要求显示所有瓶子），但整行置灰点不动：
                    // 能点就等于手选绕过 CanMixLiquids / CanFillNewBottles / 容量这些设置。
                    // 已经选中的那一行例外——否则玩家改了设置后连「取消选中」都点不到。
                    string why = PlacementPlanner.BlockedReason(b, recipe.result.id);
                    bool prevEnabled = GUI.enabled;
                    GUI.enabled = prevEnabled && (why.Length == 0 || cur);
                    if (GUILayout.Button(label, cur ? _tabOn : _tabOff, GUILayout.Width(width - 26f)))
                    {
                        CraftSession.ForceTempBottle = false;
                        CraftSession.ManualBottleInstance = id;
                        ClosePickers();
                        RefreshPanel();
                    }
                    GUI.enabled = prevEnabled;
                }
            }
            else
            {
                List<Container> containers = SafeContainers(recipe, pool);
                for (int k = 0; k < containers.Count; k++)
                {
                    Container c = containers[k];
                    bool cur = CraftSession.ManualSolidSet && CraftSession.ManualSolidContainer == c;
                    string label = (cur ? "● " : string.Empty) + PlacementPlanner.Label(c);

                    if (GUILayout.Button(label, cur ? _tabOn : _tabOff, GUILayout.Width(width - 26f)))
                    {
                        CraftSession.ManualSolidContainer = c;
                        CraftSession.ManualSolidSet = true;
                        ClosePickers();
                        RefreshPanel();
                    }
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        /// <summary>独立窗口当前占用的屏幕矩形（覆盖层用它避免和窗口叠在一起）。</summary>
        internal static Rect WindowRect => _rectReady ? _rect : new Rect(0f, 0f, 0f, 0f);

        // ── 皮肤 / 字体 ──────────────────────────────────────────────────────

        private static void EnsureStyles()
        {
            if (_label != null) return;

            Font f = ChineseFont();
            _title = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _head = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            _label = new GUIStyle(GUI.skin.label) { wordWrap = false };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = false };
            _button = new GUIStyle(GUI.skin.button);
            _tabOn = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
            _tabOff = new GUIStyle(GUI.skin.button);
            _box = new GUIStyle(GUI.skin.box);

            if (f == null) return;
            _title.font = f;
            _head.font = f;
            _label.font = f;
            _small.font = f;
            _button.font = f;
            _tabOn.font = f;
            _tabOff.font = f;
            _box.font = f;
        }

        /// <summary>中文字体：IMGUI 默认字体没有 CJK 字形，找不到就退回默认字体（显示方框但仍可用）。</summary>
        private static Font ChineseFont()
        {
            if (_fontTried) return _font;
            _fontTried = true;
            try
            {
                _font = Font.CreateDynamicFontFromOSFont(new[]
                {
                    "Microsoft YaHei", "微软雅黑", "SimHei", "SimSun",
                    "Noto Sans CJK SC", "Source Han Sans SC", "Arial Unicode MS"
                }, 15);
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning("[UI] 找不到中文字体，界面文字可能显示为方框：" + e.Message);
                _font = null;
            }
            return _font;
        }
    }
}
