using System.Collections.Generic;
using UnityEngine;

namespace OptimisedCraftingSystem
{
    /// <summary>
    /// 模组设置。
    ///
    /// 设计取舍（与原模组 BetterInventoryLogic 的差异，属于"可改进的设计"之一）：
    ///   1. **不引入第二套持久化**。原模组用 BepInEx 配置文件 + 游戏 settings.json 双写，
    ///      于是必须写一套"加载窗口"逻辑去决定谁权威。这里只注册进游戏自己的设置列表
    ///      （Settings.settings），持久化完全交给游戏的 settings.json —— 单一数据源，无同步 bug。
    ///   2. **运行时用静态字段，不缓存比较器链表**。原模组把比较器对象按开关顺序存进
    ///      List，每次改设置都要重建；这里比较器是纯函数式的，直接读静态开关，
    ///      改设置立即生效、无状态可错。
    ///   3. **沿用原模组的设置名**（prefer_on_floor / compare_storage_* / liquids_* …）：
    ///      汉化模组 A.Multi_Mod.Chinese.Localization 内置了这些键的翻译，
    ///      改名会让玩家的汉化失效。新增能力统一用 ocs. 前缀。
    /// </summary>
    internal static class ModSettings
    {
        // ————————————————————————————————————————————————————————————
        // 运行时状态（所有热路径都直接读这些字段，避免每次 Settings.Find 的开销）
        // ————————————————————————————————————————————————————————————

        // 选材（沿用原模组语义）
        internal static bool PickEnabled = true;         // ocs.pick.smart：启用智能选材（关闭则退回原模组的顺序比较）
        internal static bool PreferOnFloor = true;       // prefer_on_floor
        internal static bool PreferNotContainer = true;  // prefer_not_container
        internal static bool PreferNotWearable = true;   // prefer_not_wearable
        internal static bool PreferOnBody = true;        // prefer_on_body
        internal static bool PreferHighQuality = true;   // prefer_high_quality
        internal static bool PreferLowValue = true;      // prefer_low_value
        internal static bool PreferLowCondition = true;  // prefer_low_condition
        internal static bool ReserveScarce = true;       // ocs.pick.reserve_scarce

        // 产物存放
        internal static bool NotifyWhereStored = true;   // notify_where_stored
        internal static bool StoreInContainersFirst = true; // store_in_containers_first
        internal static bool CompareStorageType = true;      // compare_storage_type
        internal static bool CompareStorageReduction = true; // compare_storage_reduction
        internal static bool CompareStorageFull = true;      // compare_storage_full
        internal static bool CompareStorageCapacity = true;  // compare_storage_capacity
        internal static bool CompareStorageBestCondition = true; // compare_storage_best_condition
        internal static bool PredictStorage = true;          // ocs.store.predict
        internal static bool HonorPrediction = true;         // ocs.store.honor

        // 液体存放
        internal static bool CanFillNewBottles = true;   // liquids_can_fill_new_bottles
        internal static bool CanMixLiquids = false;      // liquids_can_mix
        internal static bool CompareLiquidUnmixed = true;    // compare_liquid_unmixed_enabled
        internal static bool CompareLiquidStacking = true;   // compare_liquid_stacking_enabled
        internal static bool CompareLiquidWeightRatio = true; // compare_liquid_weight_enabled
        internal static bool AllowTempBottleTarget = true;   // ocs.liquid.tempbottle
        internal static bool SkipFavoritedBottles = true;    // ocs.liquid.skip_favorited

        // 材料消耗：被整件消耗掉的容器
        internal static bool CarryContainerContents = true;  // ocs.craft.container_contents
        internal static bool PreferEmptyBottle = true;       // ocs.craft.prefer_empty_bottle
        internal static bool RescueConsumedLiquid = true;    // ocs.craft.rescue_liquid

