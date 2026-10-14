using System.Globalization;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class GameUiRenderer
{
    public void DrawStopmotionText(Session session,double time)
    {
        if(session.NativeGimmick.Data?.Stopmotion is not { } data)return;
        Use(session.GameUi);
        const string font="fnt_stopmotion";
        if(current?.Data?.Fonts.ContainsKey(font)!=true)
        {session.NativeGimmick.Fail("stopmotion-font","The original fnt_stopmotion atlas is missing.");return;}
        int bar=(int)session.Timeline.Get("sm_bar_show",time)-1;
        bool outlined=session.Chart.ObjectName=="obj_self_gimmick";
        void Draw(string value,float x,float y,Color color)
        {
            if(outlined)
                for(int dx=-1;dx<=1;dx++)for(int dy=-1;dy<=1;dy++)
                    if(dx!=0||dy!=0)Text(font,value,x+dx,y+dy,Color.Hex(0),Color.Hex(0));
            Text(font,value,x,y,color,color);
        }
        if(bar>=0&&bar<data.BarTexts.Length&&bar<data.BarColors.Length)
        {
            var color=Color.GameMaker(data.BarColors[bar]);
            // draw_text_ext uses whole-word wrapping, unlike TextDrawer's
            // separate final-character lookahead quirk used for dialogue.
            var lines=new List<string>();
            string row="";
            foreach(string word in data.BarTexts[bar].Split(' '))
            {
                string next=row.Length==0?word:row+" "+word;
                if(row.Length>0&&Width(font,next)>40){lines.Add(row);row=word;}
                else row=next;
            }
            if(row.Length>0)lines.Add(row);
            for(int i=0;i<lines.Count;i++)Draw(lines[i],210,136+i*10,color);
            string percent=(session.Timeline.Get("sm_bar_progress",time)*100).ToString("F2",CultureInfo.InvariantCulture)+"%";
            Draw(percent,97-Width(font,percent),158,color);
        }
        if(session.Chart.ObjectName=="obj_stopmotion_gimmick")
            foreach(var command in StopmotionState.Commands(data,session.Chart,session.Timeline.Bpm,time))
                Text(font,command.Text,3,(float)command.Y,Color.Hex(0x00ff00),Color.Hex(0x00ff00));
    }
}
