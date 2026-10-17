namespace KuroakiGimmick.Core;

public sealed partial class Session
{
    /// <summary>Capture on the UI thread. Workers never reread VSM/VSP or share mutable score/timeline/diagnostic state.</summary>
    internal Func<Session> CaptureRenderFactory()
    {
        var project = Project.Copy();
        var chart = Chart.CopyForSession();
        var profile = NativeGimmick.ForkForRender(chart);
        var timeline = Timeline.ForkForRender(chart, profile);
        var snapshot = new Session(this, project, chart, profile, timeline);
        bool unlimited = ChartLoadLimits.Unlimited;
        return () =>
        {
            using var limits = ChartLoadLimits.Override(unlimited);
            return new Session(snapshot, project.Copy(), chart.CopyForSession(), profile, timeline);
        };
    }

    Session(Session source, ViewerProject project, Chart chart, NativeGimmickProfile profile, Timeline timeline)
    {
        Project = project; ProjectPath = source.ProjectPath; Chart = chart;
        NativeGimmick = profile.ForkForRender(chart);
        // Loaded assets and PCM are immutable snapshots; GPU textures remain entirely worker-owned.
        WindowMotion = source.WindowMotion; Images = source.Images; Texts = source.Texts;
        Audio = source.Audio; SourceAudio = source.SourceAudio; SongAudio = source.SongAudio;
        Interludes = source.Interludes; Episode = source.Episode; Jackets = source.Jackets;
        DfEnabled = source.DfEnabled; DistortBgEnabled = source.DistortBgEnabled;
        NonBaseFxEnabled = source.NonBaseFxEnabled; SkinChangeEnabled = source.SkinChangeEnabled;
        Fx = source.Fx; GameUi = source.GameUi; Song = source.Song;
        Timeline = timeline.ForkForRender(Chart, NativeGimmick);
        Score = source.Score.ForkForRender(); NoteIndex = source.NoteIndex; EventDensity = new(Chart, Timeline.Bpm);
        HasProxyEvents = source.HasProxyEvents; Checker = source.Checker;
        Duration = source.Duration; Playback = source.Playback;
        Stars = source.Stars.ForkForRender(Timeline);
        noteSourceStamp = source.noteSourceStamp; vscSource = source.vscSource; noteBpmSettings = source.noteBpmSettings;
    }
}
