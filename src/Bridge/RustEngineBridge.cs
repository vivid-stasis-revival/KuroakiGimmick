using System.Text;
using Engine = KenomaGMK.Core;
using KenomaGMK.Native;

namespace KuroakiGimmick.Core;

/// <summary>把加载上下文和 KG 公开数据类型适配到唯一的原生谱面包读取器。</summary>
internal static unsafe partial class RustEngineBridge
{
    static int nativeVscHits, nativeVsmHits, nativeBpmHits;
    static bool Enabled => OptimizationEngineSettings.CalculationMode != "off";
    static ChartReader Reader => new(ChartReader.ParseMode(OptimizationEngineSettings.CalculationMode), ChartLoadLimits.Unlimited);

    internal static bool TryLoadVsc(string path, out Chart? result)
    {
        result = null;
        if (!Reader.TryLoadVsc(path,out var parsed)) return false;
        result = EngineChartAdapter.Import(parsed!);
        Interlocked.Increment(ref nativeVscHits);
        return true;
    }

    internal static bool TryLoadVsb(string path, string? definitionPath, string profile, out Chart? result)
    {
        result = null;
        if (!Reader.TryLoadVsb(path,name => GimmickCatalog.ReadExtraMods(name,Path.GetDirectoryName(path),definitionPath,profile),
            out var parsed)) return false;
        result = EngineChartAdapter.Import(parsed!);
        return true;
    }

    internal static bool TryApplyVsm(Chart destination, string text, string source)
    {
        var projected = EngineChartAdapter.ModHeaders(destination);
        if (!Reader.TryApplyVsm(projected,text,source)) return false;
        EngineChartAdapter.ReplaceMods(destination,projected);
        Interlocked.Increment(ref nativeVsmHits);
        return true;
    }

    internal static bool TryApplyVsmFile(Chart destination, string path)
    {
        var projected = EngineChartAdapter.ModHeaders(destination);
        if (!Reader.TryApplyVsmFile(projected,path)) return false;
        EngineChartAdapter.ReplaceMods(destination,projected);
        Interlocked.Increment(ref nativeVsmHits);
        return true;
    }

    internal static bool TryBuildBpm(Chart chart,double bpm,double offsetMs,double? fixedBpm,out List<BpmMap.Segment>? result)
    {
        result = null;
        var changes = chart.Notes.Where(n => n.Type == 3 && n.Extra.ContainsKey(1)).Select(n => (n.Time,n.Extra[1]));
        if (!Reader.TryBuildBpm(changes,bpm,offsetMs,fixedBpm,out var segments)) return false;
        result = segments!.Select(s => new BpmMap.Segment(s.Time,s.Beat,s.Bpm)).ToList();
        Interlocked.Increment(ref nativeBpmHits);
        return true;
    }

    internal static FileStream? TryCloneInput(FileStream source,out string? directory) =>
        ChartReader.TryCloneInput(source,out directory);

    internal static void SelfTest()
    {
        if (!Enabled) throw new InvalidOperationException("Set KG_RUST_ENGINE=verify or required for native engine testing.");
        string path = Path.Combine(Path.GetTempPath(), "kg-rust-engine-" + Guid.NewGuid().ToString("N") + ".vsc");
        int vscBefore = nativeVscHits, vsmBefore = nativeVsmHits, bpmBefore = nativeBpmHits;
        try
        {
            File.WriteAllText(path, "1000,0,1\n1100,2,2,1300\n2000,3,0,b:240\n", new UTF8Encoding(false));
            var chart = VscReader.Load(path);
            if (chart.Notes.Count != 3 || chart.Notes[1].End != 1.3)
                throw new InvalidDataException("RustEngine VSC smoke check failed.");
            const string vsm = "!obj:obj_custom_gimmick\n!proxies:2\n0:2:1,1,linear,0,1,prx,-1\nmpf\n0,4,test_callback\n";
            VsmReader.ReplaceModsText(chart, vsm, "smoke.vsm");
            if (chart.Mods.Count != 3 || chart.PerFrame.Count != 1 || chart.Proxies != 2)
                throw new InvalidDataException("RustEngine VSM smoke check failed.");
            var map = new BpmMap(chart, 120, 250);
            if (Math.Abs(map.Time(4) - 2.25) > 1e-10 || Math.Abs(map.Beat(2.25) - 4) > 1e-10)
                throw new InvalidDataException("RustEngine BPM smoke check failed.");
            if (nativeVscHits <= vscBefore || nativeVsmHits <= vsmBefore || nativeBpmHits <= bpmBefore)
                throw new InvalidDataException("RustEngine silently used the C# fallback during the smoke test.");
            const string commentedVsm = "!obj:obj_custom_gimmick // object\n!proxies:2 // count\n0,0,linear,0,1,prx,-1 // event\nmpf // marker\n0,4,test_callback // function\n";
            int commentsBefore = nativeVsmHits;
            var commented = new Chart();
            VsmReader.ReplaceModsText(commented, commentedVsm, "comments.vsm");
            if (commented.ObjectName != "obj_custom_gimmick" || commented.Proxies != 2 ||
                commented.Mods.Count != 1 || commented.PerFrame.Single().Function != "test_callback" ||
                commented.Diagnostics.Count != 1 || commented.Diagnostics[0] !=
                    new Diagnostic("comments.vsm", 1, VsmReader.TrailingCommentWarning) ||
                nativeVsmHits <= commentsBefore)
                throw new InvalidDataException("Native VSM trailing-comment warning parity failed.");
            // VSC 中非致命的 v/s 锚点诊断在 Rust 解码后仍保留原读取器的行号和消息。
            File.WriteAllText(path, "0,3,0,b:150|v:12|s:8\n1000,0,1\n", new UTF8Encoding(false));
            int timingBefore = nativeVscHits;
            var withAnchors = VscReader.Load(path);
            if (withAnchors.Notes.Count != 2 || withAnchors.Diagnostics.Count != 2 ||
                withAnchors.Diagnostics[0].Line != 1 ||
                withAnchors.Diagnostics[0].Message != "VSC timing 'v' is not applied." ||
                withAnchors.Diagnostics[1].Message != "VSC timing 's' is not applied." ||
                withAnchors.Diagnostics.Any(d => d.Error) || nativeVscHits <= timingBefore)
                throw new InvalidDataException("RustEngine v/s timing diagnostics failed native parity.");
            Console.WriteLine("[RustEngine] native VSC/VSM/BPM, trailing-comment warnings and v/s diagnostics smoke test passed.");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

}
