using System.Globalization;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class GameUiRenderer
{
    Session? desktopClockOwner;
    DateTime desktopClockOrigin;
    public void DrawDesktopClock(Session session,double time)
    {
        Use(session.GameUi);if(current?.Data==null)return;
        if(!ReferenceEquals(desktopClockOwner,session))
        {desktopClockOwner=session;desktopClockOrigin=DateTime.Now-TimeSpan.FromSeconds(time);}
        string text=(desktopClockOrigin+TimeSpan.FromSeconds(time)).ToString("HH:mm",CultureInfo.InvariantCulture);
        string font=Font(session);var color=Color.Hex(0);
        Text(font,text,316-Width(font,text)*.5f,176-current.Data.Fonts[font].LineHeight*.25f,color,color,scale:.5f);
    }
    public void DrawDesktopTitle(Session session,string title)
    {
        Use(session.GameUi);if(current?.Data==null)return;
        var color=Color.Hex(0);Text(Font(session),title,3,3,color,color);
    }
}
