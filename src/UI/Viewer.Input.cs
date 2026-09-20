using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using KuroakiGimmick.Core;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

/// <summary>
/// 交互层状态协调器。加载结果通过主线程队列提交，GPU 操作只在窗口线程执行；设置、输入与绘制分文件维护。
/// </summary>
public sealed partial class Viewer
{
    /// <summary>只处理输入状态和命令，不在事件分支内直接执行耗时文件加载。</summary>
    public void Handle(Sdl.Event e)
    {
        // 先换算到逻辑坐标，再按优先级交给各手势 / 覆盖层处理器；任一处理器吃掉事件后，下方的 viewer 快捷键不再响应。
        e = LogicalInput(e);
        if (HandleTextGesture(e)) return;
        if (HandleActiveImageGesture(e)) return;
        if (HandleLayoutInput(e)) return;
        if (HandleImageDrop(e)) return;
        if (HandleEditorInput(e)) return;
        // 事件类型：0x100 退出，0x400/0x401/0x402 鼠标移动/按下/抬起，0x403 滚轮，0x1000 拖入文件，0x300 按键按下。
        if (e.Type == 0x100)
        {
            quit = true;
            return;
        }
        if (e.Type == 0x400)
        {
            mouseX = e.X;
            mouseY = e.Y;
        }
        if (e.Type == 0x401 && e.Button == 1)
        {
            mouseX = e.X;
            mouseY = e.Y;
            click = true;
            held = true;
        }
        if (e.Type == 0x402 && e.Button == 1)
        {
            held = false;
            seeking = false;
        }
        if (e.Type == 0x1000)
        {
            // 拖入文件只把路径交给后台加载任务，事件分支本身不做解析。
            var path = Marshal.PtrToStringUTF8(e.DropData);
            if (path != null)
            {
                LoadPaths([path]);
            }
        }
        if (e.Type == 0x403 && !Busy && !settings)
        {
            // 报告面板上方的滚轮翻页；其余位置每格滚动 seek 0.25 秒。
            if (diagnostics && mouseX > workspaceGeometry.RightX)
            {
                diagnosticPage = Math.Max(0, diagnosticPage - (int) e.WheelY);
            }
            else
            {
                transport.Seek(transport.Position + e.WheelY * .25);
            }
        }
        // 只响应首次按下，忽略系统自动重复。
        if (e.Type != 0x300 || e.Repeat != 0)
        {
            return;
        }
        // 0x0CC0 = 左右 Ctrl / GUI（command），3 = 左右 Shift。
        bool ctrl = (e.Modifiers & 0x0CC0) != 0, shift = (e.Modifiers & 3) != 0;
        // scancode 41 = Esc：按优先级只关闭一层，设置（并落盘）> 快捷键卡片 > 全屏 > 进行中的导出。
        if (e.Scan == 41)
        {
            if (settings)
            {
                settings = false;
                SaveSettings();
            }
            else if (help)
            {
                help = false;
            }
            else if (full)
            {
                full = false;
                Sdl.SDL_SetWindowFullscreen(host.Window, false);
            }
            else if (export is { Completed: false, Cancelled: false, Error: null })
            {
                export.Cancel();
            }
            return;
        }
        if (settings)
        {
            return;
        }
        // scancode 11 = H：帮助卡片在 Busy 时也可开关，其余快捷键在 Busy 时全部忽略。
        if (e.Scan == 11)
        {
            help = !help;
            return;
        }
        if (Busy)
        {
            return;
        }
        // scancode：44 Space，79/80 右/左（Shift 移动 1 秒，否则一帧 = 1/fps 秒），74 Home，77 End，
        // 9 F，21 R，18 O，22 S，12 I，19 P，17 N，16 M，24 U，8 E。O/S/E 需要 command 修饰键。
        switch (e.Scan)
        {
            case 44:
                transport.SetPlaying(!transport.Playing);
                break;
            case 79:
                transport.SetPlaying(false);
                transport.Seek(transport.Position + (shift ? 1 : 1.0 / fps));
                break;
            case 80:
                transport.SetPlaying(false);
                transport.Seek(transport.Position - (shift ? 1 : 1.0 / fps));
                break;
            case 74:
                transport.Seek(0);
                break;
            case 77:
                transport.Seek(Current.Duration);
                break;
            case 9:
                ToggleFull();
                break;
            case 21:
                Reload();
                break;
            case 18:
                if (ctrl)
                {
                    ChooseOpen();
                }
                break;
            case 22:
                if (ctrl)
                {
                    SaveProject();
                }
                break;
            case 12:
                rangeIn = Math.Min(transport.Position, rangeOut - 1.0 / fps);
                rangeIn = Math.Max(0, rangeIn);
                break;
            case 19:
                effects = !effects;
                break;
            case 17:
                notes = !notes;
                break;
            case 16:
                SetVolume(transport.Volume > 0 ? 0 : .8);
                break;
            case 24:
                rangeOut = Math.Max(transport.Position, rangeIn + 1.0 / fps);
                break;
            case 8:
                if (ctrl)
                {
                    ChooseExport();
                }
                break;
        }
    }

    /// <summary>在窗口线程接收加载结果、同步播放状态，并逐步推进导出任务。</summary>
    void Update()
    {
        // 排空主线程队列：对话框回调等其他线程的结果只能在这里、由窗口线程执行；单个动作抛异常不影响其余动作。
        while (actions.TryDequeue(out var a))
        {
            try
            {
                a();
            }
            catch (Exception ex)
            {
                message = ex.Message;
            }
        }
        // 后台解析完成后才在窗口线程切换会话；失败保留旧会话，只报错。resumePosition 无论成败都清空。
        if (loading is { IsCompleted: true })
        {
            try
            {
                UseSession(loading.GetAwaiter().GetResult());
                ResetEditorForLoad();
                if (resumePosition is double position)
                {
                    transport.Seek(position);
                }
            }
            catch (Exception ex)
            {
                message = L.Get("Load failed: ") + ex.Message;
            }
            loading = null;
            resumePosition = null;
            if (editorMode && editor == null) OpenEditor();
        }
        try
        {
            transport.SetChartPlayback(Current.Playback);
            transport.Update();
        }
        catch (Exception ex)
        {
            transport.SetPlaying(false);
            message = L.Get("Audio: ") + ex.Message;
        }
        UpdateImageImport();
        UpdateWorkflow();
        UpdateEditor();
        // 导出每帧只推进一步（渲染一帧或等待编码器），GPU 读回因此留在窗口线程；结束后在此释放并清空。
        if (export != null)
        {
            export.Tick();
            message = export.Error ?? (export.Cancelled ? L.Get("Export cancelled")
                : export.Completed ? L.Get("Export complete")
                : export.Finalizing ? L.Get("Finalizing audio and MP4...")
                : L.Format($"Rendering frame {export.Frame:N0} / {export.TotalFrames:N0}"));
            if (export.Completed || export.Cancelled || export.Error != null)
            {
                export.Dispose();
                export = null;
            }
        }
    }
}
