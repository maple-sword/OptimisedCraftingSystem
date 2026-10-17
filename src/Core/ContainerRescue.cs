using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OptimisedCraftingSystem
{
    /// <summary>
    /// 「材料被整件消耗掉时，把它里面装的东西救出来」。
    ///
    /// 玩家需求（原话）：
    ///   ① 「合成时若需要消耗液体容器，内容物会直接消失，改进设计使优先不消耗装有液体的瓶子，
    ///      若需要消耗则将内容物使用临时瓶子装载」；
    ///   ② 「合成容器（如植物纤维袋合成植物纤维挎包）时，被消耗的容器必须全空，否则无法合成，
    ///      改进设计使装有物品的容器可以被消耗，内容物装于新容器内，装不下则掉落于地上」。
    ///
    /// 为什么必须在这一层动手（原版行为）：
    ///   · 「容器必须全空」写在**匹配阶段** —— `RecipeItem.GetMatchingItem`（`RecipeItem.cs:58-61`）
    ///     见到 `Container.itemCount > 0` 直接 `continue`，所以装着东西的容器压根不会被选中。
    ///     这一半在我们自己的候选筛选里放开就行（`CraftSelector.Usable`）。
    ///   · 真正动手消耗时（`RecipeItem.UseItem`，`RecipeItem.cs:167-175`）对**固体容器**只是
    ///     `Container.UnloadAllItems()`：把子物品脱离父级、打开物理、往上挪 1.5 —— 也就是原地倒下、
    ///     掉在地上；而对**液体容器**（`WaterContainerItem` 不是 `Container`）**什么都没做**，
    ///     `Object.Destroy(item.gameObject)` 之后瓶里的 `stack` 直接消失 —— 这就是"内容物直接消失"的根因。
    ///
    /// 做法：在 `UseItem` 真正销毁之前把东西**搬出来**（固体内容物先冻结、液体先倒进临时瓶），
    /// 于是原版的 `UnloadAllItems()` 变成空操作；等新产物被 `Body.AutoPickUpItem` 捡起时
    /// （`Patches\PatchStorage.cs`）把冻着的东西装进新容器，会话结束时还没装进去的就地落地。
    ///
    /// 安全网：不管合成成功、失败还是抛异常，`CraftSession.End` 都会 `Flush()`，
    /// 保证**没有任何一件物品会永远冻在那里**。
    /// </summary>
    internal static class ContainerRescue
    {
        /// <summary>已经搬出容器、等待装进新容器的东西（顺序即它们在旧容器里的顺序）。</summary>
        private static readonly List<Item> _pending = new List<Item>();

        /// <summary>本次合成搬了多少件 / 多重（证据行用）。</summary>
        private static int _suspended;
        private static float _suspendedWeight;

        /// <summary>被消耗的容器是谁（证据行里要写清"从哪个容器搬出来的"）。</summary>
        private static string _sourceName;

        /// <summary>产物压根不是容器（没地方装），落地文案要按这个说法写。</summary>
        private static bool _noTarget;

        /// <summary>会话开始：清掉上一次的残留计数与队列（队列本应在 End 里 Flush 干净，这里是兜底）。</summary>
        internal static void Begin()
        {
            // 上一次会话如果因为异常没能走到 End，队列里可能还冻着东西。
            // 先让它们落地再清空 —— 宁可掉在地上，也不能永远冻在手里（玩家会以为东西丢了）。
            if (_pending.Count > 0)
            {
                try { Flush(); }
                catch (System.Exception e) { Plugin.Log?.LogWarning("[Rescue] 上一次残留落地失败：" + e.Message); }
            }
            _pending.Clear();
            _suspended = 0;
            _suspendedWeight = 0f;
            _sourceName = null;
            _noTarget = false;
        }

        /// <summary>还有东西冻在手里（等新容器出现）。</summary>
        internal static bool HasPending => _pending.Count > 0;

        // ————————————————————————————————————————————————————————————
        // 消耗前：把东西搬出来
        // ————————————————————————————————————————————————————————————

        /// <summary>
        /// `RecipeItem.UseItem` 的前缀调用：这个槽位马上要整件消耗掉 <paramref name="item"/> 了，
        /// 先把里面的东西搬出来。只要不是"整件消耗"（比如当工具用、不销毁），就什么都不用做 ——
        /// 那件东西连着自己装的东西一起留在玩家手里。
        /// </summary>
        internal static void BeforeConsume(RecipeItem slot, Item item)
        {
            if (slot == null || item == null) return;
            if (slot.isLiquid || !slot.destroyItem) return;

            // 只在合成会话里动手：搬出来的东西要等 `CraftSession.End` 才会收尾（装进新容器或落地），
            // 会话外搬出来就成了没人管、永远冻着的物品。会话外的调用（别的模组直接调 `UseItem`）
            // 一律按原版处理 —— 宁可维持原样，也不能留下一个"卡住的物品"。
            if (!CraftSession.Active) return;

            // ① 液体：原版会把瓶子和瓶里的液体一起销毁（`RecipeItem.cs:167-175` 只管固体容器）
            WaterContainerItem wc = item.GetComponent<WaterContainerItem>();
            if (wc != null && wc.CurrentTotal > 0f && ModSettings.RescueConsumedLiquid)
                RescueLiquid(wc, item);

            // ② 固体内容物：先搬出来冻住，等新容器出现再装进去（装不下就落地）
            Container c = item.container;
            if (c != null && c.itemCount > 0 && ModSettings.CarryContainerContents)
                Suspend(c, item);
        }

        /// <summary>把容器里的东西全部脱离出来并冻结，等新容器出现。</summary>
        private static void Suspend(Container c, Item owner)
        {
            List<Transform> kids = new List<Transform>();
            foreach (Transform t in c.transform) kids.Add(t);   // 先快照：边遍历边改父级会漏掉后面的

            foreach (Transform t in kids)
            {
                if (t == null) continue;
                Item it = t.GetComponent<Item>();
                if (it == null) continue;

                // 脱离那件即将被 Destroy 的容器 —— 留在里面会跟着一起没
                t.SetParent(null);
                // 冻住：合成半途让它滚走，玩家会以为东西丢了
                if (it.rb != null) it.rb.simulated = false;
                // 先藏起来：落地或入袋时按原版规则重新显示（`Container.LoadItem` 里就用 itemsVisible）
                SpriteRenderer sr = t.GetComponent<SpriteRenderer>();
                if (sr != null) sr.enabled = false;

                _pending.Add(it);
                _suspended++;
                _suspendedWeight += it.totalWeight;
            }

            if (_suspended > 0 && string.IsNullOrEmpty(_sourceName))
                _sourceName = CraftingScanner.NameOf(owner);
        }

        /// <summary>
        /// 把这件瓶子里的液体倒进现场做的临时瓶（可能不止一个：临时瓶容量 1000 mL）。
        /// 搬走多少就从源瓶里扣多少，剩下的（连一个临时瓶都做不出来时）才会跟着瓶子消失。
        /// </summary>
        private static void RescueLiquid(WaterContainerItem src, Item owner)
        {
            List<LiquidStack> layers = new List<LiquidStack>(src.stack);
            List<float> removed = new List<float>();
            for (int i = 0; i < src.stack.Count; i++) removed.Add(0f);

            StringBuilder sb = new StringBuilder();
            float moved = 0f;

            // 借用「正在倒液体」标记：让临时瓶的 `AddLiquid` 与 `AutoPickUpItem` 都走原版，
            // 否则 `AutoPickUpItem` 的前缀会把临时瓶当成"这次合成的产物"，
            // 抢走真正产物的存放计划（`CraftSession.TakeSolidPlan`）。
            bool prev = PatchWaterContainerAddLiquid.Pouring;
            PatchWaterContainerAddLiquid.Pouring = true;
            try
            {
                for (int i = 0; i < layers.Count; i++)
                {
                    LiquidStack layer = layers[i];
                    if (layer == null || layer.amount <= 0f) continue;

                    float left = layer.amount;
                    // 一个临时瓶装不下就再做一个（取 16 只是防呆上限，正常最多两三个）
                    for (int guard = 0; left > 0.5f && guard < 16; guard++)
                    {
                        float got = PlacementPlanner.CreateTempBottle(layer.liquidId, left, note: false);
                        if (got <= 0f) break;

                        moved += got;
                        left -= got;
                        if (i < removed.Count) removed[i] += got;

                        if (sb.Length > 0) sb.Append(Loc.IsChinese ? "、" : ", ");
                        sb.Append(CraftText.LiquidName(layer.liquidId))
                          .Append(' ').Append(Loc.T("ocs.unit.ml", got));
                    }
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning("[Rescue] 倒进临时瓶失败：" + e.Message);
            }
            finally
            {
                PatchWaterContainerAddLiquid.Pouring = prev;
            }

            if (moved <= 0f) return;

            // 只扣掉真搬走的部分：搬不走的留在瓶里，跟着瓶子一起没也不是我们吞的
            try { src.Drain(removed); }
            catch (System.Exception e) { Plugin.Log?.LogWarning("[Rescue] 清空源瓶失败：" + e.Message); }

            Plugin.Evidence("rescue:liquid",
                "液体救援：合成要消耗「" + CraftingScanner.NameOf(owner) + "」，先把里面剩下的 " +
                sb + " 装进现场做的临时瓶（与原版一致，300 秒后连瓶带液体一起销毁），没有跟着瓶子消失。");
        }

        // ————————————————————————————————————————————————————————————
        // 新容器出现：把东西装进去
        // ————————————————————————————————————————————————————————————

        /// <summary>
        /// 新产物被捡起时调用（`Patches\PatchStorage.cs` 的 `Body.AutoPickUpItem` 前缀）。
        /// 只有产物自己就是容器（植物纤维挎包之类）才有地方装；装不下的留在队列里，会话收尾时落地。
        /// 返回真的装进去了几件。
        /// </summary>
        internal static int Deliver(Item result)
        {
            if (result == null) return 0;

            Container dst = result.container;
            if (dst == null)
            {
                // 产物自己不是容器（比如做的是绷带）：没地方装，收尾时落地。
                // 记下这个情况，好让落地那条证据行的说法准确（"产品不是容器" vs "新容器装不下"）。
                if (_pending.Count > 0) _noTarget = true;
                return 0;
            }

            if (_pending.Count == 0) return 0;

            int delivered = 0;
            List<Item> left = new List<Item>();   // 没装进去的（按原顺序留在队列里）

            foreach (Item it in _pending)
            {
                if (it == null) continue;
                bool ok = false;
                try
                {
                    // 原版 `Container.LoadItem` 不返回成功与否（重量、距离、容器套容器都可能把它挡下来），
                    // 所以调用完**按父级核对**，没真装进去的留在队列里等 Flush 落地 ——
                    // 否则会有一件东西既不在容器里、又不在队列里，等于凭空消失。
                    if (dst.CanHoldItem(it))
                    {
                        dst.LoadItem(it);
                        ok = it.transform.parent == dst.transform;
                    }
                }
                catch (System.Exception e)
                {
                    Plugin.Log?.LogWarning("[Rescue] 装进新容器失败：" + e.Message);
                }

                if (ok) delivered++;
                else left.Add(it);
            }

            _pending.Clear();
            _pending.AddRange(left);

            if (delivered > 0)
            {
                Plugin.Evidence("rescue:contents:" + result.id,
                    "内容物转移：合成消耗的「" + _sourceName + "」里搬出 " + _suspended + " 件（" +
                    FormatWeight(_suspendedWeight) + "），其中 " + delivered + " 件已装进新做的「" +
                    CraftingScanner.NameOf(result) + "」" +
                    (_pending.Count > 0 ? "，剩下 " + _pending.Count + " 件装不下，掉在脚边。" : "。"));
            }

            return delivered;
        }

        /// <summary>会话收尾：还冻在手里的东西一律落地（合成失败、产物不是容器、新容器装不下都走这里）。</summary>
        internal static void Flush()
        {
            if (_pending.Count > 0)
            {
                int dropped = 0;
                float droppedWeight = 0f;

                foreach (Item it in _pending)
                {
                    if (it == null) continue;
                    try
                    {
                        Transform t = it.transform;
                        t.SetParent(null);
                        // 与原版 `Container.UnloadAllItems` 同样的落地方式：
                        // 打开物理 + 往上挪一点，免得和玩家或合成点的东西重叠
                        if (it.rb != null) it.rb.simulated = true;
                        t.position += Vector3.up * 1.5f;
                        SpriteRenderer sr = t.GetComponent<SpriteRenderer>();
                        if (sr != null) sr.enabled = true;
                        dropped++;
                        droppedWeight += it.totalWeight;
                    }
                    catch (System.Exception e)
                    {
                        Plugin.Log?.LogWarning("[Rescue] 内容物落地失败：" + e.Message);
                    }
                }

                if (dropped > 0)
                {
                    // 两种情况的说法不一样：产物根本不是容器，还是新容器装不下
                    if (_noTarget)
                    {
                        Plugin.Evidence("rescue:noTarget:" + (_sourceName ?? "?"),
                            "内容物转移：合成消耗的「" + _sourceName + "」里搬出 " + _suspended + " 件（" +
                            FormatWeight(_suspendedWeight) + "），这次的产品不是能装东西的容器，全部掉在脚边。");
                    }
                    else
                    {
                        Plugin.Evidence("rescue:dropped",
                            "内容物落地：合成消耗的「" + _sourceName + "」里搬出 " + _suspended + " 件（" +
                            FormatWeight(_suspendedWeight) + "），新容器装不下这 " + dropped + " 件（" +
                            FormatWeight(droppedWeight) + "），掉在脚边。");
                    }

                    // 玩家最容易误解成"东西没了"，所以这里除了日志还给一条屏幕提示
                    PlayerCamera cam = PlayerCamera.main;
                    if (cam != null) cam.DoAlert(Loc.T("ocs.craft.contents.dropped"));
                }
            }

            _pending.Clear();
            _suspended = 0;
            _suspendedWeight = 0f;
            _sourceName = null;
            _noTarget = false;
        }

        /// <summary>重量文案（与 `CraftText.ContainerContents` 里的写法保持一致：`0.## kg`）。</summary>
        private static string FormatWeight(float kg)
        {
            return kg.ToString("0.##") + " kg";
        }
    }
}
