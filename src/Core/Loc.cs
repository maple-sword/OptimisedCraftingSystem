using System.Collections.Generic;
using UnityEngine;

namespace OptimisedCraftingSystem
{
    /// <summary>
    /// 模组自带的中英文案表。
    ///
    /// 为什么不直接用游戏的语言表（Locale / gameset* 键）？
    ///   1. 游戏内置语言表只有"标题 + 说明"两类键，装不下本模组 GUI 里的大量动态文案；
    ///   2. 汉化模组（A.Multi_Mod.Chinese.Localization）会覆盖它认识的 gameset* 键，
    ///      而它不认识本模组新增的键 —— 我们不能把界面文案的可靠性寄托在别人身上。
    /// 因此：**游戏设置界面里的设置项文案**走 Locale.other（见 ModSettings），
    ///       **本模组自建 GUI / 物品提示里的文案**走这张内置表。
    ///
    /// 语言判定用「内容证据优先」策略（照搬本工作区 WearSlotInfo 的成熟做法）：
    /// 先看语言包里几个必定存在的键的内容是否含汉字，有证据就按内容定论；
    /// 没证据时才退回系统语言 / 语言包文件名。每 2 秒允许重判一次，防止游戏启动早期
    /// `Locale.currentLang` 还是 null 时把结论锁死。
    /// </summary>
    internal static class Loc
    {
        /// <summary>表驱动文案：key → (中文, English)。key 统一小写，前缀 bcl 表示本模组专用。</summary>
        private static readonly Dictionary<string, (string zh, string en)> Table =
            new Dictionary<string, (string zh, string en)>
            {
                // ——— 窗口与通用 ———
                { "ocs.win.title",              ("合成材料总览", "Crafting Materials") },
                { "ocs.win.tab.materials",      ("可用材料", "Materials") },
                { "ocs.win.tab.recipes",        ("配方需求", "Recipes") },
                { "ocs.win.close",              ("关闭", "Close") },
                { "ocs.win.hint",               ("拖动标题栏移动窗口 · 热键开关", "Drag the title bar · hotkey toggles") },
                { "ocs.win.empty",              ("没有找到可用材料", "No usable materials found") },
                { "ocs.win.col.item",           ("材料", "Item") },
                { "ocs.win.col.where",          ("位置", "Where") },
                { "ocs.win.col.qualities",      ("提供的性质", "Provides") },
                { "ocs.win.col.uses",           ("可用次数", "Uses") },
                { "ocs.win.col.cost",           ("每次消耗", "Per use") },
                { "ocs.win.col.matched",        ("匹配配方", "Recipes") },
                { "ocs.win.col.needed",         ("需求", "Needed") },
                { "ocs.win.col.recipes",        ("使用该性质的配方", "Used by") },
                { "ocs.win.floor",              ("手边地面", "On the floor") },
                { "ocs.win.slot",               ("背包槽 {0}", "Slot {0}") },
                { "ocs.win.slot.hand",          ("手中槽 {0}", "Hand {0}") },
                { "ocs.win.worn",               ("已穿戴", "Worn") },
                { "ocs.win.inside",             ("容器 {0} 内", "In {0}") },
                { "ocs.win.prop",               ("价值", "Value") },
                { "ocs.win.weight",             ("重量", "Weight") },
                { "ocs.win.condition",          ("耐久", "Condition") },

                // ——— 合成界面覆盖层 ———
                { "ocs.craft.needs",            ("需要", "needs") },
                { "ocs.craft.consumes",         ("消耗", "Consumes") },
                { "ocs.craft.destroyed",        ("整件消耗", "Destroyed") },
                { "ocs.craft.uses",             ("（还能用 {0:0.#} 次）", " ({0:0.#} uses left)") },
                { "ocs.craft.store",            ("将存放于", "Goes to") },
                { "ocs.craft.change",           ("点击更换", "Click to change") },
                { "ocs.overlay.hint",           ("拖动标题栏可移动 · 双击复位", "Drag the title bar to move · double-click resets") },
                { "ocs.craft.pick.title",       ("为该槽位选择材料", "Choose material for this slot") },
                { "ocs.craft.store.title",      ("为该产物选择存放目标", "Choose storage for the result") },
                { "ocs.craft.auto",             ("自动（推荐）", "Auto (recommended)") },
                { "ocs.craft.orig",             ("原版逻辑", "Vanilla") },
                { "ocs.craft.contents.dropped", ("内容物装不下，已掉在脚边", "Contents did not fit — dropped at your feet") },

                // ——— 消耗量单位 ———
                { "ocs.unit.ml",                ("{0:0.#} mL", "{0:0.#} mL") },
                { "ocs.unit.cond",              ("耐久 -{0:0.#}%", "-{0:0.#}% condition") },
                { "ocs.unit.count",             ("{0:0.##} 件", "{0:0.##} items") },

                // ——— 存放地点 ———
                { "ocs.store.floor",            ("地面", "the floor") },
                { "ocs.store.hand",             ("手中槽 {0}", "hand slot {0}") },
                { "ocs.store.slot",             ("背包槽 {0}", "slot {0}") },
                { "ocs.store.wear",             ("装备到 {0}", "equip on {0}") },
                { "ocs.store.wear.replace",     ("装备到 {0}（替换 {1}）", "equip on {0} (replacing {1})") },
                { "ocs.store.wear.blocked",     ("无法装备（{0} 已被占用）", "cannot equip ({0} occupied)") },
                { "ocs.store.container",        ("{0}（剩余 {1:0.##} kg）", "{0} ({1:0.##} kg free)") },
                { "ocs.store.bottle",           ("{0}（余量 {1:0.#} mL）", "{0} ({1:0.#} mL free)") },
                { "ocs.store.bottle.contents",  ("{0}（{1} · 余量 {2:0.#} mL）", "{0} ({1} · {2:0.#} mL free)") },
                { "ocs.store.contents.empty",   ("空", "empty") },
                { "ocs.store.bottle.full",      ("已装满", "full") },
                { "ocs.store.bottle.newempty",  ("空瓶（设置里没允许装进空瓶）", "empty bottle (\"fill empty bottles\" is off)") },
                { "ocs.store.bottle.mix",       ("含其它液体（设置里没允许混装）", "holds another liquid (mixing is off)") },
                { "ocs.store.bottle.favorited", ("已收藏（本模组不往里倒液体）", "favourited (this mod will not pour into it)") },
                { "ocs.store.tempbottle",       ("现场做一个临时瓶", "makeshift bottle on the spot") },
                { "ocs.store.tempbottle.warn",  ("现场做一个临时瓶（5 分钟后连同液体销毁）", "makeshift bottle (destroyed with its liquid in 5 min)") },
                { "ocs.store.spread",           ("{0} 等 {1} 个容器分摊", "split over {1} containers (first: {0})") },
                { "ocs.store.lost",             ("没有能装液体的容器，液体将流失", "no liquid container - the liquid is lost") },
                { "ocs.store.none",             ("无可存放位置，将掉在地上", "nowhere to put it - drops on the floor") },
                { "ocs.stored.prefix",          ("已存放：", "Stored: ") },

                // ——— 物品提示（Shift 展开） ———
                { "ocs.tip.contents",           ("内容物：", "Contents: ") },
                { "ocs.tip.contents.weight",    ("内容物总重：{0:0.##} kg", "Contents weight: {0:0.##} kg") },
                { "ocs.tip.contents.empty",     ("内容物：空", "Contents: empty") },
                { "ocs.tip.free.space",         ("剩余空间：{0:0.##} kg", "Free space: {0:0.##} kg") },
            };

