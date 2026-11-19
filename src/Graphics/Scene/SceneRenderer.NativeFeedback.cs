using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class SceneRenderer
{
    Target? nativeProxySurface,nativeProxyPost,nativeHistory,nativeFeedbackSnapshot;
    readonly SortedDictionary<long,Target> nativeFeedbackCheckpoints=[];
    Session? feedbackOwner;
    long feedbackTick=-1;
    bool feedbackNotes,feedbackEffects,feedbackValid,feedbackInteractive;
    const int InteractiveFeedbackFrameBudget = 4;
    int feedbackWidth;
    int feedbackVisualKey;

    bool UsesNativeApplication(Session session)=>session.NativeApplicationProxyEnabled ||
        (session.Chart.ObjectName is "obj_base_gimmick" or "obj_custom_gimmick" && session.Chart.Proxies>0 && session.HasProxyEvents);

    void RenderWithNativeFeedback(Session session,double time,bool notes,bool effects,bool interactive=false)
    {
        if(!UsesNativeApplication(session) || time<0 || !double.IsFinite(time))
        {feedbackOwner=null;feedbackValid=false;RenderPresentationFrame(session,time,notes,effects,Final);return;}
        int width=ViewerSettings.ValidWidth(session.Project.RenderWidth);
        var p=session.Project;
        int visualKey=HashCode.Combine(p.GameUiEnabled,p.GameUiScore,p.GameUiExScore,p.GameUiCombo,p.GameUiJudgement,
            p.GameUiHoldEffects,p.GameUiFont,HashCode.Combine(p.VisualDelayMs,p.NoteAlignment,p.ScrollSpeed,inspectionProxy??-1,PresentationWidth));
        visualKey=HashCode.Combine(visualKey,interactivePreview && (session.NoteIndex.HasDensePreview || session.Score.HasDensePreview) ? previewLayers : 0);
        if(!ReferenceEquals(feedbackOwner,session)||notes!=feedbackNotes||effects!=feedbackEffects||width!=feedbackWidth||visualKey!=feedbackVisualKey||interactive!=feedbackInteractive)
        {
            canvas.Flush();ClearNativeFeedback();feedbackOwner=session;feedbackNotes=notes;feedbackEffects=effects;feedbackWidth=width;feedbackVisualKey=visualKey;feedbackInteractive=interactive;
        }
        long target=(long)Math.Floor(time*60+1e-8);
        if(feedbackValid && target==feedbackTick)return;
        double Hom(long tick)=>session.Timeline.Get("hom",session.Interludes?.SongAt(tick/60.0)??tick/60.0);
        // A mouse drag can seek thousands of logic ticks each UI frame. Rebuild
        // only a short, consecutive tail on a large jump or rewind. Interactive
        // history is never reused by an exact render or saved as a checkpoint.
        if(interactive)
            (feedbackTick,feedbackValid)=PlanInteractiveFeedback(target,feedbackTick,feedbackValid,Hom);
        else if(!feedbackValid || target<feedbackTick)
        {
            long start=target;
            // A zero-hom source frame severs all older feedback. Search scalar
            // state only; no GPU replay is needed before that reset frame.
            while(start>0 && Hom(start)!=0) { ExportCancellation.ThrowIfCancellationRequested(); start--; }
            var checkpoint=nativeFeedbackCheckpoints.LastOrDefault(p=>p.Key<target && p.Key>=start);
            feedbackValid=false;
            if(checkpoint.Value!=null)
            {
                nativeHistory??=new(canvas.Gpu,320,180);
                canvas.Pass(nativeHistory,checkpoint.Value.Texture,canvas.Basic);
                feedbackTick=checkpoint.Key;feedbackValid=true;
            }
            else feedbackTick=start-1;
        }
        // Forward gaps with zero hom can also start directly at the requested
        // frame; normal sequential playback otherwise advances one logic tick.
        if(!interactive && Hom(target)==0) {feedbackTick=target-1;feedbackValid=false;}
        for(long tick=feedbackTick+1;tick<=target;tick++)
        {
            RenderPresentationFrame(session,tick/60.0,notes,effects,Final);
            feedbackTick=tick;feedbackValid=true;
            if(!interactive && tick%300==0 && nativeHistory!=null && !nativeFeedbackCheckpoints.ContainsKey(tick))
            {
                var copy=new Target(canvas.Gpu,320,180);
                canvas.Pass(copy,nativeHistory.Texture,canvas.Basic);
                nativeFeedbackCheckpoints.Add(tick,copy);
                while(nativeFeedbackCheckpoints.Count>24)
                {var first=nativeFeedbackCheckpoints.First();first.Value.Dispose();nativeFeedbackCheckpoints.Remove(first.Key);}
            }
        }
    }

    internal static (long PreviousTick, bool KeepHistory) PlanInteractiveFeedback(
        long target, long previousTick, bool keepHistory, Func<long,double> hom)
    {
        long first=Math.Max(0,target-InteractiveFeedbackFrameBudget+1);
        if(!keepHistory || target<previousTick || previousTick<first-1)
        {
            previousTick=first-1;
            keepHistory=false;
        }
        // A zero-HOM tick makes earlier frames irrelevant, even in the
        // bounded tail. Use the most recent reset frame where possible.
        for(long tick=target;tick>previousTick;tick--)
            if(hom(tick)==0)return (tick-1,false);
        return (previousTick,keepHistory);
    }

    Target BeginNativeProxySurface(Session session,double time)
    {
        nativeProxySurface??=new(canvas.Gpu,320,180);
        // The source never clears pixelated_topscreen between frames. Its black
        // rectangle uses 1-hom alpha, retaining history BEFORE the current
        // side strips and opaque proxies are drawn.
        if(feedbackValid && nativeHistory!=null)
            canvas.Pass(nativeProxySurface,nativeHistory.Texture,canvas.Basic);
        else
            canvas.Begin(nativeProxySurface,320,180,320,180,Color.Hex(0));
        canvas.Begin(nativeProxySurface,320,180,320,180);
        canvas.Fill(new(0,0,320,180),Color.Hex(0).Alpha(1-session.Timeline.Get("hom",time)));
        canvas.Flush();
        return nativeProxySurface;
    }

    void ApplyNativeFeedback(Session session,double time)
    {
        if(nativeProxySurface==null)return;
        double hom=session.Timeline.Get("hom",time);
        if(hom>0)
        {
            // Other_20 creates a sprite from the surface AFTER this frame's
            // proxy draw, then scales that current snapshot onto itself. Reading
            // last frame here would produce a different recurrence.
            nativeFeedbackSnapshot??=new(canvas.Gpu,320,180);
            canvas.Pass(nativeFeedbackSnapshot,nativeProxySurface.Texture,canvas.Basic);
            canvas.Begin(nativeProxySurface,320,180,320,180);
            float angle=(float)(-2*Math.Sin(session.Timeline.Bpm.Beat(time)*Math.PI/16));
            canvas.Quad(nativeFeedbackSnapshot.Texture,new(-4.8f,-2.7f,329.6f,185.4f),Color.White.Alpha(hom*.96),angle:angle);
            canvas.Flush();
        }
        nativeHistory??=new(canvas.Gpu,320,180);
        canvas.Pass(nativeHistory,nativeProxySurface.Texture,canvas.Basic);
    }

    void ClearNativeFeedback()
    {
        foreach(var item in nativeFeedbackCheckpoints.Values)item.Dispose();
        nativeFeedbackCheckpoints.Clear();feedbackValid=false;feedbackTick=-1;
    }
    void DisposeNativeFeedback()
    {
        ClearNativeFeedback();nativeProxySurface?.Dispose();nativeProxyPost?.Dispose();nativeHistory?.Dispose();nativeFeedbackSnapshot?.Dispose();
    }
}