        // 界面
        internal static bool ShowConsumption = true;     // ocs.ui.consumption
        internal static bool StackItems = true;          // ocs.ui.stacking
        internal static bool VerboseNumbers = false;     // ocs.ui.verbose
        internal static bool ContainerTotalsInTip = true; // ocs.tip.container_totals
        internal static bool ContainerWeightInTip = true; // ocs.tip.container_weight
        internal static bool WindowEnabled = true;       // ocs.window.enabled
        internal static bool WindowIncludeFloor = true;  // ocs.window.floor
        internal static KeyCode WindowKey = KeyCode.M;   // ocs.key.window

        /// <summary>当前这一次 DefaultSettings() 调用产生的设置对象（每次重建，见下方 Postfix）。</summary>
        internal static readonly List<Setting> All = new List<Setting>();

        /// <summary>设置项的静态描述表：name / 默认值 / 中英文标题与说明。</summary>
        private sealed class BoolDef
        {
            internal string Name;
            internal bool Default;
            internal string Zh, En, ZhDsc, EnDsc;
        }

        private static readonly BoolDef[] Bools =
        {
            // ——— 智能选材 ———
            new BoolDef { Name = "ocs.pick.smart", Default = true,
                Zh = "智能选材：按性质性价比自动匹配", En = "Smart picking: match by quality value",
                ZhDsc = "开启后，合成时不再按固定顺序挑材料，而是计算每种候选材料「满足该槽位性质需求」的成本（价值、耐久、来源），选最划算的那个。\n关闭后退回原模组的顺序比较逻辑。",
                EnDsc = "Instead of a fixed priority list, evaluate every candidate by the real cost of satisfying the slot (value, condition, location).\nTurn off to fall back to the original ordered comparison." },
            new BoolDef { Name = "ocs.pick.reserve_scarce", Default = true,
                Zh = "智能选材：避免用掉别处唯一的材料", En = "Smart picking: keep scarce items",
                ZhDsc = "如果某件物品是另一个槽位唯一可用的材料，而当前槽位还有别的选择，就优先用别的 —— 避免出现「A 槽吃掉了 B 槽唯一的材料」导致无法合成。",
                EnDsc = "If an item is the only candidate for another slot, prefer something else for the current slot so the whole recipe can still be made." },
            new BoolDef { Name = "prefer_on_floor", Default = true,
                Zh = "优先使用地面上的物品", En = "Prefer items on the floor",
                ZhDsc = "在成本相近时，优先消耗地上的物品（省得背包占地方）。", EnDsc = "When cost is similar, prefer consuming items lying on the floor." },
            new BoolDef { Name = "prefer_not_container", Default = true,
                Zh = "优先不使用容器里的物品", En = "Prefer items outside containers",
                ZhDsc = "在成本相近时，优先不用装在背包/箱子里的物品（避免翻找、也避免把容器里的东西掏空）。",
                EnDsc = "When cost is similar, avoid consuming items stored inside containers." },
            new BoolDef { Name = "prefer_not_wearable", Default = true,
                Zh = "优先不消耗已穿戴的装备", En = "Prefer not to consume worn gear",
                ZhDsc = "在成本相近时，不去拆身上正穿着的衣服/装备。", EnDsc = "When cost is similar, do not salvage gear you are currently wearing." },
            new BoolDef { Name = "prefer_on_body", Default = true,
                Zh = "优先使用身上的物品", En = "Prefer items on your body",
                ZhDsc = "在成本相近时，优先用自己随身带的物品，而不是手边地上的。", EnDsc = "When cost is similar, prefer items you are carrying over ones on the ground." },
            new BoolDef { Name = "prefer_high_quality", Default = true,
                Zh = "优先性质更充足的材料", En = "Prefer richer quality",
                ZhDsc = "当同一种性质有多个来源时，优先用「单位消耗能提供的性质更多」的材料：例如提供 5 点性质的工具只掉 20% 耐久，提供 1 点的要掉满。",
                EnDsc = "Prefer the source that provides more of the required quality per unit consumed (a tool providing 5 loses 20% durability where one providing 1 loses all of it)." },
            new BoolDef { Name = "prefer_low_value", Default = true,
                Zh = "优先消耗价值更低的材料", En = "Prefer lower-value materials",
                ZhDsc = "核心省钱开关：把「这次消耗掉的价值」算出来，优先用便宜的。整件消耗的槽位按整件价值算，按耐久消耗的槽位按比例算。",
                EnDsc = "Compare the actual value destroyed by this craft: full item value for consumed materials, proportional value for durability-based tools." },
            new BoolDef { Name = "prefer_low_condition", Default = true,
                Zh = "优先消耗耐久更低的材料", En = "Prefer more worn materials",
                ZhDsc = "同等条件下优先用已经快坏掉的物品（物尽其用）。", EnDsc = "All else equal, use the item that is already close to breaking." },

            // ——— 产物存放 ———
            new BoolDef { Name = "notify_where_stored", Default = true,
                Zh = "提示产物存放位置", En = "Announce where the result was stored",
                ZhDsc = "合成完成后弹提示，告诉玩家产物进了哪个槽位/容器。", EnDsc = "Show an alert telling you which slot or container received the crafted item." },
            new BoolDef { Name = "store_in_containers_first", Default = true,
                Zh = "优先把产物放进容器", En = "Put results into containers first",
                ZhDsc = "开启时产物优先塞进背包/箱子，塞不下再占用身上的槽位。", EnDsc = "Try containers before taking up a body slot." },
            new BoolDef { Name = "compare_storage_type", Default = true,
                Zh = "存放：优先标签匹配的容器", En = "Storage: prefer matching tags",
                ZhDsc = "容器有标签限制（tagRestriction）时，优先放进「允许放这件东西」的容器。", EnDsc = "Prefer containers whose tag restriction accepts this item." },
            new BoolDef { Name = "compare_storage_reduction", Default = true,
                Zh = "存放：优先减重效果好的容器", En = "Storage: prefer weight reduction",
                ZhDsc = "优先放进背起来更省力的容器（encumberanceMult 更小）。", EnDsc = "Prefer the container that encumbers you less." },
            new BoolDef { Name = "compare_storage_full", Default = true,
                Zh = "存放：优先装进更满的容器", En = "Storage: prefer fuller containers",
                ZhDsc = "尽量把东西集中到已经装了东西的容器里，少占新的容器。\n（原模组这个开关的实现与文案相反：它实际选的是最空的容器，本模组已修正。）",
                EnDsc = "Consolidate into containers that already hold things.\n(The original mod's implementation was inverted; fixed here.)" },
            new BoolDef { Name = "compare_storage_capacity", Default = true,
                Zh = "存放：优先容量更大的容器", En = "Storage: prefer larger capacity",
                ZhDsc = "在其它条件相同时，优先用总容量更大的容器（大件物品更容易找到归宿）。", EnDsc = "All else equal, prefer the container with more total capacity." },
            new BoolDef { Name = "compare_storage_best_condition", Default = true,
                Zh = "存放：优先耐久更好的容器", En = "Storage: prefer sturdier containers",
                ZhDsc = "优先把东西放进状态更好的容器里，避免东西跟着破容器一起掉。", EnDsc = "Prefer containers in better condition." },
            new BoolDef { Name = "ocs.store.predict", Default = true,
                Zh = "预测产物的存放/装备地点", En = "Predict where results will go",
                ZhDsc = "在合成界面里直接显示「这个产物会进哪个槽位 / 哪个容器 / 穿到哪个部位」，不用等合成了才知道。",
                EnDsc = "Show which slot / container / limb the crafted item will end up in, right on the crafting screen." },
            new BoolDef { Name = "ocs.store.honor", Default = true,
                Zh = "按预测执行存放", En = "Actually follow the prediction",
                ZhDsc = "开启时，真正的存放行为会和界面上预测的一致（包括你手动改派的目标）；关闭后仍由游戏原版逻辑决定。",
                EnDsc = "Make the real storage action match what the UI predicted (including your manual overrides); off means vanilla behaviour decides." },

            // ——— 液体 ———
            new BoolDef { Name = "liquids_can_fill_new_bottles", Default = true,
                Zh = "允许装进空瓶", En = "Allow filling empty bottles",
                ZhDsc = "合成出的液体会优先装进随身携带的液体容器；空瓶也允许使用。", EnDsc = "Crafted liquids may be poured into empty bottles you carry." },
            new BoolDef { Name = "liquids_can_mix", Default = false,
                Zh = "允许混装液体", En = "Allow mixing liquids",
                ZhDsc = "允许把新液体倒进已经装了别种液体的瓶子里（会混在一起）。", EnDsc = "Allow pouring the new liquid into a bottle that already holds a different one." },
            new BoolDef { Name = "compare_liquid_unmixed_enabled", Default = true,
                Zh = "液体：优先不混装", En = "Liquid: prefer unmixed bottles",
                ZhDsc = "优先选成分单一的瓶子，保持液体纯净。", EnDsc = "Prefer bottles holding a single liquid." },
            new BoolDef { Name = "compare_liquid_stacking_enabled", Default = true,
                Zh = "液体：优先已有同类液体的瓶子", En = "Liquid: prefer same liquid",
                ZhDsc = "优先把液体倒进已经装了同种液体的瓶子，方便合并。", EnDsc = "Prefer bottles that already contain the same liquid." },
            new BoolDef { Name = "compare_liquid_weight_enabled", Default = true,
                Zh = "液体：优先轻便的瓶子", En = "Liquid: prefer lighter bottles",
                ZhDsc = "优先用「自重/容量」更小的容器，减少负重。", EnDsc = "Prefer the bottle with the best weight-to-capacity ratio." },
            new BoolDef { Name = "ocs.liquid.tempbottle", Default = true,
                Zh = "允许使用临时瓶", En = "Allow makeshift bottles",
                ZhDsc = "所有容器都装不下时，现场做一个临时瓶。与原版一样，临时瓶 300 秒（5 分钟）后连瓶子带里面剩的液体一起销毁，所以合成完请尽快把液体倒进正式容器。关闭后，装不下的部分会留在原版挑中的容器里。",
                EnDsc = "When nothing fits, make a makeshift bottle on the spot. Like vanilla, it is destroyed after 300 s (5 min) together with whatever liquid is still inside, so pour it into a real container soon. Off keeps the overflow in the container the game picked." },
            new BoolDef { Name = "ocs.liquid.skip_favorited", Default = true,
                Zh = "收藏的容器不装液体", En = "Keep liquid out of favourited containers",
                ZhDsc = "打了星标（收藏）的容器不会被倒进液体：自动规划直接跳过它，候选表里它也会整行置灰点不动，免得失手把药水倒进自己的水壶。原版也是这个意思 —— 收藏的材料不会被当耗材烧掉（`RecipeItem.cs:29`）。取消星标即可恢复。",
                EnDsc = "A starred (favourited) container never receives liquid: the planner skips it and the candidate list greys it out, so a craft cannot pour into your canteen by accident. Vanilla treats favourites the same way for ingredients (`RecipeItem.cs:29`). Un-star it to allow it again." },

            // ——— 材料消耗：被整件消耗掉的容器（玩家需求）———
            new BoolDef { Name = "ocs.craft.container_contents", Default = true,
                Zh = "装着东西的容器也能当材料", En = "Allow filled containers as ingredients",
                ZhDsc = "原版要求被消耗的容器必须全空（`RecipeItem.cs:58-61`），所以装满东西的袋子根本不会被选中。开启后装着东西的容器也能当材料：内容物会在消耗前搬出来，新做出来的容器能装多少装多少，装不下的掉在脚边。",
                EnDsc = "Vanilla refuses to consume a container that still holds items (`RecipeItem.cs:58-61`). With this on, filled containers can be used as ingredients: the contents are moved out first, loaded into the newly crafted container, and whatever does not fit is dropped on the ground." },
            new BoolDef { Name = "ocs.craft.prefer_empty_bottle", Default = true,
                Zh = "优先消耗空瓶子", En = "Prefer empty bottles",
                ZhDsc = "需要把液体容器整件消耗掉时，优先挑里面没装液体的：液体是玩家的资源，不该因为瓶子便宜就一起消耗掉。",
                EnDsc = "When a recipe consumes a whole liquid container, prefer the ones that hold no liquid — the liquid inside is a resource of its own." },
            new BoolDef { Name = "ocs.craft.rescue_liquid", Default = true,
                Zh = "消耗瓶子前把液体装进临时瓶", En = "Save liquid from consumed bottles",
                ZhDsc = "原版把瓶子当材料消耗时，瓶里的液体会跟着一起消失（`RecipeItem.cs:167-175` 只处理固体容器）。开启后会在消耗前把液体倒进现场做的临时瓶（与原版一致，300 秒后连瓶带液体销毁），一点都不会丢。",
                EnDsc = "Vanilla destroys the liquid inside a bottle used as an ingredient (`RecipeItem.cs:167-175`). With this on, the liquid is poured into a makeshift bottle first (destroyed after 300 s, like vanilla) so nothing is lost." },

            // ——— 界面 ———
            new BoolDef { Name = "ocs.ui.consumption", Default = true,
                Zh = "合成界面显示消耗量", En = "Show consumption in the crafting screen",
                ZhDsc = "在每个材料槽旁边显示这次合成会消耗多少（液体显示 mL，固体显示耐久百分比或「整件消耗」）。",
                EnDsc = "Next to each ingredient, show exactly what this craft consumes (mL for liquids, condition % for solids)." },
            new BoolDef { Name = "ocs.ui.stacking", Default = true,
                Zh = "合成界面堆叠显示相同材料", En = "Stack identical ingredients",
                ZhDsc = "同一槽位需要多份相同材料时合并成一行显示「帆布 ×3」。", EnDsc = "Merge repeated identical ingredients into one line." },
            new BoolDef { Name = "ocs.ui.verbose", Default = false,
                Zh = "显示详细数值", En = "Show detailed numbers",
                ZhDsc = "在材料总览里额外显示每件材料的价值、重量、可用次数与成本明细，便于验证算法。",
                EnDsc = "Show value, weight, uses and cost breakdown for every material in the materials window." },
            new BoolDef { Name = "ocs.tip.container_totals", Default = true,
                Zh = "按住 Shift 查看容器内容总量", En = "Hold Shift for container contents",
                ZhDsc = "按住「展开描述」键（默认左 Shift）时，物品提示里会列出它内部所有物品的合计数量（帆布（数量）……）。",
                EnDsc = "While holding the expand-description key (Left Shift by default), list the total amount of every item inside a container." },
            new BoolDef { Name = "ocs.tip.container_weight", Default = true,
                Zh = "同时显示内容物总重量", En = "Also show contents weight",
                ZhDsc = "在上面那条里追加「内容物总重 x kg / 剩余空间 y kg」，方便判断还能装多少。",
                EnDsc = "Append total contents weight and remaining capacity to the same block." },
            new BoolDef { Name = "ocs.window.enabled", Default = true,
                Zh = "启用材料总览界面", En = "Enable the materials window",
                ZhDsc = "用独立热键打开一个面板，列出身上与手边所有可用来合成的材料、它们的性质、来源与性价比，以及全部配方的性质需求。",
                EnDsc = "A standalone panel listing every material available for crafting, its qualities, source and cost, plus every recipe's quality requirements." },
            new BoolDef { Name = "ocs.window.floor", Default = true,
                Zh = "材料总览包含手边地面物品", En = "Include floor items in the window",
                ZhDsc = "除了身上和容器里的东西，也把 10 米内能捡起来的地面物品算进来。", EnDsc = "Also list pickable items within 10 units on the ground." },
        };

