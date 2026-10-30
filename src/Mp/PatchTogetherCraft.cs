using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace OptimisedCraftingSystem
{
    /// <summary>
    /// 联机（Casualties Together）下的合成接管点。
    ///
    /// 为什么单机那套补丁在联机里**完全不生效**（这是本文件存在的原因，均有反编译证据）：
    ///   · Together 用 800 优先级的 Prefix 把 `Recipe.GetItemsForRecipe` / `GetItemsForRecipeThorough`
    ///     整体替换成 `CraftCommit.MatchList`（`CraftCommit_GetItems.cs`、`CraftCommit_GetItemsThorough.cs`），
    ///     它先跑并 `return false`，我们挂在 `Priority.Low`（200）的前缀根本没机会执行；
    ///   · Together 同样用 Prefix 接管 `PlayerCamera.TryCraft` → `CraftCommit.TryClick`
    ///     （`PlayerCamera_TryCraft_MultiplayerPatch.cs`），而 `Recipe.TryMake` 在联机里**不再被调用**，
    ///     所以我们"在 TryMake 前缀里开会话、后缀里收尾"的整套机制是空转的；
    ///   · 真正的消耗与产物生成搬到权威端（主机/专用服务器）的 `CraftCommit.Commit` 里，
    ///     顺序也反了：先 `RecipeResult_SpawnResult` + `AutoPickUpItem`，之后才 `RecipeItem.UseItem`。
    ///
    /// 因此这里换一组挂点，让"智能选材 / 消耗提示 / 存放预测 / 内容物救援"在联机里继续成立：
    ///   1. `CraftCommit.MatchList` Prefix —— 界面（C 键面板、材料总览、覆盖层）显示的就是我们要提交的那套材料；
    ///   2. `CraftCommit.TryClick` / `Request` Prefix+Postfix —— 打开"联机合成会话"（只有权威端才有意义），
    ///      并把选材结果作为本次提交的引导；
    ///   3. `RecipeItem.GetMatchingItem` Prefix —— Together 在 `Evaluate` / `CheckIfTheseItemsAreEnoughForTheRecipe`
    ///      里就是用它逐个槽位挑材料的，这里是**唯一**能把"玩家/算法指定的那件"塞进提交 ids 的入口。
    ///      只在上面第 2 步划定的极窄窗口内生效，其它任何调用（QoL 的够得着扫描、prosthetics、
    ///      CraftingFramework 的自定义 id 匹配）看到的都是原版行为。
    ///
    /// 权威端与客户端的边界（不这么做就会 Desync）：
    ///   · 客户端只能决定"提交哪几件"（ids），真正的扣除与产物生成由主机执行；
    ///   · 库存改写、容器内容物搬运、液体改道只在权威端执行 —— 客户端做了只会造成两端状态不一致。
    /// </summary>
    internal static class PatchTogetherCraft
    {
        // ——— 选材结果的帧缓存（C 键面板一帧内会为每个可见配方各问一次）———
        private static int _cacheFrame = -1;
        private static readonly Dictionary<int, List<Item>> _cache = new Dictionary<int, List<Item>>();

        // ——— 本次提交的引导状态 ———
        private static Recipe _steerRecipe;
        private static List<Item> _steerSelection;
        private static int _steerDepth;

        private static bool _loggedError;

        /// <summary>是否处在"正在为一次联机提交挑材料"的窗口里（`GetMatchingItem` 只认这个窗口）。</summary>
        private static bool Steering => _steerDepth > 0 && _steerRecipe != null && _steerSelection != null;

        // ————————————————————————————————————————————————————————————
        // 安装
        // ————————————————————————————————————————————————————————————

        /// <summary>
        /// 用反射装三组补丁（Together 不在场时一次都不装，避免 Harmony 报"找不到目标"）。
        /// 每个目标单独 try/catch：Together 某个版本改了成员名时，其余补丁仍然可用。
        /// </summary>
        internal static void Install(Harmony harmony)
        {
            if (harmony == null) return;

            TryInstall(harmony, "CraftCommit.MatchList",
                AccessTools.Method(MpBridge.CraftCommitType, "MatchList", new[] { typeof(Recipe), typeof(bool) }),
                nameof(MatchListPrefix), null);

            TryInstall(harmony, "CraftCommit.TryClick",
                AccessTools.Method(MpBridge.CraftCommitType, "TryClick", new[] { typeof(PlayerCamera) }),
                nameof(BeginSteerPrefix), nameof(EndSteerPostfix), nameof(CleanupFinalizer));

            TryInstall(harmony, "CraftCommit.Request",
                AccessTools.Method(MpBridge.CraftCommitType, "Request", new[] { typeof(PlayerCamera), typeof(Recipe) }),
                nameof(BeginSteerWithRecipePrefix), nameof(EndSteerPostfix), nameof(CleanupFinalizer));

            TryInstall(harmony, "RecipeItem.GetMatchingItem",
                AccessTools.Method(typeof(RecipeItem), "GetMatchingItem",
                    new[] { typeof(List<Item>), typeof(List<Item>) }),
                nameof(GetMatchingItemPrefix), null);

            // 远端玩家（非主机）的合成走 `CraftCommit.HandleServer`，本模组刻意不插手段（见 README「已知限制」）。
            // 挂一个无参后缀只为在日志里说清楚"这一次为什么没有会话"，免得玩家以为模组在联机里失效。
            TryInstall(harmony, "CraftCommit.HandleServer",
                AccessTools.Method(MpBridge.CraftCommitType, "HandleServer"),
                null, nameof(RemoteCraftNotice));
        }

        private static void TryInstall(Harmony harmony, string label, System.Reflection.MethodBase target,
                                       string prefixName, string postfixName, string finalizerName = null)
        {
            if (target == null)
            {
                MpBridge.PatchReport.Add(label + "=目标缺失");
                Plugin.Log?.LogWarning("[联机] 找不到补丁目标 " + label + "（Together 版本可能变了），该功能在联机里退回原版。");
                return;
            }
            try
            {
                HarmonyMethod pre = prefixName == null ? null : new HarmonyMethod(typeof(PatchTogetherCraft), prefixName)
                {
                    priority = Priority.High
                };
                HarmonyMethod post = postfixName == null ? null : new HarmonyMethod(typeof(PatchTogetherCraft), postfixName)
                {
                    priority = Priority.Low
                };
                HarmonyMethod fin = finalizerName == null ? null : new HarmonyMethod(typeof(PatchTogetherCraft), finalizerName);
                // **必须用具名参数**：`Harmony.Patch` 的第 4 个参数是 transpiler、第 5 个才是 finalizer。
                // 位置传参会把 finalizer 塞进 transpiler 槽，Harmony 报
                // `TargetParameterCountException` + `IL Compile Error`（2026-10-30 实测踩过）。
                harmony.Patch(target, prefix: pre, postfix: post, finalizer: fin);
                MpBridge.PatchReport.Add(label + "=已挂");
            }
            catch (Exception e)
            {
                MpBridge.PatchReport.Add(label + "=失败");
                Plugin.Log?.LogWarning("[联机] 挂 " + label + " 补丁失败：" + e.GetType().Name + ": " + e.Message);
            }
        }

        // ————————————————————————————————————————————————————————————
        // 选材（界面与提交共用同一份算法）
        // ————————————————————————————————————————————————————————————

        /// <summary>
        /// 联机下为某个配方选材料。返回与 `recipe.items` 等长的列表（缺料位置是 null）；
        /// 关闭联机智能选材或算法出错时返回 null，表示"交回 Together 自己的匹配逻辑"。
        /// </summary>
        internal static List<Item> SelectFor(Recipe recipe, bool copy)
        {
            if (recipe == null || recipe.items == null || recipe.index < 0) return null;

            // 重入保护：如果 Together（或别的模组）在我们算选材的过程中又绕回 `MatchList`，
            // 直接交回它原本的匹配逻辑 —— 递归算同一件事既慢又可能栈溢出。
            if (_selectDepth > 0) return null;

            List<Item> cached = null;
            if (_cacheFrame == Time.frameCount && _cache.TryGetValue(recipe.index, out cached))
                return copy && cached != null ? new List<Item>(cached) : cached;

            if (_cacheFrame != Time.frameCount)
            {
                _cache.Clear();
                _cacheFrame = Time.frameCount;
            }

            List<Item> selection = null;
            _selectDepth++;
            try
            {
                // 池子已经由 CraftingScanner 按"联机可提交"过滤（已同步 + 够得着），
                // 与 Together 的 SyncedReachable 池是同一批物品。
                selection = CraftSession.Select(recipe, CraftingScanner.Gather(includeFloor: true));
            }
            catch (Exception e)
            {
                LogOnce("[联机] 智能选材失败，改用 Together 原版匹配：" + e.Message);
                selection = null;
            }
            finally
            {
                _selectDepth--;
            }

            _cache[recipe.index] = selection;
            return copy && selection != null ? new List<Item>(selection) : selection;
        }

        private static int _selectDepth;

        /// <summary>
        /// 看门狗：合成会话必须有人收尾。
        ///
        /// 什么时候会漏：`TryClick`/`Request` 里我们开了会话，但原方法**抛异常**时 Harmony 不会执行后缀，
        /// 会话（以及被 `ContainerRescue` 摘出来冻住的物品）就一直挂着，玩家会以为东西丢了。
        /// 这里每帧检查：会话在、但已经不在合成窗口里、且超过 3 秒 → 强制收尾并让东西落地。
        /// </summary>
        internal static void Tick()
        {
            // ① 引导窗口自己卡住（原方法抛异常、finalizer 也没跑到）：独立超时，必须能自愈
            if (_steerDepth > 0)
            {
                if (_steerSince <= 0f) _steerSince = Time.realtimeSinceStartup;
                else if (Time.realtimeSinceStartup - _steerSince > SteerStuckSeconds)
                {
                    Plugin.Log?.LogWarning("[联机] 选材引导超过 " + SteerStuckSeconds + " 秒没有收尾，已强制复位（下一次合成恢复正常）。");
                    ResetSteering();
                    return;
                }
                _idleSince = 0f;
                return;
            }

            if (!CraftSession.Active)
            {
                _idleSince = 0f;
                return;
            }
            if (_idleSince <= 0f)
            {
                _idleSince = Time.realtimeSinceStartup;
                return;
            }
            if (Time.realtimeSinceStartup - _idleSince > SessionIdleSeconds)
            {
                _idleSince = 0f;
                Plugin.Log?.LogWarning("[联机] 合成会话超过 " + SessionIdleSeconds + " 秒没有收尾（多半是别的前缀抛了异常），已强制结束并让内容物落地。");
                try { CraftSession.End(); } catch { }
            }
        }

        private static float _idleSince;
        private static float _steerSince;

        /// <summary>引导窗口的硬超时（秒）：一次合成点下去是毫秒级，5 秒还没收尾必然是异常路径。</summary>
        private const float SteerStuckSeconds = 5f;

        /// <summary>会话空转超时（秒）：会话存在但已经不在合成窗口里的最长容忍时间。</summary>
        private const float SessionIdleSeconds = 3f;

        private static void LogOnce(string message)
        {
            if (_loggedError) return;
            _loggedError = true;
            Plugin.Log?.LogWarning(message);
        }

        // ————————————————————————————————————————————————————————————
        // 补丁：CraftCommit.MatchList（界面显示）
        // ————————————————————————————————————————————————————————————

        /// <summary>
        /// 把 Together 的"界面匹配结果"换成我们的选材结果。
        /// 语义与 Together 保持一致：非 thorough 版本只要有一个槽位凑不出来就返回 null（界面据此显示"不可合成"）。
        /// </summary>
        private static bool MatchListPrefix(Recipe recipe, bool thorough, ref List<Item> __result)
        {
            if (!MpBridge.Enabled || !MpSmartPickOn) return true;

            List<Item> selection = SelectFor(recipe, copy: true);
            if (selection == null) return true;

            bool complete = true;
            for (int i = 0; i < selection.Count; i++)
            {
                if (selection[i] == null) { complete = false; break; }
            }

            if (!complete && !thorough)
            {
                __result = null;
                return false;
            }

            __result = selection;
            return false;
        }

        /// <summary>联机智能选材是否开启（总开关关掉时一律交回 Together）。</summary>
        private static bool MpSmartPickOn => MpBridge.SmartPickActive;

        // ————————————————————————————————————————————————————————————
        // 补丁：TryClick / Request（一次提交的开始与结束）
        // ————————————————————————————————————————————————————————————

        /// <summary>`CraftCommit.TryClick(PlayerCamera)`：玩家点了合成按钮。</summary>
        private static void BeginSteerPrefix(PlayerCamera cam)
        {
            if (!MpBridge.Enabled || cam == null) return;

            Recipe recipe = null;
            try
            {
                int index = cam.selectedRecipe;
                if (Recipes.recipes != null && index >= 0 && index < Recipes.recipes.Count)
                    recipe = Recipes.recipes[index];
            }
            catch { }
            Begin(recipe);
        }

        /// <summary>`CraftCommit.Request(PlayerCamera, Recipe)`：其它模组（或 Together 内部）走请求接口。</summary>
        private static void BeginSteerWithRecipePrefix(PlayerCamera cam, Recipe recipe)
        {
            if (!MpBridge.Enabled) return;
            Begin(recipe);
        }

        private static void Begin(Recipe recipe)
        {
            try
            {
                _steerDepth++;
                if (_steerDepth > 1) return;   // 嵌套调用只认最外层，别把状态覆盖掉
                _steerSince = Time.realtimeSinceStartup;

                _steerRecipe = null;
                _steerSelection = null;
                if (recipe == null || recipe.items == null) return;

                _steerRecipe = recipe;
                if (MpSmartPickOn)
                {
                    List<Item> selection = SelectFor(recipe, copy: false);
                    if (selection != null) _steerSelection = selection;
                }

                // 只有权威端（主机）才在本地真正扣材料、生成产物；客户端只把 ids 发出去。
                // 会话包住"扣除 + 生成 + 捡起"这一整段，容器内容物救援与存放预测才有立足点。
                if (MpBridge.Authoritative && ModSettings.MpHostStore)
                {
                    CraftSession.Begin(recipe);
                    CraftSession.SetSelection(_steerSelection);
                    // 先预判"这次会不会救援内容物"：决定产物要不要跳过存放预测（见 CraftSession.MpExpectRescue）
                    CraftSession.EvaluateMpRescue();
                    Plugin.EvidenceAlways("联机（主机）合成会话开启：" +
                                          (recipe.simpleName ?? ("配方#" + recipe.index)) +
                                          "；选材=" + Describe(_steerSelection) +
                                          (CraftSession.MpExpectRescue ? "；预期会救援内容物（产物留在手上）" : string.Empty));
                }
                else if (!MpBridge.IsHost)
                {
                    Plugin.EvidenceAlways("联机（客户端）提交合成：" +
                                          (recipe.simpleName ?? ("配方#" + recipe.index)) +
                                          "；选材=" + Describe(_steerSelection));
                }
            }
            catch (Exception e)
            {
                LogOnce("[联机] 打开合成会话失败（本次退回原版行为）：" + e.Message);
            }
        }

        private static void EndSteerPostfix()
        {
            try
            {
                if (_steerDepth <= 0) return;
                _steerDepth--;
                if (_steerDepth > 0) return;

                if (MpBridge.Authoritative && ModSettings.MpHostStore && CraftSession.Active)
                    CraftSession.End();

                _steerRecipe = null;
                _steerSelection = null;
                _steerSince = 0f;
            }
            catch (Exception e)
            {
                LogOnce("[联机] 结束合成会话失败：" + e.Message);
            }
        }

        /// <summary>
        /// 兜底收尾（Harmony Finalizer，**原方法抛异常时也会执行**）。
        ///
        /// 为什么必须有它：`CraftCommit.TryClick` 内部会一路调 `Evaluate → RecipeItem.GetMatchingItem`，
        /// 而原版 `GetMatchingItem` 在遇到未注册 id / `Stats == null` 的物品时会抛 NRE（真机踩过，见
        /// `Patches\PatchCompat.cs`）。Harmony 在原方法抛异常时**不执行后缀**，只执行 finalizer ——
        /// 没有它，`_steerDepth` 会永久停在 1：下次 `Begin` 按"嵌套"提前返回，而 `CraftSession` 永远是 Active，
        /// 之后每一次 `Body.AutoPickUpItem` 都会被当成"本次合成产物"改写，`ContainerRescue` 还会继续把
        /// 容器里的东西摘出来冻住 —— 玩家看到的就是"东西凭空没了"。
        ///
        /// 正常路径下后缀已经把深度归零，这里什么都不做。
        /// </summary>
        private static Exception CleanupFinalizer(Exception __exception)
        {
            if (_steerDepth > 0)
            {
                try
                {
                    Plugin.Log?.LogWarning("[联机] 合成提交过程中抛了异常（" +
                                           (__exception != null ? __exception.GetType().Name : "未知") +
                                           "），已强制收尾：关闭选材引导并结束会话，被摘出的内容物会落回地面。");
                    ResetSteering();
                }
                catch { }
            }
            return __exception;   // 原样抛回，不吞掉别人的异常
        }

        /// <summary>`CraftCommit.HandleServer` 的后缀：远端玩家的合成由主机按 Together 原版执行，
        /// 本模组不插手（不换算会话、不救援内容物、不改落点）。只记一次，避免刷屏。</summary>
        private static void RemoteCraftNotice()
        {
            try
            {
                Plugin.Evidence("mp:remote",
                    "收到远端玩家的合成提交：本模组按设计不介入（不做内容物救援、不做存放预测），" +
                    "扣除与生成完全由 Together 的权威端执行。");
            }
            catch { }
        }

        /// <summary>把引导状态与会话强制复位（异常路径与看门狗共用）。</summary>
        private static void ResetSteering()
        {
            _steerDepth = 0;
            _steerRecipe = null;
            _steerSelection = null;
            _steerSince = 0f;
            _idleSince = 0f;
            try
            {
                if (CraftSession.Active) CraftSession.End();
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning("[联机] 强制结束会话时又失败了一次（已忽略）：" + e.Message);
            }
        }

        // ————————————————————————————————————————————————————————————
        // 补丁：RecipeItem.GetMatchingItem（把选材结果塞进提交）
        // ————————————————————————————————————————————————————————————

        /// <summary>
        /// Together 的 `Evaluate` 逐槽调用 `recipeItem.GetMatchingItem(items, exclusions)` 取**池子里第一件**
        /// 满足条件的物品 —— 它不看成本、不看玩家偏好。这里在"正在为本次提交挑材料"的窗口内，
        /// 把该槽位应得的那一件直接返回，于是提交给服务器的 ids 就是我们在界面上显示的那一套。
        ///
        /// 三个必须守住的边界：
        ///   ① 只在 `TryClick`/`Request` 的窗口内生效（其余调用方：QoL 的够得着扫描、prosthetics 的钛合金
        ///      合成、CraftingFramework 的自定义 id 前缀 —— 一律看到原版行为）；
        ///   ② 这件物品必须在 Together 自己的池子 `items` 里、且没被 `exclusions` 排除（否则说明它这次不可提交，
        ///      交回原版，由 Together 决定是"缺料"还是"还在同步"）；
        ///   ③ 必须通过本模组的 `CraftSelector.Usable`（复刻原版硬规则：收藏、产物同类、装满的容器、性质与耐久）。
        /// </summary>
        private static bool GetMatchingItemPrefix(RecipeItem __instance, List<Item> items, List<Item> exclusions,
                                                 ref Item __result)
        {
            if (!Steering || __instance == null || items == null) return true;

            // 整个前缀包在 try/catch 里：它挂在原版最关键的材料判定上，任何一行出错都不该
            // 让 Together 的提交链断掉（原方法自己还有会抛 NRE 的分支，见 `Patches\PatchCompat.cs`）。
            try
            {
                int index = SlotIndex(__instance);
                if (index < 0 || index >= _steerSelection.Count) return true;

                Item wanted = _steerSelection[index];
                if (wanted == null) return true;
                if (!items.Contains(wanted)) return true;
                if (exclusions != null && exclusions.Contains(wanted)) return true;
                if (!CraftSelector.Usable(__instance, wanted)) return true;

                __result = wanted;
                return false;
            }
            catch (Exception e)
            {
                LogOnce("[联机] 选材引导失败（本次按原版匹配）：" + e.Message);
                return true;
            }
        }

        private static int SlotIndex(RecipeItem slot)
        {
            try
            {
                List<RecipeItem> list = _steerRecipe != null ? _steerRecipe.items : null;
                if (list == null) return -1;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] == slot) return i;
                }
            }
            catch { }
            return -1;
        }

        /// <summary>把选材结果压成一行给日志用。</summary>
        private static string Describe(List<Item> selection)
        {
            if (selection == null || selection.Count == 0) return "（无）";
            List<string> parts = new List<string>();
            for (int i = 0; i < selection.Count; i++)
            {
                Item it = selection[i];
                parts.Add(it == null ? "缺料" : CraftingScanner.NameOf(it));
            }
            return string.Join(" + ", parts.ToArray());
        }
    }
}
