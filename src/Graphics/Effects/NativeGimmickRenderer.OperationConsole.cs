using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    Target? filterChainA, filterChainB;

    /// <summary>Original OLDTV GUI-End chain; source timer increments by zero, so its noise offset/kernel stay at their initialized values.</summary>
    public bool RenderFilterChain(Target output, Texture input, Session session, double time)
    {
        Prepare(session);
        if (current?.Data is not { SceneComposite: "filter-chain" } definition) return false;
        if (!GimmickLifecycle.At(definition.CompositeLifecycle,session.Timeline.Callbacks,time))
            return false;
        canvas.Flush();
        filterChainA ??= new(canvas.Gpu, input.Width, input.Height, linear: true);
        filterChainB ??= new(canvas.Gpu, input.Width, input.Height, linear: true);
        filterChainA.Resize(input.Width, input.Height); filterChainB.Resize(input.Width, input.Height);
        canvas.Pass(filterChainA, input, canvas.Basic);
        Texture source = filterChainA.Texture;
        foreach (var pass in definition.CompositePasses)
        {
            Target target = source == filterChainA.Texture ? filterChainB : filterChainA;
            if (!ApplyShader(target, source, session, time, pass)) canvas.Pass(target, source, canvas.Basic);
            source = target.Texture;
        }
        canvas.Pass(output, source, canvas.Basic);
        return true;
    }

    void DisposeFilterChainTargets()
    {
        filterChainA?.Dispose(); filterChainB?.Dispose(); filterChainA = filterChainB = null;
    }

    public void DrawOperationConsoleScene(Session session,double time)
    {
        Prepare(session);
        if(current?.Data?.Console is not { } data)return;
        int bar=(int)session.Timeline.Get("sm_bar_show",time)-1;
        if(bar>=0&&bar<data.BarColors.Length)
        {
            var color=Color.GameMaker(data.BarColors[bar]);
            DrawOperationConsoleSprite(session,"sp_stopmotion_opbar",0,98,65,1,1,color);
            DrawOperationConsoleSprite(session,"sp_stopmotion_opbar",1,98,65,1,session.Timeline.Get("sm_bar_progress",time),color);
        }
        if(current.Behavior("crt-console")?.Flag("grave")==true)
            foreach(var e in OperationConsoleState.Events(session.Timeline,time).Where(e=>e.Name=="self_gravefg"))
            {
                double age=time-e.Time;
                double scale=1.2-.2*Easings.Eval("outExpo",age/8);
                DrawOperationConsoleSprite(session,"sp_self_grave",0,0,90,.25,.25*scale,Color.White);
            }
        canvas.Flush();
    }

    public void DrawOperationConsoleHoldOverlay(Session session,double time)
    {
        Prepare(session);
        if(current?.Data?.Console==null)return;
        bool destroyed=OperationConsoleState.HasEvent(session.Timeline,time,"storycheck1");
        bool restored=OperationConsoleState.HasEvent(session.Timeline,time,"storycheck2");
        if(!destroyed)DrawOperationConsoleSprite(session,"sp_holdnote_overlay_stopmotion",0,0,0,1,1,Color.White);
        else if(restored)DrawOperationConsoleSprite(session,"sp_holdnote_overlay",0,0,0,1,1,Color.White.Alpha(session.Timeline.Get("holdoverlayalpha",time)));
        canvas.Flush();
    }

    public void DrawOperationConsoleStoryBackground(Session session,double time)
    {
        if(current?.Behavior("crt-console")?.Flag("commands")!=true||session.NativeGimmick.Data?.Console==null||
            !OperationConsoleState.HasEvent(session.Timeline,time,"storycheck1")||
            OperationConsoleState.HasEvent(session.Timeline,time,"storycheck2"))return;
        Prepare(session);
        DrawOperationConsoleSprite(session,"bg_placeholder",0,0,0,1,1,Color.White);
        canvas.Flush();
    }

    void DrawOperationConsoleSprite(Session session,string name,double frame,double x,double y,double sx,double sy,Color color)
    {
        if(current==null||color.A<=0||!current.Sprites.TryGetValue(name,out var sprite)||!double.IsFinite(frame))return;
        int index=(int)((Math.Floor(frame)%sprite.Frames.Count+sprite.Frames.Count)%sprite.Frames.Count);
        if(Image(name,index) is not { } texture)return;
        canvas.Quad(texture,new((float)(x-sprite.OriginX*sx),(float)(y-sprite.OriginY*sy),
            (float)(sprite.Width*sx),(float)(sprite.Height*sy)),color);
    }
}