        /// <summary>键位设置项的名称（默认 M：Material）。</summary>
        internal const string KeyWindow = "ocs.key.window";

        /// <summary>
        /// 构造本模组的全部设置项。**每次 <see cref="Settings.DefaultSettings"/> 被调用时都要重新构造**
        /// —— 游戏每次调用它都会返回一个全新的列表，旧的设置对象会被丢弃
        /// （进程内默认只在 EnsureLoaded 里建一次，但设置菜单的"恢复默认"会重建）。
        /// 重建时用 <see cref="_lastKnown"/> 里的既有值做种子，避免把玩家刚改的设置重置掉。
        /// </summary>
        internal static void Build()
        {
            All.Clear();

            foreach (BoolDef def in Bools)
            {
                string name = def.Name;
                All.Add(new SettingBool
                {
                    name = name,
                    value = _lastKnown.TryGetValue(name, out bool remembered) ? remembered : def.Default,
                    category = Setting.SettingCategory.Game,
                    // 点击开关后：把该设置的新值同步进运行时静态字段（不重建任何缓存 → 立即生效）
                    apply = ApplyFromSettings,
                });
            }

            All.Add(new SettingKeybind
            {
                name = KeyWindow,
                value = _lastKeybind ?? KeyCode.M,
                category = Setting.SettingCategory.Input,
                apply = ApplyFromSettings,
            });

            ApplyFromSettings();
        }

