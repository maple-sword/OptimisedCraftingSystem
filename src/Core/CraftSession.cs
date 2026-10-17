using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OptimisedCraftingSystem
{
    /// <summary>
    /// 一次合成（`Recipe.TryMake` 的一次调用）期间的状态。
    ///
    /// 为什么要它：原模组把"合成"拆成两个互不知情的补丁 —— 一个挑材料、一个事后存放，
    /// 于是没法回答玩家最关心的两个问题："这次会消耗什么、消耗多少"和"产物会去哪"。
    /// 这里把一次合成包成一个会话：
    ///   · `Begin` 在 `Recipe.TryMake` 前缀里开，算好固体产物的存放计划与液体目标；
    ///   · 材料选择由 `Select` 统一负责（含玩家手动指定），预览与实际用的是同一份算法；
    ///   · 存放执行时从 `TakeSolidPlan` 逐件取预测好的计划，做到**预测 = 实际**；
    ///   · `End` 在后缀里收尾，把"已存放：…"播报给玩家（`notify_where_stored`）。
    /// </summary>
    internal static class CraftSession
    {
        /// <summary>本次合成里产物实际去了哪里（用于合成结束的播报）。</summary>
        internal static readonly List<string> StoredAt = new List<string>();

        /// <summary>
        /// "自由选择材料"的记忆：配方下标 → (槽位下标 → 物品 InstanceID)。
        /// 只存在内存里（不写盘），玩家重新选一次即可；物品消失时会自动失效。
        /// </summary>
        internal static readonly Dictionary<int, Dictionary<int, int>> PickMemory = new Dictionary<int, Dictionary<int, int>>();

        /// <summary>玩家手动指定的液体目标容器（物品 InstanceID；0 = 自动挑）。</summary>
        internal static int ManualBottleInstance;

        /// <summary>玩家手动选择了"临时瓶"作为液体去向。</summary>
        internal static bool ForceTempBottle;

        /// <summary>玩家手动指定的固体产物存放容器（`ManualSolidSet` 为真时生效）。</summary>
        internal static Container ManualSolidContainer;
        internal static bool ManualSolidSet;

        /// <summary>把结果的目标设回"自动"（液体回到自动挑瓶/临时瓶，固体回到自动挑容器）。</summary>
        internal static void ResetDestination()
        {
            ManualBottleInstance = 0;
            ForceTempBottle = false;
            ManualSolidContainer = null;
            ManualSolidSet = false;
        }

        private static Recipe _recipe;
        private static List<Item> _selection;
        /// <summary>材料被消耗**之前**的耐久快照（与 _selection 同下标），日志用它显示「烧掉的是哪一件」。</summary>
        private static float[] _conditionBefore;
        private static List<PlacementPlanner.Plan> _solidPlans;
        private static int _solidIndex;
        private static WaterContainerItem _liquidTarget;
        private static bool _liquidForceTemp;
        private static bool _liquidPending;
        private static string _liquidId;
        private static float _liquidMl;

        internal static bool Active => _recipe != null;
        internal static Recipe CurrentRecipe => _recipe;
        internal static List<Item> Selection => _selection;
        internal static string LiquidId => _liquidId;
        internal static float LiquidMl => _liquidMl;
        internal static WaterContainerItem LiquidTarget => _liquidTarget;
        internal static bool LiquidForceTemp => _liquidForceTemp;

        /// <summary>合成中的液体是否已经有明确去向（有它就不该再往背包里塞临时瓶）。</summary>
        internal static bool HasLiquidTarget => _liquidTarget != null || _liquidForceTemp;

        /// <summary>
        /// 本次合成是否还在等待"原版把产出的液体倒进某个瓶子"这一步。
        /// 只在这期间才拦截 AddLiquid，避免会话期间瓶子的自动补水（AutoFill）被误改道。
        /// </summary>
        internal static bool LiquidPending => _liquidPending;

        /// <summary>液体已经倒进目标容器，撤销改道窗口。</summary>
        internal static void LiquidDone()
        {
            _liquidPending = false;
        }

        // ————————————————————————————————————————————————————————————
        // 材料选择（预览与实际共用）
        // ————————————————————————————————————————————————————————————

        /// <summary>为一个配方选出每一槽要用的物品；未能满足的槽位是 `null`。</summary>
        internal static List<Item> Select(Recipe recipe, List<Item> pool)
        {
            List<Item> selection = CraftSelector.SelectForRecipe(recipe, pool);
            if (selection != null)
            {
                try { ApplyOverrides(recipe, pool, selection); }
                catch (System.Exception e) { Plugin.Log?.LogWarning("[Pick] " + e.Message); }
            }
            return selection;
        }

        /// <summary>
        /// 记住玩家在面板里手动指定的材料。
        /// `slot` 只用于证据日志的措辞（液体槽位写成「液体名（容器名）」），不参与选择逻辑。
        /// </summary>
        internal static void RememberPick(int recipeIndex, int slotIndex, int instanceId, RecipeItem slot = null)
        {
            if (!PickMemory.TryGetValue(recipeIndex, out Dictionary<int, int> map))
            {
                map = new Dictionary<int, int>();
                PickMemory[recipeIndex] = map;
            }
            map[slotIndex] = instanceId;

            // 证据日志：手动选材是玩家侧交互，日志留痕后"这次合成用的是我指定的那件"才可核对
            try
            {
                Item picked = FindByInstance(CraftingScanner.Gather(includeFloor: true), instanceId);
                Plugin.Evidence(
                    "pick:" + recipeIndex + ":" + slotIndex + ":" + instanceId,
                    "手动选材：配方#" + recipeIndex + " 槽位" + slotIndex + " ← " +
                    (picked != null ? CraftText.MaterialLabel(slot, picked) : ("物品#" + instanceId)));
            }
            catch { }
        }

        /// <summary>取消玩家对某个槽位的手动指定。</summary>
        internal static void ClearPick(int recipeIndex, int slotIndex)
        {
            if (PickMemory.TryGetValue(recipeIndex, out Dictionary<int, int> map))
            {
                map.Remove(slotIndex);
                if (map.Count == 0) PickMemory.Remove(recipeIndex);
            }
            Plugin.Evidence("pick:" + recipeIndex + ":" + slotIndex + ":clear",
                "手动选材：配方#" + recipeIndex + " 槽位" + slotIndex + " 已改回自动。");
        }

        internal static Item ResolvePick(int recipeIndex, int slotIndex, List<Item> pool)
        {
            if (PickMemory.TryGetValue(recipeIndex, out Dictionary<int, int> map)
                && map.TryGetValue(slotIndex, out int instanceId))
            {
                return FindByInstance(pool, instanceId);
            }
            return null;
        }

        internal static Item FindByInstance(List<Item> pool, int instanceId)
        {
            if (pool == null) return null;
            foreach (Item it in pool)
            {
                if (it != null && it.GetInstanceID() == instanceId) return it;
            }
            return null;
        }

        private static void ApplyOverrides(Recipe recipe, List<Item> pool, List<Item> selection)
        {
            if (recipe == null || recipe.items == null) return;
            if (!PickMemory.TryGetValue(recipe.index, out Dictionary<int, int> map) || map.Count == 0) return;

            foreach (KeyValuePair<int, int> kv in map)
            {
                int slotIndex = kv.Key;
                if (slotIndex < 0 || slotIndex >= selection.Count || slotIndex >= recipe.items.Count) continue;

                Item chosen = FindByInstance(pool, kv.Value);
                if (chosen == null) continue;
                if (!CraftSelector.Usable(recipe.items[slotIndex], chosen)) continue;
                selection[slotIndex] = chosen;
            }

            // 同一件"会被消耗掉的"物品不能同时填两个槽位（原版用 exclusions 保证这一点）
            for (int i = 0; i < selection.Count && i < recipe.items.Count; i++)
            {
                Item shared = selection[i];
                if (shared == null) continue;
                RecipeItem ri = recipe.items[i];
                if (ri.isLiquid || !ri.destroyItem) continue;

                for (int j = i + 1; j < selection.Count && j < recipe.items.Count; j++)
                {
                    if (selection[j] != shared) continue;
                    RecipeItem rj = recipe.items[j];
                    if (rj.isLiquid || !rj.destroyItem)
                    {
                        selection[j] = null;
                        continue;
                    }
                    Item alternative = CraftSelector.Best(rj, pool);
                    selection[j] = alternative != shared ? alternative : null;
                }
            }
        }

        // ————————————————————————————————————————————————————————————
        // 会话生命周期
        // ————————————————————————————————————————————————————————————

        internal static bool Begin(Recipe recipe)
        {
            End(quiet: true);
            ContainerRescue.Begin();   // 上一次合成搬出来的东西不该跨会话存在（End 已落地，这里是兜底）
            if (recipe == null) return false;

            _recipe = recipe;
            try
            {
                if (recipe.result != null && !recipe.result.isLiquid)
                {
                    ItemInfo info = Item.GetItem(recipe.result.id);
                    if (info != null)
                    {
                        _solidPlans = PlacementPlanner.PlanSequence(
                            info,
                            recipe.result.resultCondition,
                            Mathf.Max(1, recipe.result.amount));
                    }
                }
                else if (recipe.result != null)
                {
                    _liquidId = recipe.result.id;
                    _liquidMl = recipe.result.resultCondition;
                    _liquidForceTemp = ForceTempBottle;
                    _liquidTarget = ResolveManualBottle();
                    if (_liquidTarget == null && !_liquidForceTemp)
                    {
                        PlacementPlanner.Plan p = PlacementPlanner.PredictLiquid(_liquidId, _liquidMl);
                        if (p.Kind == PlacementPlanner.Destination.Bottle) _liquidTarget = p.Bottle;
                    }
                    // 只在这一步之后才允许改道，防止会话期间瓶子的 AutoFill 被误拦。
                    _liquidPending = true;
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning("[Session] 计划失败：" + e.Message);
            }
            return true;
        }

        internal static void SetSelection(List<Item> selection)
        {
            _selection = selection;
            // 记下消耗前的耐久：日志在合成结束（End）时才写，那时材料已经被扣过耐久，
            // 直接读 condition 会显示成「用完之后」的数字，玩家没法核对到底烧掉了哪一件。
            if (selection == null)
            {
                _conditionBefore = null;
                return;
            }
            _conditionBefore = new float[selection.Count];
            for (int i = 0; i < selection.Count; i++)
                _conditionBefore[i] = selection[i] != null ? selection[i].condition : 0f;
        }

        /// <summary>取下一件固体产物的存放计划（按产物顺序）。</summary>
        internal static PlacementPlanner.Plan TakeSolidPlan()
        {
            if (_solidPlans == null || _solidIndex >= _solidPlans.Count) return default;
            return _solidPlans[_solidIndex++];
        }

        internal static int RemainingSolidPlans =>
            _solidPlans == null ? 0 : Mathf.Max(0, _solidPlans.Count - _solidIndex);

        /// <summary>液体产物去向的预览（尊重玩家的手动指定）。</summary>
        internal static PlacementPlanner.Plan PreviewLiquid(string liquidId, float ml)
        {
            try
            {
                if (ForceTempBottle)
                    return PlacementPlanner.Make(PlacementPlanner.Destination.TempBottle, Loc.T("ocs.store.tempbottle.warn"));

                WaterContainerItem manual = ResolveManualBottle();
                if (manual != null)
                {
                    // 手动指定的瓶子后来被挡了（最典型：玩家顺手给它打了星标 → 「收藏的容器不接液体」）：
                    // 这时预测必须跟执行一致 —— `Pour` 会跳过这个目标、改走自动分配，所以这里也回落到自动预测，
                    // 免得界面上写着「将存放于 水壶」而实际倒进了别处。
                    if (PlacementPlanner.BlockedReason(manual, liquidId).Length > 0)
                        return PlacementPlanner.PredictLiquid(liquidId, ml);

                    return PlacementPlanner.Make(
                        PlacementPlanner.Destination.Bottle,
                        Loc.T("ocs.store.bottle.contents", PlacementPlanner.BottleName(manual),
                              PlacementPlanner.ContentsText(manual), Mathf.Max(0f, manual.SpaceLeft)),
                        null,
                        manual);
                }
                return PlacementPlanner.PredictLiquid(liquidId, ml);
            }
            catch
            {
                return default;
            }
        }

        internal static WaterContainerItem ResolveManualBottle()
        {
            if (ManualBottleInstance == 0) return null;
            try
            {
                // 含手边地面：候选表里会列出地面上的瓶子（含容器里翻出来的），选了就得认
                foreach (Item it in CraftingScanner.Gather(includeFloor: true))
                {
                    if (it == null || it.GetInstanceID() != ManualBottleInstance) continue;
                    WaterContainerItem wc = it.GetComponent<WaterContainerItem>();
                    if (wc != null && wc.Capacity > 0f) return wc;
                }
            }
            catch { }
            ManualBottleInstance = 0;
            return null;
        }

        internal static void NoteStored(string where)
        {
            if (string.IsNullOrEmpty(where) || !ModSettings.NotifyWhereStored) return;
            if (StoredAt.Contains(where)) return;
            StoredAt.Add(where);
        }

        internal static void End(bool quiet = false)
        {
            // 材料里被整件消耗掉的容器：还冻在手里的内容物一律落地。
            // 走到这里就说明"新容器没接住它们"——产物不是容器、新容器装满了、或者合成中途失败了。
            // 这一句同时也是**安全网**：没有它，任何异常路径都会留下几件永远冻着的物品。
            try { ContainerRescue.Flush(); }
            catch (System.Exception e) { Plugin.Log?.LogWarning("[Session] 内容物落地失败：" + e.Message); }

            try { LogCraft(); }
            catch (System.Exception e) { Plugin.Log?.LogWarning("[Session] 记录失败：" + e.Message); }

            try
            {
                if (!quiet && StoredAt.Count > 0 && ModSettings.NotifyWhereStored)
                {
                    PlayerCamera cam = PlayerCamera.main;
                    if (cam != null)
                    {
                        string sep = Loc.IsChinese ? "、" : ", ";
                        cam.DoAlert(Loc.T("ocs.stored.prefix") + string.Join(sep, StoredAt.ToArray()));
                    }
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning("[Session] 播报失败：" + e.Message);
            }

            _recipe = null;
            _selection = null;
            _conditionBefore = null;
            _solidPlans = null;
            _solidIndex = 0;
            _liquidTarget = null;
            _liquidForceTemp = false;
            _liquidPending = false;
            _liquidId = null;
            _liquidMl = 0f;
            StoredAt.Clear();
        }

        // ————————————————————————————————————————————————————————————
        // 日志：每次合成记一行（连续相同的只记一条），既是排错依据，
        // 也是"改进前后对比"里可以逐条引用的游戏内证据。
        // ————————————————————————————————————————————————————————————

        private static string _lastLog;

        private static void LogCraft()
        {
            if (_recipe == null) return;

            StringBuilder sb = new StringBuilder();
            sb.Append("[合成] ").Append(_recipe.simpleName).Append(" ← ");

            if (_selection == null || _selection.Count == 0)
            {
                sb.Append("(无材料)");
            }
            else
            {
                for (int i = 0; i < _selection.Count; i++)
                {
                    if (i > 0) sb.Append(" + ");
                    Item it = _selection[i];
                    if (it == null) { sb.Append("(缺料)"); continue; }

                    sb.Append(CraftingScanner.NameOf(it));
                    // 带上这件材料**消耗前**的耐久（纯文本百分比），方便核对"到底烧掉了哪一件"
                    float condBefore = _conditionBefore != null && i < _conditionBefore.Length
                        ? _conditionBefore[i]
                        : it.condition;
                    sb.Append('(').Append(Mathf.RoundToInt(condBefore * 100f)).Append('%');
                    RecipeItem slot = _recipe.items != null && i < _recipe.items.Count ? _recipe.items[i] : null;
                    if (slot != null)
                    {
                        string desc = QualityModel.Describe(slot, it);
                        if (!string.IsNullOrEmpty(desc) && desc != "-") sb.Append(", ").Append(desc);
                    }
                    sb.Append(')');
                }
            }

            if (StoredAt.Count > 0)
                sb.Append(" → ").Append(string.Join(Loc.IsChinese ? "、" : ", ", StoredAt.ToArray()));

            string line = sb.ToString();
            if (line == _lastLog) return;
            _lastLog = line;
            Plugin.Log?.LogInfo(line);
        }
    }
}
