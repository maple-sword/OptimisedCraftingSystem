using UnityEngine;

namespace OptimisedCraftingSystem
{
    /// <summary>
    /// 全局热键与按键状态（<see cref="Plugin.Update"/> 每帧调用 <see cref="Tick"/>）。
    ///
    /// 与原模组的差异：原模组没有任何界面，本模组有一个独立面板，因此需要热键；
    /// 热键本身注册进游戏自己的按键设置（SettingCategory.Input），玩家可以在
    /// 设置界面的「输入」页里改，并且自动获得 settings.json 持久化。
    /// </summary>
    internal static class Hotkeys
    {
        internal static void Tick()
        {
            try
            {
                if (!ModSettings.WindowEnabled)
                {
                    if (UI.MaterialWindow.Visible)
                    {
                        UI.MaterialWindow.Visible = false;
                        Plugin.Evidence("window:disabled", "材料总览窗口：设置里已关闭，不显示。");
                    }
                    return;
                }

                // 面板没打开时也允许开关；打开后 Esc 关闭（不拦截游戏自身的按键处理）
                if (ModSettings.WindowKey != KeyCode.None && Input.GetKeyDown(ModSettings.WindowKey))
                {
                    UI.MaterialWindow.Toggle();
                    // 证据日志：开/关各记一行，让"热键真的能开窗口"可被日志证实
                    Plugin.Evidence(
                        "window:" + UI.MaterialWindow.Visible,
                        UI.MaterialWindow.Visible
                            ? "材料总览窗口：已打开（热键 " + ModSettings.WindowKey + "）。"
                            : "材料总览窗口：已关闭（热键或 Esc）。");
                }

                if (UI.MaterialWindow.Visible && Input.GetKeyDown(KeyCode.Escape))
                {
                    UI.MaterialWindow.Visible = false;
                    Plugin.Evidence("window:False", "材料总览窗口：已关闭（热键或 Esc）。");
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning("[Hotkeys] " + e.Message);
            }
        }

        /// <summary>
        /// 玩家是否按住了游戏里「展开物品描述」的键（默认左 Shift）。
        /// 刻意复用游戏自己的绑定（PlayerCamera 判断展开也是用 KeyBinds.GetBind("expanddesc")），
        /// 这样玩家改键后本模组的 Shift 内容物提示会跟着走。
        /// </summary>
        internal static bool ExpandHeld()
        {
            try
            {
                KeyCode key = KeyBinds.GetBind("expanddesc");
                if (key != KeyCode.None && Input.GetKey(key)) return true;
            }
            catch { }
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }
    }
}
