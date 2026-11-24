// 捕获并复用原版演出反馈所需的画面与中间目标。

using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class SceneRenderer
{
    Target? nativeProxySurface, nativeProxyPost, nativeHistory, nativeFeedbackSnapshot;
    readonly SortedDictionary<long, Target> nativeFeedbackCheckpoints = [];
    Session? feedbackOwner;
    long feedbackTick = -1;
    bool feedbackNotes, feedbackEffects, feedbackValid, feedbackInteractive;
    const int InteractiveFeedbackFrameBudget = 4;
    int feedbackWidth;
    int feedbackVisualKey;

    bool UsesNativeApplication(Session session) => session.NativeApplicationProxyEnabled ||
        (session.Chart.ObjectName is "obj_base_gimmick" or "obj_custom_gimmick" && session.Chart.Proxies > 0 && session.HasProxyEvents);

    void RenderWithNativeFeedback(Session session, double time, bool notes, bool effects, bool interactive = false)
    {
        if (!UsesNativeApplication(session) || time < 0 || !double.IsFinite(time))
        {
            feedbackOwner = null;
            feedbackValid = false;
            RenderPresentationFrame(session, time, notes, effects, Final);
            return;
        }
        int width = ViewerSettings.ValidWidth(session.Project.RenderWidth);
        var p = session.Project;
        int visualKey = HashCode.Combine(p.GameUiEnabled, p.GameUiScore, p.GameUiExScore, p.GameUiCombo, p.GameUiJudgement,
            p.GameUiHoldEffects, p.GameUiFont, HashCode.Combine(p.VisualDelayMs, p.NoteAlignment, p.ScrollSpeed, inspectionProxy ?? -1, PresentationWidth));
        visualKey = HashCode.Combine(visualKey, interactivePreview && (session.NoteIndex.HasDensePreview || session.Score.HasDensePreview) ? previewLayers : 0);
        if (!ReferenceEquals(feedbackOwner, session) || notes != feedbackNotes || effects != feedbackEffects || width != feedbackWidth || visualKey != feedbackVisualKey || interactive != feedbackInteractive)
        {
            canvas.Flush();
            ClearNativeFeedback();
            feedbackOwner = session;
            feedbackNotes = notes;
            feedbackEffects = effects;
            feedbackWidth = width;
            feedbackVisualKey = visualKey;
            feedbackInteractive = interactive;
        }
        long target = (long)Math.Floor(time * 60 + 1e-8);
        if (feedbackValid && target == feedbackTick) return;
        double Hom(long tick) => session.Timeline.Get("hom", session.Interludes?.SongAt(tick / 60.0) ?? tick / 60.0);
        // 鼠标拖动每帧可能跨过数千个 tick。大幅跳转或倒拖时仅重建末尾连续的一小段；交互近似结果不用于精确渲染或保存检查点。
        if (interactive)
            (feedbackTick, feedbackValid) = PlanInteractiveFeedback(target, feedbackTick, feedbackValid, Hom);
        else if (!feedbackValid || target < feedbackTick)
        {
            long start = target;
            // hom 为零的源帧切断此前反馈，只需查询标量状态，复位帧前无需 GPU 重放。
            while (start > 0 && Hom(start) != 0)
            {
                ExportCancellation.ThrowIfCancellationRequested();
                start--;
            }
            var checkpoint = nativeFeedbackCheckpoints.LastOrDefault(p => p.Key < target && p.Key >= start);
            feedbackValid = false;
            if (checkpoint.Value != null)
            {
                nativeHistory ??= new(canvas.Gpu, 320, 180);
                canvas.Pass(nativeHistory, checkpoint.Value.Texture, canvas.Basic);
                feedbackTick = checkpoint.Key;
                feedbackValid = true;
            }
            else feedbackTick = start - 1;
        }
        // 向前跳过 hom 为零的区间时可直接从目标帧开始；其余顺序播放按单 tick 推进。
        if (!interactive && Hom(target) == 0)
        {
            feedbackTick = target - 1;
            feedbackValid = false;
        }
        for (long tick = feedbackTick + 1; tick <= target; tick++)
        {
            RenderPresentationFrame(session, tick / 60.0, notes, effects, Final);
            feedbackTick = tick;
            feedbackValid = true;
            if (!interactive && tick % 300 == 0 && nativeHistory != null && !nativeFeedbackCheckpoints.ContainsKey(tick))
            {
                var copy = new Target(canvas.Gpu, 320, 180);
                canvas.Pass(copy, nativeHistory.Texture, canvas.Basic);
                nativeFeedbackCheckpoints.Add(tick, copy);
                while (nativeFeedbackCheckpoints.Count > 24)
                {
                    var first = nativeFeedbackCheckpoints.First();
                    first.Value.Dispose();
                    nativeFeedbackCheckpoints.Remove(first.Key);
                }
            }
        }
    }

    internal static (long PreviousTick, bool KeepHistory) PlanInteractiveFeedback(
        long target, long previousTick, bool keepHistory, Func<long, double> hom)
    {
        long first = Math.Max(0, target - InteractiveFeedbackFrameBudget + 1);
        if (!keepHistory || target < previousTick || previousTick < first - 1)
        {
            previousTick = first - 1;
            keepHistory = false;
        }
        // 即使在有限回放窗口内，HOM 为零的 tick 也使更早帧失效，优先从最近的复位帧开始。
        for (long tick = target; tick > previousTick; tick--)
            if (hom(tick) == 0) return (tick - 1, false);
        return (previousTick, keepHistory);
    }

    Target BeginNativeProxySurface(Session session, double time)
    {
        nativeProxySurface ??= new(canvas.Gpu, 320, 180);
        // 原版在帧之间保留 pixelated_topscreen。用不透明度 1-hom 的黑色矩形衰减历史画面，然后绘制本帧侧条和不透明 proxy。
        if (feedbackValid && nativeHistory != null)
            canvas.Pass(nativeProxySurface, nativeHistory.Texture, canvas.Basic);
        else
            canvas.Begin(nativeProxySurface, 320, 180, 320, 180, Color.Hex(0));
        canvas.Begin(nativeProxySurface, 320, 180, 320, 180);
        canvas.Fill(new(0, 0, 320, 180), Color.Hex(0).Alpha(1 - session.Timeline.Get("hom", time)));
        canvas.Flush();
        return nativeProxySurface;
    }

    void ApplyNativeFeedback(Session session, double time)
    {
        if (nativeProxySurface == null) return;
        double hom = session.Timeline.Get("hom", time);
        if (hom > 0)
        {
            // Other_20 取本帧 proxy 绘制完成后的 surface 建立精灵，再缩放回相同目标；若采样上一帧，反馈递推结果会不同。
            nativeFeedbackSnapshot ??= new(canvas.Gpu, 320, 180);
            canvas.Pass(nativeFeedbackSnapshot, nativeProxySurface.Texture, canvas.Basic);
            canvas.Begin(nativeProxySurface, 320, 180, 320, 180);
            float angle = (float)(-2 * Math.Sin(session.Timeline.Bpm.Beat(time) * Math.PI / 16));
            canvas.Quad(nativeFeedbackSnapshot.Texture, new(-4.8f, -2.7f, 329.6f, 185.4f), Color.White.Alpha(hom * .96), angle: angle);
            canvas.Flush();
        }
        nativeHistory ??= new(canvas.Gpu, 320, 180);
        canvas.Pass(nativeHistory, nativeProxySurface.Texture, canvas.Basic);
    }

    void ClearNativeFeedback()
    {
        foreach (var item in nativeFeedbackCheckpoints.Values) item.Dispose();
        nativeFeedbackCheckpoints.Clear();
        feedbackValid = false;
        feedbackTick = -1;
    }
    void DisposeNativeFeedback()
    {
        ClearNativeFeedback();
        nativeProxySurface?.Dispose();
        nativeProxyPost?.Dispose();
        nativeHistory?.Dispose();
        nativeFeedbackSnapshot?.Dispose();
    }
}
