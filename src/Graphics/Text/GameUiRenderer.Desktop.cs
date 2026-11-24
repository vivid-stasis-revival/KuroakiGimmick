// 绘制模拟桌面的时钟和标题。

using System.Globalization;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class GameUiRenderer
{
    Session? desktopClockOwner;
    DateTime desktopClockOrigin;
    /// <summary>在模拟桌面的时钟区域绘制时间。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    public void DrawDesktopClock(Session session, double time)
    {
        Use(session.GameUi);
        if (current?.Data == null) return;
        if (!ReferenceEquals(desktopClockOwner, session))
        {
            desktopClockOwner = session;
            desktopClockOrigin = DateTime.Now - TimeSpan.FromSeconds(time);
        }
        string text = (desktopClockOrigin + TimeSpan.FromSeconds(time)).ToString("HH:mm", CultureInfo.InvariantCulture);
        string font = Font(session);
        var color = Color.Hex(0);
        Text(font, text, 316 - Width(font, text) * .5f, 176 - current.Data.Fonts[font].LineHeight * .25f, color, color, scale: .5f);
    }
    /// <summary>用游戏字体绘制模拟窗口的标题。</summary>
    /// <param name="session">提供谱面、时间轴及已加载资源的会话。</param>
    /// <param name="title">窗口或内容的显示标题。</param>
    public void DrawDesktopTitle(Session session, string title)
    {
        Use(session.GameUi);
        if (current?.Data == null) return;
        var color = Color.Hex(0);
        Text(Font(session), title, 3, 3, color, color);
    }
}
