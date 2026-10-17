using HarmonyLib;

namespace OptimisedCraftingSystem
{
    /// <summary>
    /// 存放接管：让合成界面里"预测的存放地点"真的成为产物落点。
    ///
    /// 为什么补这里而不是像原模组那样事后补救（`StorageLogic.AutoPickup` 手工把物品
    /// 塞进容器）：产物的落点是**原版在生成后立刻决定**的（第一个空槽位 → 第一个装得下的
    /// 容器，见 `Body.cs:836-869`）。事后挪动会撞上 `Container.LoadItem` 的"容器套容器"
    /// 拒绝（`Container.cs:116-123`），也会把玩家排好的背包重新洗牌。所以就在产物落位
    /// 的那一行接管 —— 预测与实际用的是同一套算法（`PlacementPlanner`）。
    /// </summary>
    [HarmonyPatch(typeof(Body), nameof(Body.AutoPickUpItem))]
    internal static class PatchBodyAutoPickUpItem
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Low)]
        private static bool Prefix(Body __instance, Item item)
        {
            // 被消耗容器里搬出来的东西：新产物自己就是容器时立刻装进去（装不下的留在队里，
            // 会话收尾时落地）。这一句必须在下面所有守卫**之前** —— 它跟"存放预测"没关系，
            // 漏掉就会让那几件东西一直冻在手里。
            int delivered = 0;
            if (item != null) delivered = ContainerRescue.Deliver(item);

            if (PatchWaterContainerAddLiquid.Pouring) return true;     // 我们自己在倒液体 / 做临时瓶时让路
            if (!ModSettings.HonorPrediction) return true;             // 玩家关掉了"按预测执行存放"
            if (!CraftSession.Active) return true;                     // 只在合成过程中接管
            if (__instance == null || __instance != CraftingScanner.PlayerBody) return true;
            if (item == null || item.Stats == null) return true;

            // 刚把内容物装进这件新容器 → 它已经不是"空容器"了。原版不允许把有内容的容器再塞进别的容器
            // （`Container.cs:116-119`：只弹一条提示、根本不装载），所以别去撞存放预测里的"装进某容器"，
            // 直接交回原版（进背包空槽），免得计划落空还多弹一条原版提示。
            if (delivered > 0 && item.container != null) return true;

            try
            {
                Recipe recipe = CraftSession.CurrentRecipe;
                bool liquidResult = recipe != null && recipe.result != null && recipe.result.isLiquid;

                // 液体合成：原版（和原模组）会把临时瓶塞进背包，5 分钟后连瓶带液体一起销毁。
                // 已经有明确去向（玩家指定、或预测到某个瓶子）时，就不该再留这个临时瓶。
                if (liquidResult && CraftSession.HasLiquidTarget && item.id == "craftingbottle")
                    return false;

                if (item.Stats.wearable) return true;   // 穿戴仍走原版，预测只用于界面显示

                PlacementPlanner.Plan plan = CraftSession.TakeSolidPlan();
                if (!plan.Valid)
                    plan = PlacementPlanner.PredictOne(item.Stats, item.condition, null);
                if (plan.Kind == PlacementPlanner.Destination.Wear) return true;

                if (PlacementPlanner.Execute(item, plan)) return false;

                // 计划落空（槽位被占、容器已满…）→ 现挑一个容器，仍落空就交回原版
                Container fallback = PlacementPlanner.BestContainer(item.Stats, item.condition);
                if (fallback != null && PlacementPlanner.Execute(
                        item,
                        PlacementPlanner.Make(PlacementPlanner.Destination.Container, PlacementPlanner.Label(fallback), fallback)))
                {
                    return false;
                }

                return true;
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning("[Store] 按预测存放失败，退回原版：" + e.Message);
                return true;
            }
        }
    }

    /// <summary>
    /// 液体产物的去向改道。
    ///
    /// 原版 `RecipeResult.SpawnResult` 只认"单种液体且一次装得下"的瓶子
    /// （`RecipeResult.cs:44-56`），否则造临时瓶（300 秒后连液体一起销毁）。
    /// 玩家在界面上选定目标容器（或预测器选出了更好的瓶子）时，在 `AddLiquid` 这一层改道：
    /// 先倒目标 → 余量续瓶 → 临时瓶 → 最后兜底倒进原版挑中的那个瓶子（它必然兼容同种液体）。
    /// </summary>
    [HarmonyPatch(typeof(WaterContainerItem), nameof(WaterContainerItem.AddLiquid))]
    internal static class PatchWaterContainerAddLiquid
    {
        /// <summary>改道进行中（防止我们自己的 `AddLiquid` 调用又被前缀拦一次）。</summary>
        internal static bool Pouring;

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Low)]
        private static bool Prefix(WaterContainerItem __instance, string liquidId, float amount, ref float __result)
        {
            if (Pouring) return true;
            if (!ModSettings.HonorPrediction) return true;
            if (!CraftSession.Active) return true;
            // 只拦"产出液体入瓶"这一次调用；否则会话期间瓶子的自动补水也会被改道。
            if (!CraftSession.LiquidPending) return true;
            if (__instance == null || amount <= 0f) return true;

            Recipe recipe = CraftSession.CurrentRecipe;
            if (recipe == null || recipe.result == null || !recipe.result.isLiquid) return true;

            bool forceTemp = CraftSession.LiquidForceTemp;
            WaterContainerItem target = CraftSession.LiquidTarget;
            if (!forceTemp && (target == null || target == __instance)) return true;

            Pouring = true;
            try
            {
                __result = forceTemp
                    ? PlacementPlanner.CreateTempBottle(liquidId, amount)
                    : PlacementPlanner.Pour(liquidId, amount, target, __instance);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning("[Pour] 液体改道失败，退回原版：" + e.Message);
                return true;
            }
            finally
            {
                Pouring = false;
            }
            CraftSession.LiquidDone();
            return false;
        }
    }
}
