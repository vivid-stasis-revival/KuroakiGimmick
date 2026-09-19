using System.Runtime.InteropServices;
using KuroakiGimmick.Native;
namespace KuroakiGimmick.UI;

/// <summary>
/// 编辑器输入分派。这里的判断顺序本身就是契约：辅助窗口 → 退出 → workflow → 参考手册 → 模态 →
/// 快速帮助 → 编辑器快捷键，上层覆盖层一律吞掉下层的键盘与滚轮，只放行鼠标事件给覆盖层自己的按钮。
/// 事件分支里只做状态切换，不做慢速文件加载。
/// </summary>
public sealed partial class Viewer
{
    /// <summary>返回 true 表示该事件专属于编辑器 / 真实窗口预览，已被消费，不再往 viewer 输入层传。</summary>
    bool HandleEditorInput(Sdl.Event e)
    {
        // 辅助窗口的鼠标坐标绝不能进入编辑器宿主窗口的命中测试，否则会按别的窗口坐标去点工作区。
        bool windowEvent = e.Type is >= 0x202 and <= 0x21B or >= 0x300 and <= 0x303 or >= 0x400 and <= 0x403 or >= 0x1000 and <= 0x1004;
        if (windowEvent && e.WindowID != 0 && e.WindowID != Sdl.SDL_GetWindowID(host.Window))
        {
            if (nativeWindows?.Owns(e.WindowID) == true && (e.Type == Sdl.WindowCloseRequested || e.Type == 0x300 && e.Scan == 41))
            { nativeWindows.Dispose(); nativeWindows = null; message = "Live windows closed."; }
            return true;
        }
        if (e.Type == 0x100 || e.Type == Sdl.WindowCloseRequested)
        {
            if (ChartExportBusy) { chartExportCancellation?.Cancel(); message = "Cancelling export; close again when it has stopped."; return true; }
            if (referenceOpen) CloseReference();
            if (!GuardUnsaved(() => quit = true)) quit = true;
            return true;
        }
        if (HandleWorkflowInput(e)) return true;
        if (ReferenceVisible) return HandleReferenceInput(e);
        if (UiClosingOverlay)
        {
            if (e.Type is 0x400 or 0x401 or 0x402) { mouseX = e.X; mouseY = e.Y; }
            held = click = false;
            return true;
        }
        if (pendingDiscard != null)
        {
            if (e.Type == 0x300 && e.Scan == 41) pendingDiscard = null;
            return e.Type is not (0x400 or 0x401 or 0x402);
        }
        if (modalActive)
        {
            if (e.Type == 0x303)
            {
                string value = Marshal.PtrToStringUTF8(e.TextData) ?? "";
                if (modalSelectAll) { modalValue = ""; modalSelectAll = false; }
                if (modalValue.Length + value.Length <= 65536) modalValue += value;
                return true;
            }
            if (e.Type == 0x300)
            {
                bool ctrl = (e.Modifiers & 0x0CC0) != 0;
                if (e.Scan == 41) CloseValue();
                else if (e.Scan is 40 or 88) AcceptValue();
                else if (ctrl && e.Scan == 4) modalSelectAll = true;
                else if (ctrl && e.Scan == 25)
                {
                    nint ptr = Sdl.SDL_GetClipboardText();
                    try
                    {
                        string value = Marshal.PtrToStringUTF8(ptr) ?? "";
                        if (value.Length > 65536) { modalError = "Clipboard text exceeds 64 KiB."; return true; }
                        modalValue = modalSelectAll ? value : (modalValue + value);
                        if (modalValue.Length > 65536) modalValue = modalValue[..65536]; modalSelectAll = false;
                    }
                    finally { Sdl.SDL_free(ptr); }
                }
                else if (e.Scan is 42 or 76)
                {
                    if (modalSelectAll) modalValue = "";
                    else if (modalValue.Length > 0)
                    {
                        int count = modalValue.Length > 1 && char.IsLowSurrogate(modalValue[^1]) && char.IsHighSurrogate(modalValue[^2]) ? 2 : 1;
                        modalValue = modalValue[..^count];
                    }
                    modalSelectAll = false;
                }
                return true;
            }
            return e.Type is not (0x400 or 0x401 or 0x402);
        }
        // 右键菜单排在就地编辑之前：它是阻塞覆盖层，开着的时候键盘全归它，点在菜单外只负责关掉菜单。
        if (menuOpen && HandleClipMenuInput(e)) return true;
        // inspector 的就地编辑持有焦点时独占键盘，否则打字会顺着下去触发编辑器快捷键。
        // 鼠标点在输入框以外时它只提交并返回 false，这一次点击继续往下走，照常落到用户真正瞄准的控件上。
        if (InlineActive && HandleInlineInput(e)) return true;
        if (e.Type == 0x300 && e.Repeat == 0 && e.Scan == 58 && !Busy && !settings)
        { OpenContextReference(); return true; }
        // 快速帮助在显示期间是阻塞覆盖层。导航键和滚轮不能拖动播放头或编辑下面的时间轴；
        // 鼠标事件仍然放行，这样帮助自己的按钮还能点。
        if (help)
        {
            if (e.Type == 0x300 && e.Repeat == 0 && (e.Scan is 41 or 11)) help = false;
            return e.Type is not (0x400 or 0x401 or 0x402);
        }
        if (e.Type == 0x301)
        {
            // SDL scancode 26 = W。虽然普通的按键抬起事件会继续下传给 viewer 输入层，
            // 但 W 的抬起必须在这里被观察到，否则松手后帮助卡片会一直挂着。
            if (e.Scan == 26)
            {
                trackHelpWHeld = false; trackHelpWDownAt = 0;
                return editorMode;
            }
            return false;
        }
        if (e.Type == 0x401) mouseClicks = e.Clicks;
        if (e.Type == 0x402 && e.Button == 1)
        {
            mouseX = e.X; mouseY = e.Y;
            if (editorMode) { FinishDrag(); FinishMarquee(); }
            editorScrub = false;
        }
        // 右键：按住拖 = 平移时间轴（横向按拍、纵向按轨道行），原地松手 = 在指针处弹菜单。
        // 右键事件不会更新 mouseX / mouseY 的公共路径，所以这里必须自己写进去。
        if (editorMode && e.Type is 0x401 or 0x402 && e.Button == 3)
        {
            mouseX = e.X; mouseY = e.Y;
            if (e.Type == 0x401) { if (!Busy && editorTimelineRect.Contains(e.X, e.Y)) BeginPan(e.X, e.Y); }
            else FinishPan(e.X, e.Y);
            return true;
        }
        if (e.Type == 0x400 && panActive) { mouseX = e.X; mouseY = e.Y; UpdatePan(e.X, e.Y); return true; }
        if (e.Type == 0x300 && e.Repeat == 0 && e.Scan == 41 && nativeWindows != null)
        { nativeWindows.Dispose(); nativeWindows = null; return true; }
        if (e.Type == 0x300 && e.Repeat == 0 && e.Scan == 43 && !Busy && !settings)
        { ToggleWorkspace(); return true; }
        // 即使已经切回 Viewer 也要保存编辑文档本身，而不只是它旧的工程引用。
        if (e.Type == 0x300 && e.Repeat == 0 && (e.Modifiers & 0x0CC0) != 0 && e.Scan == 22 && editor != null)
        { SaveEditor((e.Modifiers & 3) != 0); return true; }
        if (!editorMode || settings) return false;
        if (help)
        {
            if (e.Type == 0x300 && e.Scan is 41 or 11) help = false;
            return e.Type is not (0x400 or 0x401 or 0x402);
        }
        if (HandleImageObjectKeys(e)) return true;
        if (e.Type == 0x403 && !Busy)
        {
            // 按住 W 时滚轮属于帮助卡片而不是下面的时间轴。220 ms 的门槛避免"轻按一下 W"就抢走滚轮。
            if (trackHelpWHeld && TrackHelpOwnsPointer() && Environment.TickCount64 - trackHelpWDownAt >= 220)
            {
                helpCardScroll = Math.Clamp(helpCardScroll - e.WheelY * 42, 0, helpCardScrollMax);
                return true;
            }
            ushort mods = Sdl.SDL_GetModState();
            if (editorInspectorRect.Contains(mouseX, mouseY)) inspectorScroll = Math.Max(0, inspectorScroll - (int)Math.Round(e.WheelY));
            else if (editorTimelineRect.Contains(mouseX, mouseY))
            {
                // 时间轴上的滚轮：command 缩放（以指针处为锚），Shift 横向平移，无修饰键纵向滚动轨道列表。
                // beatStart 允许到 -64 拍，好让 0 拍之前也留出可视余量。
                if ((mods & 0x0CC0) != 0) ZoomTimeline(Math.Pow(1.15, e.WheelY), mouseX);
                else if ((mods & 3) != 0) ScrollTimeline(beatStart - e.WheelY * 70 / pixelsPerBeat);
                else trackScroll = Math.Max(0, trackScroll - (int)Math.Round(e.WheelY));
                if (e.WheelX != 0) ScrollTimeline(beatStart + e.WheelX * 70 / pixelsPerBeat);
            }
            return true;
        }
        if (e.Type != 0x300 || e.Repeat != 0 || Busy) return false;
        // 0x0CC0 = 左右 Ctrl / GUI（command），3 = 左右 Shift，0x0300 = 左右 Alt。
        bool command = (e.Modifiers & 0x0CC0) != 0, shift = (e.Modifiers & 3) != 0;
        // scancode 8 = E：单独按下标记一个目标点，把插入点钉在这里；Shift+E 删除当前目标标记。
        // 拖拽进行中不接受，否则会在一次拖拽中途改掉插入点。
        if (!command && (e.Modifiers & 0x0300) == 0 && e.Scan == 8 && editDrag == null)
        {
            if (shift && activeMarker is Guid marker) { editor?.RemoveMarker(marker); activeMarker = null; }
            else if (!shift) MarkTimestamp();
            return true;
        }
        if (command && e.Scan == 8) { OpenChartExport(); return true; }
        if (!command && e.Scan == 26)
        {
            // 长按 W 且指针停在原始轨道名上时，展开详细帮助卡片。
            trackHelpWHeld = true; trackHelpWDownAt = Environment.TickCount64;
            return true;
        }
        // scancode 41 = Esc：拖拽中按下只取消本次拖拽，不退出编辑器。
        if (e.Scan == 41 && editDrag != null) { editDrag = null; return true; }
        // 29 = Z，28 = Y：撤销 / 重做后作废布局缓存，否则时间轴仍按旧结构绘制。
        if (command && e.Scan == 29)
        { if (shift) editor?.Redo(); else editor?.Undo(); layoutRevision = -1; return true; }
        if (command && e.Scan == 28) { editor?.Redo(); layoutRevision = -1; return true; }
        if (!command && e.Scan is 42 or 76) { DeleteSelection(); return true; }
        if (!command && e.Scan == 7) { DuplicateSelection(); return true; }
        // 4 = A，5 = B，15 = L：以当前播放位置（秒）换算成拍设置循环试听区间；出点至少比入点晚 1/8 拍。
        if (!command && e.Scan == 4) { loopIn = Current.Timeline.Bpm.Beat(transport.Position); return true; }
        if (!command && e.Scan == 5) { loopOut = Math.Max(loopIn + .125, Current.Timeline.Bpm.Beat(transport.Position)); return true; }
        if (!command && e.Scan == 15) { loopEnabled = !loopEnabled; return true; }
        if (!command && e.Scan == 40) { AddMod(); return true; }
        return false;
    }
}
