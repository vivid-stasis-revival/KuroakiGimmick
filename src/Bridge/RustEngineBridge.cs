using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace KuroakiGimmick.Core;

/// <summary>
/// Rust-owned chart / VSM parsing and BPM segment construction, enabled by default.
/// Unlike the note-motion bridge, parse results cross the ABI in one contiguous packet.
/// Keeps full C# fallback for diagnostics and unsupported VSB manifest extensions.
/// Modes: KG_RUST_ENGINE=auto(default)|off|verify|required. UI compatibility mode selects off.
/// </summary>
internal static unsafe partial class RustEngineBridge
{
    const string Library = "kg_native_core";
    const int MaxPacketBytes = 512 * 1024 * 1024;
    const int MaxChartBytes = 128 * 1024 * 1024;
    const uint MaxEvents = 1_000_000;
    static string Mode => OptimizationEngineSettings.CalculationMode;
    static bool Enabled => Mode is "auto" or "verify" or "required";
    static bool Verify => Mode == "verify";
    static bool Required => Mode == "required";
    static int failed;
    static int nativeVscHits, nativeVsbHits, nativeVsmHits, nativeBpmHits;

    [LibraryImport(Library, EntryPoint = "kg_core_parse_vsc")]
    private static partial int ParseVsc(byte* data, nuint length, byte** output, nuint* size);
    [LibraryImport(Library, EntryPoint = "kg_core_parse_vsb")]
    private static partial int ParseVsb(byte* data, nuint length, byte** output, nuint* size);
    [LibraryImport(Library, EntryPoint = "kg_core_parse_vsm")]
    private static partial int ParseVsm(byte* data, nuint length, byte** output, nuint* size);
    [LibraryImport(Library, EntryPoint = "kg_core_parse_vsc_unlimited")]
    private static partial int ParseVscUnlimited(byte* data, nuint length, byte** output, nuint* size);
    [LibraryImport(Library, EntryPoint = "kg_core_parse_vsb_unlimited")]
    private static partial int ParseVsbUnlimited(byte* data, nuint length, byte** output, nuint* size);
    [LibraryImport(Library, EntryPoint = "kg_core_parse_vsm_unlimited")]
    private static partial int ParseVsmUnlimited(byte* data, nuint length, byte** output, nuint* size);
    [LibraryImport(Library, EntryPoint = "kg_core_build_bpm")]
    private static partial int BuildBpm(BpmChange* changes, nuint count, double baseBpm,
        double offsetMs, double fixedBpm, uint hasFixed, byte** output, nuint* size);
    [LibraryImport(Library, EntryPoint = "kg_core_build_bpm_unlimited")]
    private static partial int BuildBpmUnlimited(BpmChange* changes, nuint count, double baseBpm,
        double offsetMs, double fixedBpm, uint hasFixed, byte** output, nuint* size);
    [LibraryImport(Library, EntryPoint = "kg_core_free_packet")]
    private static partial void FreePacket(byte* packet, nuint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct BpmChange { public double Time, Bpm; }

    // One managed allocation after a single FFI call. Rust owns and frees its native packet.
    private static byte[] Receive(int code, byte* packet, nuint length)
    {
        try
        {
            if (length > (nuint)Array.MaxLength || ChartLoadLimits.Exceeds((long)length, MaxPacketBytes))
                throw new InvalidDataException("RustBridge packet length exceeds budget.");
            if (length > 0 && packet == null)
                throw new InvalidDataException("RustBridge returned a null packet.");
            var bytes = new byte[(int)length];
            if (bytes.Length != 0) Marshal.Copy((nint)packet, bytes, 0, bytes.Length);
            if (code != 0)
                throw new InvalidDataException("Native parser returned " + code + ": " + Encoding.UTF8.GetString(bytes));
            return bytes;
        }
        finally { if (packet != null) FreePacket(packet, length); }
    }
    private static byte[] CallParser(byte[] input, int kind)
    {
        fixed (byte* data = input)
        {
            byte* output = null; nuint length = 0;
            int rc = kind switch
            {
                0 => ChartLoadLimits.Unlimited ? ParseVscUnlimited(data,(nuint)input.Length,&output,&length) : ParseVsc(data,(nuint)input.Length,&output,&length),
                1 => ChartLoadLimits.Unlimited ? ParseVsbUnlimited(data,(nuint)input.Length,&output,&length) : ParseVsb(data,(nuint)input.Length,&output,&length),
                2 => ChartLoadLimits.Unlimited ? ParseVsmUnlimited(data,(nuint)input.Length,&output,&length) : ParseVsm(data,(nuint)input.Length,&output,&length),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            return Receive(rc, output, length);
        }
    }
    private static void Report(Exception e, string area)
    {
        // A missing library or old bridge only gets one report, no per-frame log storms.
        if (System.Threading.Interlocked.Exchange(ref failed, 1) == 0)
            Console.Error.WriteLine($"[RustEngine] {area}: {e.Message}. Falling back to C#.");
        if (Required) throw new InvalidOperationException($"RustEngine {area} failed", e);
    }

    private static byte[] ReadChartBytes(string path)
    {
        using var stream = File.OpenRead(path);
        long length = stream.Length;
        if (ChartLoadLimits.Exceeds(length, MaxChartBytes))
            throw new InvalidDataException("Chart exceeds the 128 MiB limit.");
        var bytes = new byte[checked((int)length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    internal static bool TryLoadVsc(string path, out Chart? result)
    {
        result = null;
        if (!Enabled) return false;
        if (ChartLoadLimits.Unlimited && new FileInfo(path).Length > Array.MaxLength) return false; // Streaming managed parser.
        try
        {
            var timer = Stopwatch.StartNew();
            var chart = DecodeChart(CallParser(ReadChartBytes(path), 0), Path.GetFileNameWithoutExtension(path), path);
            if (Verify)
            {
                var reference = VscReader.LoadOriginal(path);
                if (!EquivalentNotes(chart, reference))
                    throw new InvalidDataException("VSC note/dialect mismatch against C# reference.");
            }
            result = chart;
            System.Threading.Interlocked.Increment(ref nativeVscHits);
            Console.Error.WriteLine($"[RustEngine] VSC parsed: {chart.Notes.Count} notes, {chart.Diagnostics.Count} diagnostics, {timer.Elapsed.TotalMilliseconds:F2} ms ({Mode}).");
            return true;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
            or InvalidDataException or IOException or ArgumentException or OverflowException)
        {
            Report(e,"VSC");return false;
        }
    }

    internal static bool TryLoadVsb(string path,string? definitionPath,string profile,out Chart? result)
    {
        result=null;
        if (!Enabled) return false;
        if (ChartLoadLimits.Unlimited && new FileInfo(path).Length > Array.MaxLength) return false;
        try
        {
            var timer=Stopwatch.StartNew();
            var chart=DecodeChart(CallParser(ReadChartBytes(path),1),Path.GetFileNameWithoutExtension(path));
            // The binary encoding stores numeric IDs for object-local Mods. Rust parses the
            // binary; existing catalog lookup retains the original extension ID ordering.
            var extras=GimmickCatalog.ReadExtraMods(chart.ObjectName,Path.GetDirectoryName(path),definitionPath,profile);
            for(int i=0;i<chart.Mods.Count;i++)
            {
                var m=chart.Mods[i];
                string prefix=chart.ObjectName+":mod_";
                if(!m.Name.StartsWith(prefix,StringComparison.Ordinal)) continue;
                if(!int.TryParse(m.Name.AsSpan(prefix.Length),out int id)) continue;
                string name=ModCatalog.Decode(id,chart.ObjectName,extras);
                if(name!=m.Name) chart.Mods[i]=m with {Name=name};
            }
            if(Verify)
            {
                var reference=VsbReader.LoadOriginal(path,definitionPath,profile);
                if(!EquivalentNotes(chart,reference)||!EquivalentMods(chart,reference))
                    throw new InvalidDataException("VSB note/mod/proxy mismatch against C# reference.");
            }
            result=chart;
            System.Threading.Interlocked.Increment(ref nativeVsbHits);
            Console.Error.WriteLine($"[RustEngine] VSB parsed: {chart.Notes.Count} notes, {timer.Elapsed.TotalMilliseconds:F2} ms ({Mode}).");
            return true;
        }
        catch(Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
            or InvalidDataException or IOException or ArgumentException or OverflowException)
        {
            Report(e,"VSB");return false;
        }
    }

    internal static bool TryApplyVsm(Chart destination, string text, string source)
    {
        if (!Enabled) return false;
        try
        {
            var timer = Stopwatch.StartNew();
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            var parsed = DecodeChart(CallParser(Encoding.UTF8.GetBytes(text), 2), "VSM", source);
            if (Verify)
            {
                var reference = new Chart { ObjectName = destination.ObjectName,
                    Proxies = destination.Proxies, ProxyCountDeclared = destination.ProxyCountDeclared };
                VsmReader.ReplaceModsTextOriginal(reference, text, source);
                var converted = new Chart { ObjectName = destination.ObjectName,
                    Proxies = destination.Proxies, ProxyCountDeclared = destination.ProxyCountDeclared };
                ApplyVsm(converted, parsed);
                if (!EquivalentMods(converted, reference))
                    throw new InvalidDataException("VSM metadata/mod/per-frame mismatch against C# reference.");
            }
            ApplyVsm(destination, parsed);
            System.Threading.Interlocked.Increment(ref nativeVsmHits);
            Console.Error.WriteLine($"[RustEngine] VSM parsed: {parsed.Mods.Count} events, {timer.Elapsed.TotalMilliseconds:F2} ms ({Mode}).");
            return true;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
            or InvalidDataException or IOException or ArgumentException or OverflowException)
        {
            Report(e,"VSM");return false;
        }
    }
    private static void ApplyVsm(Chart target, Chart parsed)
    {
        target.Mods.Clear(); target.Mods.AddRange(parsed.Mods);
        target.PerFrame.Clear(); target.PerFrame.AddRange(parsed.PerFrame);
        target.Diagnostics.AddRange(parsed.Diagnostics);
        target.Metadata.Clear();
        foreach (var item in parsed.Metadata) target.Metadata[item.Key] = item.Value;
        if (parsed.Metadata.ContainsKey("obj")) target.ObjectName = parsed.ObjectName;
        if (parsed.Metadata.ContainsKey("proxies"))
        {
            target.Proxies = parsed.Proxies;
            target.ProxyCountDeclared = true;
        }
    }
    private static bool Near(double a, double b) => a.Equals(b) ||
        (double.IsFinite(a) && double.IsFinite(b) && Math.Abs(a - b) <= 1e-10 * Math.Max(1, Math.Max(Math.Abs(a),Math.Abs(b))));
    private static bool EquivalentNotes(Chart a, Chart b)
    {
        if (a.VscDialect != b.VscDialect || a.Notes.Count != b.Notes.Count ||
            a.Diagnostics.Count != b.Diagnostics.Count) return false;
        for (int i = 0; i < a.Diagnostics.Count; i++)
        {
            var x = a.Diagnostics[i]; var y = b.Diagnostics[i];
            if (x.Source != y.Source || x.Line != y.Line ||
                x.Message != y.Message || x.Error != y.Error) return false;
        }
        for (int i=0; i<a.Notes.Count;i++)
        {
            var x=a.Notes[i]; var y=b.Notes[i];
            if (!Near(x.Time,y.Time)||!Near(x.End,y.End)||x.Type!=y.Type||x.Lane!=y.Lane ||
                x.Extra.Count!=y.Extra.Count || (x.ModExtra?.Count??0)!=(y.ModExtra?.Count??0) || (x.ModExtra is null)!=(y.ModExtra is null)) return false;
            foreach(var kv in x.Extra)
            {
                if(!y.Extra.TryGetValue(kv.Key,out var value) || kv.Value.GetType()!=value.GetType())return false;
                if(kv.Value is string s) {if(value is not string v || s!=v)return false;}
                else if(kv.Value is bool boolValue) {if(value is not bool v || boolValue!=v)return false;}
                else if(!Near(Convert.ToDouble(kv.Value),Convert.ToDouble(value)))return false;
            }
            if (x.ModExtra is { } mods)
            {
                if(y.ModExtra is null || !mods.SequenceEqual(y.ModExtra)) return false;
            }
        }
        return true;
    }
    private static bool EquivalentMods(Chart a, Chart b)
    {
        if (b.Diagnostics.Any(d => d.Error) || !a.Diagnostics.SequenceEqual(b.Diagnostics) ||
            a.Mods.Count!=b.Mods.Count || a.PerFrame.Count!=b.PerFrame.Count ||
            a.ObjectName!=b.ObjectName || a.Proxies!=b.Proxies || a.ProxyCountDeclared!=b.ProxyCountDeclared ||
            a.Metadata.Count!=b.Metadata.Count) return false;
        if(!a.Metadata.SequenceEqual(b.Metadata)) return false;
        for(int i=0;i<a.Mods.Count;i++)
        {
            var x=a.Mods[i];var y=b.Mods[i];
            if (!Near(x.Beat,y.Beat)||!Near(x.Duration,y.Duration)||!Near(x.From,y.From)||!Near(x.To,y.To)||
                x.Ease!=y.Ease||x.Name!=y.Name||x.Proxy!=y.Proxy||x.Order!=y.Order||x.SourceLine!=y.SourceLine) return false;
        }
        for(int i=0;i<a.PerFrame.Count;i++)
        {
            var x=a.PerFrame[i];var y=b.PerFrame[i];
            if (!Near(x.StartBeat,y.StartBeat)||!Near(x.EndBeat,y.EndBeat)||x.Function!=y.Function) return false;
        }
        return true;
    }

    // KGS2 adds non-fatal source diagnostics; KGS1 remains accepted for older native libraries.
    private static Chart DecodeChart(byte[] bytes, string title, string? sourcePath = null)
    {
        using var stream = new MemoryStream(bytes, false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, false);
        string wireVersion = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (wireVersion is not ("KGS1" or "KGS2"))
            throw new InvalidDataException("RustBridge chart wire version mismatch.");
        var chart = new Chart { Title=title, VscDialect=reader.ReadByte()!=0 ? VscDialect.Csm340 : VscDialect.Legacy,
            ProxyCountDeclared=reader.ReadByte()!=0, Proxies=reader.ReadByte(), ObjectName=ReadText(reader) };
        int meta=ReadCount(reader,ChartLoadLimits.CountLimit(100_000));
        for(int i=0;i<meta;i++) chart.Metadata[ReadText(reader)] = ReadText(reader);
        // No arbitrary note-count cap: validate against packet bytes instead.
        // Each encoded note requires at least 33 bytes (time/type/lane/end,
        // extra count, modExtra presence/count). The packet has a 512 MiB budget.
        int notes = ReadCount(reader, int.MaxValue);
        if (notes > (stream.Length - stream.Position) / 33)
            throw new InvalidDataException("RustBridge note count exceeds packet contents.");
        for(int i=0;i<notes;i++)
        {
            double time=reader.ReadDouble(); int kind=reader.ReadInt32(); int lane=reader.ReadInt32(); double end=reader.ReadDouble();
            int xcount=ReadCount(reader,256);
            Dictionary<int,object>? extras = xcount == 0 ? null : new(xcount);
            for(int x=0;x<xcount;x++)
            {
                int key=reader.ReadByte();int tag=reader.ReadByte();
                extras![key]=tag switch {0=>(object)reader.ReadDouble(),1=>ReadText(reader),2=>reader.ReadByte()!=0,
                    3=>checked((byte)reader.ReadDouble()),4=>checked((sbyte)reader.ReadDouble()),
                    5=>checked((uint)reader.ReadDouble()),6=>checked((int)reader.ReadDouble()),
                    _=>throw new InvalidDataException("Invalid Rust extra type.")};
            }
            bool hasModExtra=reader.ReadByte()!=0;
            int mcount=ReadCount(reader,ChartLoadLimits.CountLimit(100_000));
            Dictionary<string,string?>? modExtra=hasModExtra?new Dictionary<string,string?>(StringComparer.Ordinal):null;
            if(!hasModExtra && mcount>0) throw new InvalidDataException("Invalid Rust modExtra presence flag.");
            for(int m=0;m<mcount;m++)
            {
                string key=ReadText(reader);modExtra![key]=reader.ReadByte()!=0?ReadText(reader):null;
            }
            chart.Notes.Add(new(time,kind,lane,end,extras ?? Note.EmptyExtra,modExtra));
        }
        int events=ReadCount(reader,ChartLoadLimits.CountLimit((int)MaxEvents));
        for(int i=0;i<events;i++)
        {
            double beat=reader.ReadDouble(), duration=reader.ReadDouble(), from=reader.ReadDouble(), to=reader.ReadDouble();
            int proxy=reader.ReadInt32(), order=ReadCount(reader,ChartLoadLimits.CountLimit((int)MaxEvents)), line=ReadCount(reader,int.MaxValue);
            string ease=ReadText(reader), name=ReadText(reader);
            chart.Mods.Add(new(beat,duration,ease,from,to,name,proxy,order,line));
        }
        int frames=ReadCount(reader,ChartLoadLimits.CountLimit((int)MaxEvents));
        for(int i=0;i<frames;i++) chart.PerFrame.Add(new(reader.ReadDouble(),reader.ReadDouble(),ReadText(reader)));
        if (wireVersion == "KGS2")
        {
            int count = ReadCount(reader, ChartLoadLimits.CountLimit(1_000_000));
            if (count > 0 && sourcePath is null)
                throw new InvalidDataException("Rust diagnostics require a source path.");
            for (int i = 0; i < count; i++)
            {
                int line = ReadCount(reader, int.MaxValue);
                string message = ReadText(reader);
                chart.Diagnostics.Add(new(sourcePath!, line, message));
            }
        }
        if(stream.Position!=stream.Length) throw new InvalidDataException("Trailing bytes in Rust chart packet.");
        return chart;
    }
    private static int ReadCount(BinaryReader r,int limit)
    {
        uint n=r.ReadUInt32();if(n>(uint)limit) throw new InvalidDataException("RustBridge count exceeds limit.");return (int)n;
    }
    private static string ReadText(BinaryReader r)
    {
        int n=ReadCount(r,ChartLoadLimits.CountLimit(64*1024*1024));
        if(n>r.BaseStream.Length-r.BaseStream.Position) throw new EndOfStreamException("Truncated native string.");
        return Encoding.UTF8.GetString(r.ReadBytes(n));
    }

    internal static bool TryBuildBpm(Chart chart,double baseBpm,double offsetMs,double? fixedBpm,out List<BpmMap.Segment>? result)
    {
        result=null;
        if (!Enabled) return false;
        try
        {
            var changes=chart.Notes.Where(n=>n.Type==3 && n.Extra.ContainsKey(1))
                .Select(n=>new BpmChange {Time=n.Time, Bpm=Convert.ToDouble(n.Extra[1])}).ToArray();
            byte* packet=null;nuint length=0;
            fixed (BpmChange* data=changes)
            {
                int rc=ChartLoadLimits.Unlimited
                    ? BuildBpmUnlimited(data,(nuint)changes.Length,baseBpm,offsetMs,fixedBpm??0,fixedBpm.HasValue?1u:0u,&packet,&length)
                    : BuildBpm(data,(nuint)changes.Length,baseBpm,offsetMs,fixedBpm??0,fixedBpm.HasValue?1u:0u,&packet,&length);
                var bytes=Receive(rc,packet,length);
                using var stream=new MemoryStream(bytes,false);
                using var r=new BinaryReader(stream);
                if(Encoding.ASCII.GetString(r.ReadBytes(4))!="KGB1") throw new InvalidDataException("Rust BPM wire mismatch.");
                int count=ReadCount(r,int.MaxValue);
                if(count==0) throw new InvalidDataException("Rust BPM segments empty.");
                if(count > (stream.Length - stream.Position) / 24)
                    throw new InvalidDataException("Rust BPM segment count exceeds packet contents.");
                var segments=new List<BpmMap.Segment>(count);
                for(int i=0;i<count;i++) segments.Add(new(r.ReadDouble(),r.ReadDouble(),r.ReadDouble()));
                if(stream.Position!=stream.Length) throw new InvalidDataException("Rust BPM trailing bytes.");
                if(Verify && !CompareBpm(changes,baseBpm,fixedBpm,segments))
                    throw new InvalidDataException("BPM segment mismatch against C# reference.");
                result=segments;
                System.Threading.Interlocked.Increment(ref nativeBpmHits);
                return true;
            }
        }
        catch(Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
            or InvalidDataException or IOException or ArgumentException or OverflowException)
        {
            Report(e,"BPM");return false;
        }
    }
    /// <summary>Headless native integration check, intended for KG_RUST_ENGINE=verify/required.</summary>
    internal static void SelfTest()
    {
        if (!Enabled) throw new InvalidOperationException("Set KG_RUST_ENGINE=verify or required for native engine testing.");
        string path=Path.Combine(Path.GetTempPath(),"kg-rust-engine-"+Guid.NewGuid().ToString("N")+".vsc");
        int vscBefore=nativeVscHits, vsmBefore=nativeVsmHits, bpmBefore=nativeBpmHits;
        try
        {
            File.WriteAllText(path, "1000,0,1\n1100,2,2,1300\n2000,3,0,b:240\n", new UTF8Encoding(false));
            var chart=VscReader.Load(path);
            if(chart.Notes.Count!=3 || chart.Notes[1].End!=1.3)
                throw new InvalidDataException("RustEngine VSC smoke check failed.");
            const string vsm="!obj:obj_custom_gimmick\n!proxies:2\n0:2:1,1,linear,0,1,prx,-1\nmpf\n0,4,test_callback\n";
            VsmReader.ReplaceModsText(chart,vsm,"smoke.vsm");
            if(chart.Mods.Count!=3 || chart.PerFrame.Count!=1 || chart.Proxies!=2)
                throw new InvalidDataException("RustEngine VSM smoke check failed.");
            var map=new BpmMap(chart,120,250);
            if (Math.Abs(map.Time(4)-2.25)>1e-10 || Math.Abs(map.Beat(2.25)-4)>1e-10)
                throw new InvalidDataException("RustEngine BPM smoke check failed.");
            if(nativeVscHits<=vscBefore || nativeVsmHits<=vsmBefore || nativeBpmHits<=bpmBefore)
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
            // Non-fatal VSC v/s anchors must survive Rust decoding with the
            // exact source line and messages produced by the original reader.
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
        finally {if(File.Exists(path)) File.Delete(path);}
    }

    private static bool CompareBpm(BpmChange[] changes,double bpm,double? fixedBpm,List<BpmMap.Segment> actual)
    {
        var expected=new List<BpmMap.Segment>{new(0,0,fixedBpm??bpm)};
        if(fixedBpm is null)
            foreach(var n in changes)
            {
                if(n.Time<0){expected[0]=new(0,0,n.Bpm);continue;}
                var prev=expected[^1];double beat=prev.Beat+(n.Time-prev.Time)*prev.Bpm/60;
                if(n.Time==prev.Time) expected[^1]=new(n.Time,beat,n.Bpm);
                else expected.Add(new(n.Time,beat,n.Bpm));
            }
        if(expected.Count!=actual.Count) return false;
        for(int i=0;i<expected.Count;i++)
            if(!Near(expected[i].Time,actual[i].Time)||!Near(expected[i].Beat,actual[i].Beat)||!Near(expected[i].Bpm,actual[i].Bpm)) return false;
        return true;
    }
}