        private static bool _isChinese;
        private static float _nextCheck = -99f;
        private static bool _logged;

        /// <summary>当前界面应当使用中文吗（带 2 秒节流的缓存判定）。</summary>
        internal static bool IsChinese
        {
            get
            {
                float now = Time.realtimeSinceStartup;
                if (now >= _nextCheck)
                {
                    _nextCheck = now + 2f;
                    bool value = DetectChinese();
                    if (value != _isChinese || !_logged)
                    {
                        _isChinese = value;
                        if (!_logged)
                        {
                            _logged = true;
                            Plugin.Log.LogInfo("[Loc] 界面语言判定为 " + (value ? "中文" : "English") + "。");
                        }
                    }
                }
                return _isChinese;
            }
        }

        /// <summary>取文案；找不到键时回退到 key 本身（便于开发期发现漏配）。</summary>
        internal static string T(string key)
        {
            if (Table.TryGetValue(key, out var pair)) return IsChinese ? pair.zh : pair.en;
            return key;
        }

        /// <summary>取带一个参数的文案（用 string.Format，占位符 {0}）。</summary>
        internal static string T(string key, object a0)
        {
            return string.Format(T(key), a0);
        }

        /// <summary>取带两个参数的文案。</summary>
        internal static string T(string key, object a0, object a1)
        {
            return string.Format(T(key), a0, a1);
        }

