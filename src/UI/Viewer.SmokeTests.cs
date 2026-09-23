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
    /// <summary>
    /// 走真实 SDL 事件与真实绘制路径的界面自测，失败即抛异常。关闭 UI 动画后一帧就是最终布局，
    /// 因此单帧截图不会拍到入场过程中的半透明状态；有限 tween 的数学由独立的 CPU 测试覆盖。
    /// </summary>
    public void SmokeUi()
    {
        // 检查的是稳定后的命中矩形。有限过渡的数学由独立的 CPU 测试覆盖。
        preferences.UiAnimations = false;
        // 下面的合成坐标都基于未经缩放的 1440x940 测试画布。
        preferences.Workspace = new WorkspaceLayout { FollowDisplayScale = false, UiScale = 1 };
        bool before = notes;
        // scancode 17 = N：切换音符显示，再按一次还原。
        Handle(new()
        {
            Type = 0x300,
            Scan = 17
        });
        if (notes == before)
        {
            throw new Exception("N shortcut failed.");
        }
        Handle(new()
        {
            Type = 0x300,
            Scan = 17
        });
        SetTime(4);
        // scancode 79 = 右方向键：无 Shift 时步进正好一帧 = 1/fps 秒。
        Handle(new()
        {
            Type = 0x300,
            Scan = 79
        });
        if (Math.Abs(transport.Position - (4 + 1.0 / fps)) > 1e-6)
        {
            throw new Exception("Frame-step failed.");
        }
        // scancode 12 = I：把导出 IN 标记设在当前播放位置。
        Handle(new()
        {
            Type = 0x300,
            Scan = 12
        });
        if (Math.Abs(rangeIn - transport.Position) > 1e-6)
        {
            throw new Exception("Export in marker failed.");
        }
        // scancode 11 = H 打开快捷键卡片，41 = Esc 关闭。
        Handle(new()
        {
            Type = 0x300,
            Scan = 11
        });
        Draw(1440, 940);
        if (!help)
        {
            throw new Exception("Help toggle failed.");
        }
        Handle(new()
        {
            Type = 0x300,
            Scan = 41
        });
        SetTime(45);
        Draw(1440, 940);
        // 剧情轨要排在设置面板那一段之前：它自带完整的开关编辑器流程，而下面几条断言靠的是查看器自己的布局。
        SmokeEpisodeUi();
        editorMode = false; Draw(1440, 940);
        if (Current.GameUi.Data?.Fonts.ContainsKey(ViewerSettings.MonacoFont) == true)
        {
            string saved = Current.Project.GameUiFont;
            var session = Current;
            OpenSettings();
            // (900, 172) 命中设置面板的 MONACO 按钮；坐标基于 1440x940 画布，按下后必须补一帧 Draw 才会消费这次点击。
            // 面板高度改了就要跟着改：y 取的是 Text Font 那一行在淡入补间两端都覆盖到的重叠区间。
            Handle(new()
            {
                Type = 0x401,
                Button = 1,
                X = 900,
                Y = 172
            });
            Draw(1440, 940);
            Handle(new()
            {
                Type = 0x402,
                Button = 1
            });
            Draw(1440, 940);
            // ReferenceEquals 是关键断言：改字体只能改当前会话的 project，不得重新解析谱面、换出 Session。
            if (Current.Project.GameUiFont != ViewerSettings.MonacoFont || !ReferenceEquals(Current, session))
            {
                throw new Exception("Monaco setting did not switch in the current session.");
            }
            // (700, 172) 命中同一行的 DEFAULT 按钮。
            Handle(new()
            {
                Type = 0x401,
                Button = 1,
                X = 700,
                Y = 172
            });
            Draw(1440, 940);
            Handle(new()
            {
                Type = 0x402,
                Button = 1
            });
            Draw(1440, 940);
            if (Current.Project.GameUiFont != ViewerSettings.DefaultFont)
            {
                throw new Exception("Default font setting did not restore.");
            }
            // 复位为进入自测前的状态，供后续自测继续复用同一个 Viewer。
            settings = false;
            Current.Project.GameUiFont = saved;
        }
    }
}

