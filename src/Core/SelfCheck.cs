using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace OptimisedCraftingSystem
{
    /// <summary>
    /// 启动后一次性自检：把"补丁是否真的挂上了、挂在谁身上、游戏数据是否就绪、界面字体是否可用"
    /// 写进 BepInEx 日志。
    ///
    /// 为什么需要它：本模组改的是合成与存放的关键路径，如果补丁因为签名不符 / 与其它模组冲突而没生效，
    /// 玩家只会看到"功能没反应"，从日志里什么都看不出来。自检把结论直接打出来，
    /// 也让"改进前后对比"有可引用的日志证据。
    /// </summary>
    internal static class SelfCheck
    {
        private static bool _done;
        private static bool _waitedOnce;
        private static float _firstTick;

        /// <summary>
        /// 每个游戏进程只跑一次，但要等游戏真的把配方表建好（那意味着玩家已经进了关卡）。
        /// 主菜单里 `Recipes.recipes` 是空的，此时打出来的"配方=0"没有参考价值，
        /// 所以先等一分钟提示一句，之后每次 `Tick` 都盯着，一进关卡就自动出报告。
        /// </summary>
        internal static void Tick()
        {
            if (_done) return;
            try
            {
                if (_firstTick <= 0f) _firstTick = Time.realtimeSinceStartup;

                List<Recipe> recipes = null;
                try { recipes = Recipes.recipes; } catch { }
                if (recipes == null || recipes.Count == 0)
                {
                    if (!_waitedOnce && Time.realtimeSinceStartup - _firstTick >= 60f)
                    {
                        _waitedOnce = true;
                        Plugin.Log.LogInfo("[自检] 配方表还是空的（多半还在主菜单），进关卡后会自动再查一次。");
                    }
                    return;
                }

                _done = true;
                StringBuilder sb = new StringBuilder();
                sb.Append("[自检] 语言=").Append(Loc.IsChinese ? "中文" : "English");
                sb.Append(" 设置项=").Append(ModSettings.All != null ? ModSettings.All.Count : 0).Append(" 项");
                sb.Append(" 热键=").Append(ModSettings.WindowKey);
                sb.Append(" 字体=").Append(UI.MaterialWindow.FontName);
                Plugin.Log.LogInfo(sb.ToString());

                sb.Length = 0;
                sb.Append("[自检] 配方=").Append(recipes != null ? recipes.Count : 0).Append(" 个");
                try { sb.Append(" 物品类型=").Append(Item.GlobalItems != null ? Item.GlobalItems.Count : 0).Append(" 种"); } catch { }
                try
                {
                    PlayerCamera cam = PlayerCamera.main;
                    sb.Append(" 玩家=").Append(cam != null && cam.body != null ? "在场" : "未进场");
                }
                catch { }
                Plugin.Log.LogInfo(sb.ToString());

                CheckPatch(typeof(Recipe), "GetItemsForRecipe", true);
                CheckPatch(typeof(Recipe), "GetItemsForRecipeThorough", true);
                CheckPatch(typeof(Recipe), "TryMake", true);
                // 注意：本模组**故意不补** `RecipeResult.SpawnResult`（原模组是整体替换它，
                // 因此会顶掉 CUCoreLib 的自定义产物）。这里只报告谁挂着它，供对比参考。
                CheckPatch(typeof(RecipeResult), "SpawnResult", false);
                CheckPatch(typeof(Body), "AutoPickUpItem", false);
                CheckPatch(typeof(WaterContainerItem), "AddLiquid", false);
                CheckPatch(typeof(PlayerCamera), "IngredientTextForRecipe", false);
                CheckPatch(typeof(PlayerCamera), "ItemHoverDescription", false);
                CheckPatch(typeof(PlayerCamera), "OpenCraftScreen", false);
                CheckPatch(typeof(Settings), "DefaultSettings", false);
                CheckPatch(typeof(SettingsMenu), "ResetToDefault", false);
                // 这一条挂的是我们自己的兼容垫片（见 `Patches\PatchCompat.cs`）：Crafting Tweaks 的
                // 库存计数会遍历身上物品调它，原版实现遇到 null 物品/未注册 id 会抛 NRE 并打断整条调用链。
                CheckPatch(typeof(RecipeItem), "DoesUseItemType", false);
                // 这一条是本模组 07:28:16 版新加的「消耗前抢救」（`Patches\PatchCrafting.cs` 的
                // `PatchRecipeItemUseItem`）：瓶子/容器被整件消耗前，先把瓶内液体装进临时瓶、
                // 把容器内容物摘出来冻住，等新产物出现再装进去。之所以归为「提示级」，
                // 是因为它失效只是退回原版行为（液体随瓶子消失），不会让整个模组失灵。
                CheckPatch(typeof(RecipeItem), "UseItem", false);
                CheckPatch(typeof(Locale), "LoadLanguage", false);
            }
            catch (Exception e)
            {
                _done = true;
                Plugin.Log?.LogWarning("[自检] 失败（不影响运行）：" + e);
            }
        }

        /// <summary>打印某个方法上都有谁挂了补丁，用来一眼看出与其它模组的共存情况。</summary>
        private static void CheckPatch(Type type, string method, bool critical)
        {
            try
            {
                MethodBase mi = AccessTools.Method(type, method);
                if (mi == null)
                {
                    Plugin.Log.LogWarning("[自检] 找不到方法 " + type.Name + "." + method + "（游戏版本可能变了）");
                    return;
                }

                Patches info = Harmony.GetPatchInfo(mi);
                string mine = Signs(info, Plugin.Guid);
                string others = Signs(info, null);
                string line = "[自检] " + type.Name + "." + method + " ← 本模组：" + (mine.Length > 0 ? mine : "无") +
                              (others.Length > 0 ? "；其它模组：" + others : string.Empty);
                if (mine.Length == 0 && critical) Plugin.Log.LogError(line + "  ← 关键补丁未生效！");
                else Plugin.Log.LogInfo(line);
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning("[自检] 检查 " + type.Name + "." + method + " 补丁状态失败：" + e.Message);
            }
        }

        /// <summary>owner 为 null 时统计除本模组以外的所有补丁。</summary>
        private static string Signs(Patches info, string owner)
        {
            if (info == null) return string.Empty;
            List<string> parts = new List<string>();
            Collect(info.Prefixes, owner, "prefix", parts);
            Collect(info.Postfixes, owner, "postfix", parts);
            Collect(info.Transpilers, owner, "transpiler", parts);
            return string.Join(", ", parts.ToArray());
        }

        private static void Collect(IEnumerable<Patch> patches, string owner, string kind, List<string> parts)
        {
            if (patches == null) return;
            foreach (Patch p in patches)
            {
                if (p == null) continue;
                bool isMine = p.owner == Plugin.Guid;
                if (owner == Plugin.Guid && !isMine) continue;
                if (owner == null && isMine) continue;
                parts.Add(p.owner + "/" + kind + "(p" + p.priority + ")");
            }
        }
    }
}
