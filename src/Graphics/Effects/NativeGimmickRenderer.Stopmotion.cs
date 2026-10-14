using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    Target? oldTvA, oldTvB;

    /// <summary>Original OLDTV GUI-End chain; source timer increments by zero, so its noise offset/kernel stay at their initialized values.</summary>
    public bool RenderStopmotionComposite(Target output, Texture input, Session session, double time)
    {
        Prepare(session);
        if (current?.Data is not { SceneComposite: "self" or "stopmotion" } definition) return false;
        if (!StopmotionState.ShaderActive(session.Chart, session.Timeline.Bpm, time))
            return false;
        canvas.Flush();
        oldTvA ??= new(canvas.Gpu, input.Width, input.Height, linear: true);
        oldTvB ??= new(canvas.Gpu, input.Width, input.Height, linear: true);
        oldTvA.Resize(input.Width, input.Height); oldTvB.Resize(input.Width, input.Height);
        canvas.Pass(oldTvA, input, canvas.Basic);
        Texture source = oldTvA.Texture;
        foreach (var pass in definition.CompositePasses)
        {
            Target target = source == oldTvA.Texture ? oldTvB : oldTvA;
            if (!ApplyShader(target, source, session, time, pass)) canvas.Pass(target, source, canvas.Basic);
            source = target.Texture;
        }
        canvas.Pass(output, source, canvas.Basic);
        return true;
    }

    void DisposeStopmotionTargets()
    {
        oldTvA?.Dispose(); oldTvB?.Dispose(); oldTvA = oldTvB = null;
    }

    void DrawSelfStargazing(Session session,double time)
    {
        if(current?.ResourceRoom=="scene_gameplay_self"&&StopmotionState.HasEvent(session.Chart,session.Timeline.Bpm,time,"self_destsg"))return;
        foreach(var (name,x,prefix) in new[]{("sp_self_stargazing",0,"self_sat"),("sp_self_stargazing_tsuki",160,"self_tsuki")})
        {
            double hue=session.Timeline.Get(prefix+"color",time)*.708%255/255;
            var color=Color.Hsv((float)hue,.7f);
            color=new(color.R*.9f,color.G*.9f,color.B*.9f);
            DrawStopmotionSprite(session,name,session.Timeline.Get(prefix+"frame",time),x,0,1,1,
                color.Alpha(session.Timeline.Get(prefix+"alpha",time)));
        }
    }

    public void DrawStopmotionScene(Session session,double time)
    {
        Prepare(session);
        if(current?.Data?.Stopmotion is not { } data)return;
        int bar=(int)session.Timeline.Get("sm_bar_show",time)-1;
        if(bar>=0&&bar<data.BarColors.Length)
        {
            var color=Color.GameMaker(data.BarColors[bar]);
            DrawStopmotionSprite(session,"sp_stopmotion_opbar",0,98,65,1,1,color);
            DrawStopmotionSprite(session,"sp_stopmotion_opbar",1,98,65,1,session.Timeline.Get("sm_bar_progress",time),color);
        }
        if(current.Data.SceneComposite=="self")
            foreach(var e in StopmotionState.Events(session.Chart,session.Timeline.Bpm,time).Where(e=>e.Name=="self_gravefg"))
            {
                double age=time-session.Timeline.Bpm.Time(e.Beat);
                double scale=1.2-.2*Easings.Eval("outExpo",age/8);
                DrawStopmotionSprite(session,"sp_self_grave",0,0,90,.25,.25*scale,Color.White);
            }
        canvas.Flush();
    }

    public void DrawStopmotionHoldOverlay(Session session,double time)
    {
        Prepare(session);
        if(current?.Data?.Stopmotion==null)return;
        bool destroyed=StopmotionState.HasEvent(session.Chart,session.Timeline.Bpm,time,"storycheck1");
        bool restored=StopmotionState.HasEvent(session.Chart,session.Timeline.Bpm,time,"storycheck2");
        if(!destroyed)DrawStopmotionSprite(session,"sp_holdnote_overlay_stopmotion",0,0,0,1,1,Color.White);
        else if(restored)DrawStopmotionSprite(session,"sp_holdnote_overlay",0,0,0,1,1,Color.White.Alpha(session.Timeline.Get("holdoverlayalpha",time)));
        canvas.Flush();
    }

    public void DrawStopmotionStoryBackground(Session session,double time)
    {
        if(session.Chart.ObjectName!="obj_stopmotion_gimmick"||session.NativeGimmick.Data?.Stopmotion==null||
            !StopmotionState.HasEvent(session.Chart,session.Timeline.Bpm,time,"storycheck1")||
            StopmotionState.HasEvent(session.Chart,session.Timeline.Bpm,time,"storycheck2"))return;
        Prepare(session);
        DrawStopmotionSprite(session,"bg_placeholder",0,0,0,1,1,Color.White);
        canvas.Flush();
    }

    void DrawStopmotionSprite(Session session,string name,double frame,double x,double y,double sx,double sy,Color color)
    {
        if(current==null||color.A<=0||!current.Sprites.TryGetValue(name,out var sprite)||!double.IsFinite(frame))return;
        int index=(int)((Math.Floor(frame)%sprite.Frames.Count+sprite.Frames.Count)%sprite.Frames.Count);
        if(Image(name,index) is not { } texture)return;
        canvas.Quad(texture,new((float)(x-sprite.OriginX*sx),(float)(y-sprite.OriginY*sy),
            (float)(sprite.Width*sx),(float)(sprite.Height*sy)),color);
    }
}