        /// <summary>
        /// 把本模组的设置项挂进游戏设置列表（由 `Settings.DefaultSettings` 的后缀调用）。
        /// 先按 <see cref="Build"/> 重新构造，再跳过列表里已存在的同名项 —— 因此
        /// "恢复默认"或多次调用都是幂等的，不会出现重复条目。
        /// </summary>
        internal static void Attach(List<Setting> list)
        {
            if (list == null) return;
            try
            {
                if (Resetting)
                {
                    // 玩家点了设置界面的"恢复默认"（SettingsMenu.ResetToDefault 的前缀置位）：
                    // 这一次必须真的回到默认值，不能再拿 _lastKnown 里的旧值做种子
                    // —— 旧模组的同类缺陷（把静态实例塞回列表）正是在这里让"恢复默认"失效的。
                    _resetApplied = true;
                    _lastKnown.Clear();
                    _lastKeybind = null;
                }
                Build();
                foreach (Setting s in All)
                {
                    if (s != null && !ContainsName(list, s.name)) list.Add(s);
                }
                ApplyFromSettings();
                InjectLocaleKeys();
                if (Resetting)
                {
                    // 证据日志：点"恢复默认"是玩家侧的一次交互，日志里留痕才能被实测表引用
                    Plugin.Evidence("reset", "恢复默认：本模组设置项已按默认值重建（" +
                                             (All != null ? All.Count : 0) + " 项）。");
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning("[Settings] 挂载设置项失败（忽略）：" + e.Message);
            }
            finally
            {
                Resetting = false;
            }
        }

        /// <summary>
        /// 由 `SettingsMenu.ResetToDefault` 的前缀置位：表示本次 <see cref="Settings.DefaultSettings"/>
        /// 是玩家主动"恢复默认"触发的，因此要丢弃记忆值、按默认值重建。
        ///
        /// **必须由优先级高于其它模组的前缀来置位**：Harmony 的前缀一旦有人 `return false`，
        /// 原方法和其余前缀都会被跳过（详见 <see cref="ForceReset"/>）。
        /// </summary>
        internal static bool Resetting;

        /// <summary>本次"恢复默认"是否真的按默认值重建过（<see cref="Attach"/> 在 <see cref="Resetting"/> 分支里置位）。</summary>
        private static bool _resetApplied;

        /// <summary>供补丁后缀判断：这次"恢复默认"是否已经生效。</summary>
        internal static bool ResetAlreadyApplied { get { return _resetApplied; } }

        /// <summary>补丁前缀调用：记下"本次是恢复默认"，并清掉上一轮的结果标记。</summary>
        internal static void BeginReset()
        {
            _resetApplied = false;
            Resetting = true;
        }

        /// <summary>收尾：清掉两个标记（正常路径与兜底路径都走这里）。</summary>
        internal static void ClearResetMark()
        {
            Resetting = false;
            _resetApplied = false;
        }

        /// <summary>
        /// 后缀兜底：某个**优先级更高**的前缀抢先 `return false` 时（实测 `QoL Unknown` 的
        /// `ModOptionsMenu.SettingsMenuResetToDefaultPrefix`（p400）正是如此：它自己
        /// `Settings.settings = Settings.DefaultSettings()` 后 `SelectTab(0)` 再返回 false），
        /// Harmony 会跳过原方法与其余前缀，本模组的 <see cref="BeginReset"/> 根本没机会执行。
        /// 后果不是崩，而是**静默失效**：`Attach` 没看到 <see cref="Resetting"/>，
        /// 于是拿 `_lastKnown` 里的旧值做种子 —— 界面上"恢复默认"像是生效了，
        /// 本模组那 32 项却仍是玩家原来的值。
        ///
        /// 所以这里自己把整套重置补做一遍（<see cref="Settings.DefaultSettings"/> 会再次触发
        /// `Attach`，这一次 <see cref="Resetting"/> 是真的），最后把界面重画到第 0 页。
        /// </summary>
        internal static void ForceReset(SettingsMenu menu)
        {
            try
            {
                BeginReset();
                Settings.settings = Settings.DefaultSettings();
                if (!_resetApplied)
                {
                    // 极端情况：DefaultSettings 上没有本模组后缀（正常不会发生），至少别让这次点击白点
                    _lastKnown.Clear();
                    _lastKeybind = null;
                    Build();
                }
                Settings.ApplyAll();
                Settings.SaveSettings();
                if (menu != null) menu.SelectTab(Setting.SettingCategory.Video);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning("[Settings] 补做\"恢复默认\"失败（忽略）：" + e.Message);
            }
            finally
            {
                ClearResetMark();
            }
        }

        private static bool ContainsName(List<Setting> list, string name)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].name == name) return true;
            }
            return false;
        }

        /// <summary>上一次同步过的设置值（跨 DefaultSettings 重建保留玩家选择）。</summary>
        private static readonly Dictionary<string, bool> _lastKnown = new Dictionary<string, bool>();
        private static KeyCode? _lastKeybind;

        /// <summary>
        /// 把游戏设置列表里的当前值同步到运行时静态字段。
        ///
        /// 注意：游戏在 settings.json 不存在时只会保存、**不会调用 ApplyAll**，
        /// 所以这里必须能被无条件调用（Build() 结束时就调一次），
        /// 且找不到设置项时要静默退回已知值 —— 不能像 Settings.Get 那样打错误日志。
        /// </summary>
        internal static void ApplyFromSettings()
        {
            foreach (BoolDef def in Bools)
                SetBool(def.Name, _lastKnown.TryGetValue(def.Name, out bool remembered) ? remembered : def.Default);

            Setting key = FindInList(All, KeyWindow) ?? FindSetting(KeyWindow);
            WindowKey = key is SettingKeybind kb ? kb.value : (_lastKeybind ?? KeyCode.M);
            _lastKeybind = WindowKey;
        }

        /// <summary>按键名找设置项；找不到返回 null（不报错）。</summary>
        private static Setting FindSetting(string name) => FindInList(Settings.settings, name);

        /// <summary>在指定列表里按键名找设置项（All 优先于游戏列表，见 ApplyFromSettings 的说明）。</summary>
        private static Setting FindInList(List<Setting> list, string name)
        {
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].name == name) return list[i];
            }
            return null;
        }

        /// <summary>按键名把设置值写进对应的静态字段；名字未知时按传入的兜底值处理。</summary>
        private static void SetBool(string name, bool fallback)
        {
            bool value = fallback;
            Setting setting = FindInList(All, name) ?? FindSetting(name);
            if (setting is SettingBool b) value = b.value;
            _lastKnown[name] = value;

            switch (name)
            {
                case "ocs.pick.smart": PickEnabled = value; break;
                case "ocs.pick.reserve_scarce": ReserveScarce = value; break;
                case "prefer_on_floor": PreferOnFloor = value; break;
                case "prefer_not_container": PreferNotContainer = value; break;
                case "prefer_not_wearable": PreferNotWearable = value; break;
                case "prefer_on_body": PreferOnBody = value; break;
                case "prefer_high_quality": PreferHighQuality = value; break;
                case "prefer_low_value": PreferLowValue = value; break;
                case "prefer_low_condition": PreferLowCondition = value; break;
                case "notify_where_stored": NotifyWhereStored = value; break;
                case "store_in_containers_first": StoreInContainersFirst = value; break;
                case "compare_storage_type": CompareStorageType = value; break;
                case "compare_storage_reduction": CompareStorageReduction = value; break;
                case "compare_storage_full": CompareStorageFull = value; break;
                case "compare_storage_capacity": CompareStorageCapacity = value; break;
                case "compare_storage_best_condition": CompareStorageBestCondition = value; break;
                case "ocs.store.predict": PredictStorage = value; break;
                case "ocs.store.honor": HonorPrediction = value; break;
                case "liquids_can_fill_new_bottles": CanFillNewBottles = value; break;
                case "liquids_can_mix": CanMixLiquids = value; break;
                case "compare_liquid_unmixed_enabled": CompareLiquidUnmixed = value; break;
                case "compare_liquid_stacking_enabled": CompareLiquidStacking = value; break;
                case "compare_liquid_weight_enabled": CompareLiquidWeightRatio = value; break;
                case "ocs.liquid.tempbottle": AllowTempBottleTarget = value; break;
                case "ocs.liquid.skip_favorited": SkipFavoritedBottles = value; break;
                case "ocs.craft.container_contents": CarryContainerContents = value; break;
                case "ocs.craft.prefer_empty_bottle": PreferEmptyBottle = value; break;
                case "ocs.craft.rescue_liquid": RescueConsumedLiquid = value; break;
                case "ocs.ui.consumption": ShowConsumption = value; break;
                case "ocs.ui.stacking": StackItems = value; break;
                case "ocs.ui.verbose": VerboseNumbers = value; break;
                case "ocs.tip.container_totals": ContainerTotalsInTip = value; break;
                case "ocs.tip.container_weight": ContainerWeightInTip = value; break;
                case "ocs.window.enabled": WindowEnabled = value; break;
                case "ocs.window.floor": WindowIncludeFloor = value; break;
            }
        }

        /// <summary>
        /// 往游戏语言表里补设置项文案（`gameset<name>` / `gameset<name>dsc`，SettingsMenu 就是按这两条取的）。
        ///
        /// 三条纪律：
        ///   ① **已存在的键绝不覆盖** —— 汉化模组（A.Multi_Mod.Chinese.Localization）会用更高优先级
        ///      注入同名键的中文翻译，覆盖它等于把玩家的汉化干掉；
        ///   ② 用索引器写而不是 Add —— Add 撞键会抛异常；
        ///   ③ 整体 try/catch + 防重入标记 —— 这段代码跑在 Locale.LoadLanguage 的补丁里，
        ///      它自身出错绝不能让语言加载失败。
        /// </summary>
        internal static void InjectLocaleKeys()
        {
            if (_injectingLocale) return;
            _injectingLocale = true;
            try
            {
                Language lang = Locale.currentLang;
                if (lang?.other == null) return;

                foreach (BoolDef def in Bools)
                    InjectOne(lang, def.Name, def.Zh, def.En, def.ZhDsc, def.EnDsc);

                InjectOne(lang, KeyWindow,
                    "材料总览界面热键", "Materials window hotkey",
                    "打开/关闭「合成材料总览」面板。", "Open or close the crafting materials window.");
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning("[Locale] 注入设置项文案失败（忽略）：" + e.Message);
            }
            finally
            {
                _injectingLocale = false;
            }
        }

        private static bool _injectingLocale;

        private static void InjectOne(Language lang, string name, string zh, string en, string zhDsc, string enDsc)
        {
            bool zhUi = Loc.IsChinese;
            string titleKey = "gameset" + name;
            string descKey = titleKey + "dsc";

            // 标题：留出位置颜色标记，和游戏原生设置项风格一致（有 <color> 支持）
            if (!lang.other.ContainsKey(titleKey))
                lang.other[titleKey] = zhUi ? "<color=purple>合成</color>：" + zh : "<color=purple>Crafting</color>: " + en;
            if (!lang.other.ContainsKey(descKey))
                lang.other[descKey] = zhUi ? zhDsc : enDsc;
        }
    }
}
