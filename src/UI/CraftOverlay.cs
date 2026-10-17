using System;
using UnityEngine;

namespace OptimisedCraftingSystem.UI
{
    /// <summary>
    /// C 键合成界面打开时贴在**屏幕右侧空白处**的小覆盖层：显示本次合成每槽会消耗什么、产物会存到哪里，
    /// 并可以点材料名 / 点去向文字展开候选表来换材料、换容器、换临时瓶
    /// （"合成 GUI 内追加预计容器与更改存放容器"）。
    ///
    /// 位置：从右边贴到 C 键面板右侧那条空白里——原版合成面板与它的预览图标都在中左部，
    /// 早先贴在左下角会把面板内容压住（看不清也点不到），所以改成右侧竖排并按内容动态定高。
    /// 玩家拖过标题行之后改用**手动位置**（本局有效），双击标题行回到自动位置。
    ///
    /// 只负责定位、拖动与显隐判断，内容全部由 <see cref="MaterialWindow.DrawCompact(Rect)"/> 画，
    /// 这样覆盖层与独立窗口不会出现两套不一致的逻辑。
    /// </summary>
    internal static class CraftOverlay
    {
        private static bool _loggedError;

        // ——— 拖动状态 ———
        // 覆盖层默认自动贴右侧；一旦玩家拖过就记住手动位置（本局有效，双击标题行复位）。
        private static bool _moved;            // true = 手动位置优先
        private static Vector2 _pos;           // 手动位置的左上角（已在 GUI.matrix 缩放后的坐标系里）
        private static bool _dragging;
        private static Vector2 _grab;          // 按下时鼠标相对左上角的偏移
        private static Rect _lastRect;         // 上一帧画出来的矩形（命中判定只能用它）

        /// <summary>只有标题行能拖：点候选表、点按钮时不会误拖（行高与 `MaterialWindow.DrawCompact` 的标题行相当）。</summary>
        private const float DragBarHeight = 26f;

        /// <summary>把覆盖层放回自动位置（双击标题行触发）。</summary>
        internal static void ResetPosition()
        {
            _moved = false;
            _dragging = false;
        }

        internal static void Draw()
        {
            if (!ModSettings.ShowConsumption && !ModSettings.PredictStorage)
            {
                _dragging = false;
                return;
            }

            Matrix4x4 prev = GUI.matrix;
            try
            {
                PlayerCamera cam = PlayerCamera.main;
                if (cam == null || cam.craftingPanel == null || !cam.craftingPanel.activeSelf)
                {
                    // 面板关掉时也要收掉拖动状态：MouseUp 事件落在面板外时我们收不到，
                    // 留着 _dragging 会让下次打开面板后鼠标一动就把覆盖层拖走。
                    _dragging = false;
                    return;
                }

                float s = Mathf.Clamp(Screen.height / 1080f, 0.85f, 1.8f);
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));

                float screenW = Screen.width / s;
                float screenH = Screen.height / s;
                float width = MaterialWindow.CompactWidth;      // 660
                float height = MaterialWindow.CompactHeight();  // 随展开的候选表变化

                HandleDrag();

                float x;
                float y;
                if (_moved)
                {
                    // 手动摆放：夹回屏幕内，拖到边缘也不会丢
                    x = Mathf.Clamp(_pos.x, 0f, Mathf.Max(0f, screenW - width));
                    y = Mathf.Clamp(_pos.y, 0f, Mathf.Max(0f, screenH - height));
                }
                else
                {
                    // 贴右侧；竖直方向默认居中，但不越过右上角「已钉配方」HUD（约 140 高）
                    x = Mathf.Max(16f, screenW - width - 20f);
                    y = Mathf.Max(140f, (screenH - height) * 0.5f);

                    // M 窗口也开着时（它固定在右上角），把覆盖层压到窗口下沿，别叠在一起
                    if (MaterialWindow.Visible)
                    {
                        Rect win = MaterialWindow.WindowRect;
                        if (win.height > 0f) y = Mathf.Max(y, win.yMax + 12f);
                    }

                    y = Mathf.Min(y, Mathf.Max(16f, screenH - height - 16f));
                }

                Rect area = new Rect(x, y, width, height);
                _lastRect = area;
                MaterialWindow.DrawCompact(area);
            }
            catch (Exception e)
            {
                if (_loggedError) return;
                _loggedError = true;
                Plugin.Log?.LogWarning("[UI] 合成覆盖层绘制失败（已停止重复报告）：" + e);
            }
            finally
            {
                GUI.matrix = prev;
            }
        }

        /// <summary>
        /// 拖标题行移动覆盖层；双击标题行复位到自动位置。
        ///
        /// 为什么不用 `GUI.DragWindow`：它只在 `GUI.Window` 里有效，而覆盖层是 `GUILayout.BeginArea` 画的
        /// （`GUI.Window` 有标题栏与自己的内边距，塞不下这个紧凑排版）。命中判定用**上一帧**的矩形 ——
        /// 本帧的矩形正是由这次拖动决定的，用本帧会自相矛盾。
        /// </summary>
        private static void HandleDrag()
        {
            Event e = Event.current;
            if (e == null) return;

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button != 0 || !InDragBar(e.mousePosition)) return;

                    if (e.clickCount >= 2)
                    {
                        ResetPosition();
                        e.Use();
                        return;
                    }

                    _dragging = true;
                    _grab = e.mousePosition - new Vector2(_lastRect.x, _lastRect.y);
                    e.Use();
                    break;

                case EventType.MouseDrag:
                    if (!_dragging) return;
                    _pos = e.mousePosition - _grab;
                    _moved = true;
                    e.Use();
                    break;

                case EventType.MouseUp:
                    _dragging = false;
                    break;
            }
        }

        private static bool InDragBar(Vector2 mouse)
        {
            if (_lastRect.width <= 0f || _lastRect.height <= 0f) return false;
            Rect bar = new Rect(_lastRect.x, _lastRect.y, _lastRect.width, DragBarHeight);
            return bar.Contains(mouse);
        }
    }
}