        /// <summary>取带三个参数的文案（例：瓶子名 + 内容物 + 余量）。</summary>
        internal static string T(string key, object a0, object a1, object a2)
        {
            return string.Format(T(key), a0, a1, a2);
        }

        /// <summary>
        /// 语言判定：内容证据优先。
        ///   1. 语言包里几个必定存在的键，只要有一个含汉字 → 中文；
        ///   2. 拿到了内容且都不含汉字 → 非中文（直接定论，避免被文件名骗）；
        ///   3. 没有内容证据 → 看系统语言是否中文系；
        ///   4. 最后才看语言包文件名是否含 zh/cn/chinese/中文/汉化。
        /// 任何异常都吞掉并返回上一次的结论：语言表结构随版本可能变化，不该因此让模组崩掉。
        /// </summary>
        private static bool DetectChinese()
        {
            try
            {
                Language lang = Locale.currentLang;
                if (lang != null)
                {
                    string probe = Sample(lang, "hunger4") ?? Sample(lang, "steak") ?? Sample(lang, "savecontinue");
                    if (!string.IsNullOrEmpty(probe))
                    {
                        // 有内容证据：按内容定论，不再参考文件名
                        return HasCjk(probe);
                    }
                }

                switch (Application.systemLanguage)
                {
                    case SystemLanguage.Chinese:
                    case SystemLanguage.ChineseSimplified:
                    case SystemLanguage.ChineseTraditional:
                        return true;
                }

                string name = Locale.currentLangName;
                if (!string.IsNullOrEmpty(name))
                {
                    string lower = name.ToLowerInvariant();
                    if (lower.Contains("zh") || lower.Contains("cn") || lower.Contains("chinese")
                        || name.Contains("中文") || name.Contains("汉化"))
                        return true;
                }
            }
            catch
            {
                // 忽略：退回上一次结论
            }
            return _isChinese;
        }

        /// <summary>依次从 moodles / main / other 三张表里取一个用于探测语言的样本。</summary>
        private static string Sample(Language lang, string key)
        {
            if (lang.moodles != null && lang.moodles.TryGetValue(key, out var a) && !string.IsNullOrEmpty(a)) return a;
            if (lang.main != null && lang.main.TryGetValue(key, out var b) && !string.IsNullOrEmpty(b)) return b;
            if (lang.other != null && lang.other.TryGetValue(key, out var c) && !string.IsNullOrEmpty(c)) return c;
            return null;
        }

        /// <summary>是否含中日韩统一表意文字（用来判断语言包内容是中文）。</summary>
        private static bool HasCjk(string s)
        {
            foreach (char c in s)
            {
                if ((c >= '\u4e00' && c <= '\u9fff') || (c >= '\u3400' && c <= '\u4dbf') || (c >= '\uf900' && c <= '\ufaff'))
                    return true;
            }
            return false;
        }

        /// <summary>语言表被重新加载后清掉节流缓存，让下一次访问立刻重判。</summary>
        internal static void InvalidateLanguage()
        {
            _nextCheck = -99f;
        }
    }
}
