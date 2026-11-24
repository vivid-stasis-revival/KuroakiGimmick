// 连接 Rust 谱面解析、BPM 构建与 C# 模型，处理原生包的接收、解码、校验及回退。

using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace KuroakiGimmick.Core;

/// <summary>默认通过 Rust 解析谱面、VSM 并构建 BPM 分段；解析结果以单个连续数据包跨越 ABI。C# 路径用于诊断及未支持的 VSB 清单扩展。KG_RUST_ENGINE 支持 auto、off、verify、required，界面的兼容模式选择 off。</summary>
internal static unsafe partial class RustEngineBridge
{
    const string Library = "kg_native_core";
    const int MaxPacketBytes = 512 * 1024 * 1024;
    const int MaxChartBytes = 128 * 1024 * 1024;
    const int MappedInputThreshold = 4 * 1024 * 1024;
    const uint MaxEvents = 1_000_000;
    static string Mode => OptimizationEngineSettings.CalculationMode;
    static bool Enabled => Mode is "auto" or "verify" or "required";
    static bool Verify => Mode == "verify";
    static bool Required => Mode == "required";
    static int failed;
    static int nativeVscHits, nativeVsbHits, nativeVsmHits, nativeBpmHits;

    [LibraryImport(Library, EntryPoint = "kg_core_parse_vsc_compact")]
    private static partial int ParseVscCompact(byte* data, nuint length, byte unlimited, byte** output, nuint* size);

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "fclonefileat", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int CloneFileAt(Microsoft.Win32.SafeHandles.SafeFileHandle source, int directory, string destination, uint flags);

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
    private struct BpmChange
    {
        public double Time, Bpm;
    }

    // LIFETIME: 直接从 Rust 所有的原生包读取。无限制模式下长度可超过 Array.MaxLength，复制到 byte[] 会引入额外上限和一份完整副本。
    sealed class NativePacketStream : UnmanagedMemoryStream
    {
        nint owned;
        readonly nuint size;
        internal NativePacketStream(byte* packet, nuint length) : base(packet, checked((long)length))
        {
            owned = (nint)packet;
            size = length;
        }
        ~NativePacketStream() => Dispose(false);
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            // LIFETIME: 显式释放与终结器共享同一指针，只有首次取走非零地址的一方归还原生包。
            nint packet = System.Threading.Interlocked.Exchange(ref owned, 0);
            if (packet != 0) FreePacket((byte*)packet, size);
            if (disposing) GC.SuppressFinalize(this);
        }
    }

    static long ValidatePacketLength(nuint length)
    {
        if (length > (nuint)nint.MaxValue)
            throw new InvalidDataException($"RustBridge packet length {length} exceeds the process address range.");
        long bytes = checked((long)length);
        if (ChartLoadLimits.Exceeds(bytes, MaxPacketBytes))
            throw new InvalidDataException($"RustBridge packet is {bytes:N0} bytes; the 512 MiB budget is active because unlimited chart loading is off for this load.");
        return bytes;
    }

    private static NativePacketStream Receive(int code, byte* packet, nuint length)
    {
        try
        {
            ValidatePacketLength(length);
            if (code != 0)
            {
                // 1. 原生错误包按 UTF-8 读取，最多解码 8192 字节以限制诊断字符串的大小。
                string detail = packet == null ? "no error packet" :
                    Encoding.UTF8.GetString(new ReadOnlySpan<byte>(packet, (int)Math.Min((ulong)length, 8192UL)));
                throw new InvalidDataException($"Native parser returned {code}: {detail}");
            }
            if (packet == null || length == 0)
                throw new InvalidDataException("RustBridge returned an empty packet.");
            // 2. 成功时转移到只读流；构造失败则由本方法的 finally 归还原生包。
            var stream = new NativePacketStream(packet, length);
            packet = null; // 所有权已转移，即使后续解码失败也由流释放。
            return stream;
        }
        finally
        {
            if (packet != null) FreePacket(packet, length);
        }
    }
    private static NativePacketStream CallParser(byte[] input, int kind)
    {
        fixed (byte* data = input)
            return CallParser(data, (nuint)input.Length, kind);
    }
    private static NativePacketStream CallParser(byte* data, nuint inputLength, int kind)
    {
        byte* output = null;
        nuint length = 0;
        int rc = kind switch
        {
            0 => ParseVscPreferred(data, inputLength, &output, &length),
            1 => ChartLoadLimits.Unlimited ? ParseVsbUnlimited(data, inputLength, &output, &length) : ParseVsb(data, inputLength, &output, &length),
            2 => ChartLoadLimits.Unlimited ? ParseVsmUnlimited(data, inputLength, &output, &length) : ParseVsm(data, inputLength, &output, &length),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return Receive(rc, output, length);
    }
    private static int ParseVscPreferred(byte* data, nuint length, byte** output, nuint* size)
    {
        try
        {
            return ParseVscCompact(data, length, ChartLoadLimits.Unlimited ? (byte)1 : (byte)0, output, size);
        }
        catch (EntryPointNotFoundException)
        {
            return ChartLoadLimits.Unlimited ? ParseVscUnlimited(data, length, output, size) : ParseVsc(data, length, output, size);
        }
    }

    private static void Report(Exception e, string area)
    {
        // 原生库缺失或版本过旧时仅报告一次，避免逐帧输出相同错误。
        if (System.Threading.Interlocked.Exchange(ref failed, 1) == 0)
            Console.Error.WriteLine($"[RustEngine] {area}: {e.Message}. Falling back to C#.");
        if (Required) throw new InvalidOperationException($"RustEngine {area} failed", e);
    }

    // LIFETIME: 原生解析器只在同步调用期间借用映射；输出包独立持有结果，关闭映射后仍然有效。
    private static NativePacketStream CallFileParser(string path, int kind)
    {
        using var stream = File.OpenRead(path);
        long length = stream.Length;
        ValidateInputLength(length, kind);
        if (length <= MappedInputThreshold)
        {
            if (kind == 2)
            {
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return CallVsmTextParser(reader.ReadToEnd());
            }
            var bytes = new byte[(int)length];
            stream.ReadExactly(bytes);
            return CallParser(bytes, kind);
        }
        // COMPAT: Windows 的 FileShare.Read 可阻止并发截短文件；Unix 的建议锁不能阻止截短使映射访问触发 SIGBUS，因此映射受控的私有快照。
        if (OperatingSystem.IsWindows()) return CallFileParser(stream, kind, normalizeVsm: kind == 2);
        if (OperatingSystem.IsMacOS())
        {
            string? directory = null;
            try
            {
                using var clone = TryCloneInput(stream, out directory);
                if (clone != null && clone.Length == length)
                    return CallFileParser(clone, kind, normalizeVsm: kind == 2);
            }
            finally
            {
                if (directory != null) Directory.Delete(directory);
            }
        }
        using var snapshot = CreateInputTemporary();
        byte[] buffer = new byte[64 * 1024];
        long remaining = length;
        while (remaining > 0)
        {
            int count = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (count == 0) throw new EndOfStreamException("Chart was truncated while reading its input snapshot.");
            snapshot.Write(buffer, 0, count);
            remaining -= count;
        }
        snapshot.Flush();
        snapshot.Position = 0;
        return CallFileParser(snapshot, kind, normalizeVsm: kind == 2);
    }

    // APFS 克隆可隔离后续写入和截短且无需完整复制文件，不支持克隆的卷仍走有界复制。
    internal static FileStream? TryCloneInput(FileStream source, out string? directory)
    {
        directory = null;
        if (!OperatingSystem.IsMacOS()) return null;
        string folder = Path.Combine(Path.GetTempPath(), "kg-chart-clone-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string destination = Path.Combine(folder, "input");
        bool transferred = false;
        try
        {
            // 目标为绝对路径，使用 AT_FDCWD。调用方持有源描述符，SafeHandle 封送在调用期间保持其有效。
            try
            {
                if (CloneFileAt(source.SafeFileHandle, -2, destination, 0) != 0) return null;
            }
            catch (EntryPointNotFoundException)
            {
                return null;
            }
            File.SetUnixFileMode(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var clone = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.DeleteOnClose | FileOptions.SequentialScan);
            directory = folder;
            transferred = true;
            return clone;
        }
        finally
        {
            if (!transferred)
            {
                File.Delete(destination);
                Directory.Delete(folder);
            }
        }
    }
    private static void ValidateInputLength(long length, int kind)
    {
        int limit = kind == 2 ? 64 * 1024 * 1024 : MaxChartBytes;
        if (ChartLoadLimits.Exceeds(length, limit))
            throw new InvalidDataException(kind == 2 ? "VSM exceeds 64 MiB." : "Chart exceeds the 128 MiB limit.");
        if ((ulong)length > (ulong)nint.MaxValue)
            throw new InvalidDataException("Chart exceeds the process address range.");
    }
    private static NativePacketStream CallFileParser(FileStream stream, int kind, bool normalizeVsm)
    {
        long length = stream.Length;
        ValidateInputLength(length, kind);
        if (length == 0) return CallParser([], kind);
        using var mapped = MemoryMappedFile.CreateFromFile(stream, null, 0, MemoryMappedFileAccess.Read,
            HandleInheritability.None, leaveOpen: true);
        using var view = mapped.CreateViewAccessor(0, length, MemoryMappedFileAccess.Read);
        byte* pointer = null;
        view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
        try
        {
            // SAFETY: 使用已获取的视图指针加上映射偏移，访问长度以流长度为限；finally 释放指针。
            pointer += view.PointerOffset;
            int preamble = 0;
            if (normalizeVsm && VsmNeedsTranscoding(pointer, length, out preamble))
            {
                stream.Position = 0;
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
                    bufferSize: 64 * 1024, leaveOpen: true);
                return CallNormalizedVsmParser(reader);
            }
            return CallParser(pointer + preamble, checked((nuint)(length - preamble)), kind);
        }
        finally
        {
            view.SafeMemoryMappedViewHandle.ReleasePointer();
        }
    }
    private static bool VsmNeedsTranscoding(byte* pointer, long length, out int preamble)
    {
        preamble = 0;
        var prefix = new ReadOnlySpan<byte>(pointer, (int)Math.Min(length, 4));
        if (prefix.StartsWith(new byte[] { 0xff, 0xfe }) || prefix.StartsWith(new byte[] { 0xfe, 0xff }) ||
            prefix.StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) return true;
        if (prefix.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) preamble = 3;
        // 保留 File.ReadAllText 对非法 UTF-8 的替换行为，使 required 模式仍可读取此前接受的输入。
        var decoder = new UTF8Encoding(false, true).GetDecoder();
        const int chunkSize = 64 * 1024;
        // GetCharCount 不推进 Decoder 状态，GetChars 须跨块保留不完整的 UTF-8 序列；为前一块开头的字符多留两个 UTF-16 单元。
        char[] decoded = new char[chunkSize + 2];
        try
        {
            for (long offset = preamble; offset < length;)
            {
                int count = (int)Math.Min(chunkSize, length - offset);
                var bytes = new ReadOnlySpan<byte>(pointer + offset, count);
                decoder.GetChars(bytes, decoded, flush: offset + count == length);
                int position = 0;
                while (position < count)
                {
                    int next = bytes[position..].IndexOf((byte)'\r');
                    if (next < 0) break;
                    position += next;
                    if (offset + position + 1 == length || pointer[offset + position + 1] != '\n') return true;
                    position++;
                }
                offset += count;
            }
        }
        catch (DecoderFallbackException)
        {
            return true;
        }
        return false;
    }
    private static NativePacketStream CallNormalizedVsmParser(TextReader reader)
    {
        // UTF-16、UTF-32、非法 UTF-8 和仅 CR 换行的源文件沿用旧文本路径的解码及换行规则。通过临时 UTF-8 文件限制内存，同时保留 required 与 verify 行为。
        using var temporary = CreateInputTemporary();
        using (var writer = new StreamWriter(temporary, new UTF8Encoding(false), 64 * 1024, leaveOpen: true))
        {
            char[] buffer = new char[64 * 1024];
            bool previousCr = false;
            int count;
            while ((count = reader.Read(buffer, 0, buffer.Length)) != 0)
            {
                int start = 0;
                for (int i = 0; i < count; i++)
                {
                    char character = buffer[i];
                    if (character == '\r' || character == '\n' && previousCr)
                    {
                        writer.Write(buffer, start, i - start);
                        if (character == '\r') writer.Write('\n');
                        start = i + 1;
                    }
                    previousCr = character == '\r';
                }
                writer.Write(buffer, start, count - start);
            }
        }
        temporary.Position = 0;
        return CallFileParser(temporary, 2, normalizeVsm: false);
    }
    private static NativePacketStream CallVsmTextParser(string text)
    {
        if (text.Length <= MappedInputThreshold)
            return CallParser(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n").Replace('\r', '\n')), 2);
        using var reader = new StringReader(text);
        return CallNormalizedVsmParser(reader);
    }
    private static FileStream CreateInputTemporary()
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            BufferSize = 64 * 1024,
            Options = FileOptions.DeleteOnClose
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(Path.Combine(Path.GetTempPath(), "kg-chart-input-" + Guid.NewGuid().ToString("N") + ".tmp"), options);
    }

    internal static bool TryLoadVsc(string path, out Chart? result)
    {
        result = null;
        if (!Enabled) return false;
        try
        {
            var timer = Stopwatch.StartNew();
            var chart = DecodeChart(CallFileParser(path, 0), Path.GetFileNameWithoutExtension(path), path);
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
            or InvalidDataException or IOException or ArgumentException or OverflowException or PlatformNotSupportedException)
        {
            Report(e, "VSC");
            return false;
        }
    }

    internal static bool TryLoadVsb(string path, string? definitionPath, string profile, out Chart? result)
    {
        result = null;
        if (!Enabled) return false;
        try
        {
            var timer = Stopwatch.StartNew();
            var chart = DecodeChart(CallFileParser(path, 1), Path.GetFileNameWithoutExtension(path));
            // 二进制中的对象专属 mod 使用数值 ID；Rust 解析数据后仍按原目录顺序解释扩展编号。
            var extras = GimmickCatalog.ReadExtraMods(chart.ObjectName, Path.GetDirectoryName(path), definitionPath, profile);
            for (int i = 0; i < chart.Mods.Count; i++)
            {
                var m = chart.Mods[i];
                string prefix = chart.ObjectName + ":mod_";
                if (!m.Name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (!int.TryParse(m.Name.AsSpan(prefix.Length), out int id)) continue;
                string name = ModCatalog.Decode(id, chart.ObjectName, extras);
                if (name != m.Name) chart.Mods[i] = m with { Name = name };
            }
            if (Verify)
            {
                var reference = VsbReader.LoadOriginal(path, definitionPath, profile);
                if (!EquivalentNotes(chart, reference) || !EquivalentMods(chart, reference))
                    throw new InvalidDataException("VSB note/mod/proxy mismatch against C# reference.");
            }
            result = chart;
            System.Threading.Interlocked.Increment(ref nativeVsbHits);
            Console.Error.WriteLine($"[RustEngine] VSB parsed: {chart.Notes.Count} notes, {timer.Elapsed.TotalMilliseconds:F2} ms ({Mode}).");
            return true;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
            or InvalidDataException or IOException or ArgumentException or OverflowException or PlatformNotSupportedException)
        {
            Report(e, "VSB");
            return false;
        }
    }

    internal static bool TryApplyVsm(Chart destination, string text, string source)
        => TryApplyVsmCore(destination, source, () => CallVsmTextParser(text),
            reference => VsmReader.ReplaceModsTextOriginal(reference, text, source));

    internal static bool TryApplyVsmFile(Chart destination, string path)
        => TryApplyVsmCore(destination, path, () => CallFileParser(path, 2),
            reference => VsmReader.ReplaceModsOriginal(reference, path));

    private static bool TryApplyVsmCore(Chart destination, string source, Func<NativePacketStream> parse,
        Action<Chart> readReference)
    {
        if (!Enabled) return false;
        try
        {
            var timer = Stopwatch.StartNew();
            var parsed = DecodeChart(parse(), "VSM", source);
            if (Verify)
            {
                var reference = new Chart
                {
                    ObjectName = destination.ObjectName,
                    Proxies = destination.Proxies,
                    ProxyCountDeclared = destination.ProxyCountDeclared
                };
                readReference(reference);
                var converted = new Chart
                {
                    ObjectName = destination.ObjectName,
                    Proxies = destination.Proxies,
                    ProxyCountDeclared = destination.ProxyCountDeclared
                };
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
            or InvalidDataException or IOException or ArgumentException or OverflowException or PlatformNotSupportedException)
        {
            Report(e, "VSM");
            return false;
        }
    }
    private static void ApplyVsm(Chart target, Chart parsed)
    {
        target.Mods.Clear();
        target.Mods.AddRange(parsed.Mods);
        target.PerFrame.Clear();
        target.PerFrame.AddRange(parsed.PerFrame);
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
        (double.IsFinite(a) && double.IsFinite(b) && Math.Abs(a - b) <= 1e-10 * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b))));
    private static bool EquivalentNotes(Chart a, Chart b)
    {
        if (a.VscDialect != b.VscDialect || a.Notes.Count != b.Notes.Count ||
            a.Diagnostics.Count != b.Diagnostics.Count) return false;
        for (int i = 0; i < a.Diagnostics.Count; i++)
        {
            var x = a.Diagnostics[i];
            var y = b.Diagnostics[i];
            if (x.Source != y.Source || x.Line != y.Line ||
                x.Message != y.Message || x.Error != y.Error) return false;
        }
        for (int i = 0; i < a.Notes.Count; i++)
        {
            var x = a.Notes[i];
            var y = b.Notes[i];
            if (!Near(x.Time, y.Time) || !Near(x.End, y.End) || x.Type != y.Type || x.Lane != y.Lane ||
                x.Extra.Count != y.Extra.Count || (x.ModExtra?.Count ?? 0) != (y.ModExtra?.Count ?? 0) || (x.ModExtra is null) != (y.ModExtra is null)) return false;
            foreach (var kv in x.Extra)
            {
                if (!y.Extra.TryGetValue(kv.Key, out var value) || kv.Value.GetType() != value.GetType()) return false;
                if (kv.Value is string s)
                {
                    if (value is not string v || s != v) return false;
                }
                else if (kv.Value is bool boolValue)
                {
                    if (value is not bool v || boolValue != v) return false;
                }
                else if (!Near(Convert.ToDouble(kv.Value), Convert.ToDouble(value))) return false;
            }
            if (x.ModExtra is { } mods)
            {
                if (y.ModExtra is null || !mods.SequenceEqual(y.ModExtra)) return false;
            }
        }
        return true;
    }
    private static bool EquivalentMods(Chart a, Chart b)
    {
        if (b.Diagnostics.Any(d => d.Error) || !a.Diagnostics.SequenceEqual(b.Diagnostics) ||
            a.Mods.Count != b.Mods.Count || a.PerFrame.Count != b.PerFrame.Count ||
            a.ObjectName != b.ObjectName || a.Proxies != b.Proxies || a.ProxyCountDeclared != b.ProxyCountDeclared ||
            a.Metadata.Count != b.Metadata.Count) return false;
        if (!a.Metadata.SequenceEqual(b.Metadata)) return false;
        for (int i = 0; i < a.Mods.Count; i++)
        {
            var x = a.Mods[i];
            var y = b.Mods[i];
            if (!Near(x.Beat, y.Beat) || !Near(x.Duration, y.Duration) || !Near(x.From, y.From) || !Near(x.To, y.To) ||
                x.Ease != y.Ease || x.Name != y.Name || x.Proxy != y.Proxy || x.Order != y.Order || x.SourceLine != y.SourceLine) return false;
        }
        for (int i = 0; i < a.PerFrame.Count; i++)
        {
            var x = a.PerFrame[i];
            var y = b.PerFrame[i];
            if (!Near(x.StartBeat, y.StartBeat) || !Near(x.EndBeat, y.EndBeat) || x.Function != y.Function) return false;
        }
        return true;
    }

    // COMPAT: KGS2 增加非致命源诊断，同时接受旧原生库输出的 KGS1。
    private static Chart DecodeChart(Stream stream, string title, string? sourcePath = null, bool nested = false)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, nested);
        string wireVersion = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (wireVersion == "KGR2" && !nested)
        {
            var records = DecodeChart(stream, title, sourcePath, nested: true);
            var patterns = records.Notes.ToArray();
            int count = ReadCount(reader, int.MaxValue), groups = ReadCount(reader, int.MaxValue);
            if ((long)groups * 12 != stream.Length - stream.Position ||
                !ChartLoadLimits.Unlimited && (long)count * 4 > MaxPacketBytes)
                throw new InvalidDataException("Invalid repeated VSC packet size.");
            records.Notes.Clear();
            records.Notes.EnsureCapacity(count);
            CollectionsMarshal.SetCount(records.Notes, count);
            var destination = CollectionsMarshal.AsSpan(records.Notes);
            int written = 0;
            double previous = double.NegativeInfinity;
            for (int g = 0; g < groups; g++)
            {
                int start = ReadCount(reader, patterns.Length), length = ReadCount(reader, patterns.Length),
                    repeats = ReadCount(reader, int.MaxValue);
                int expanded = checked(length * repeats);
                if (length == 0 || repeats == 0 || (long)start + length > patterns.Length || expanded > count - written)
                    throw new InvalidDataException("Invalid repeated VSC group.");
                double time = patterns[start].Time;
                if (time < previous) throw new InvalidDataException("Unsorted repeated VSC groups.");
                for (int i = start; i < start + length; i++)
                    if (patterns[i].Time != time) throw new InvalidDataException("Repeated VSC group crosses timestamps.");
                var target = destination.Slice(written, expanded);
                if (length == 1) target.Fill(patterns[start]);
                else
                {
                    patterns.AsSpan(start, length).CopyTo(target);
                    // 倍增复制保留完整的同刻记录次序，避免逐次重新读取、装箱和扩容。
                    for (int filled = length; filled < expanded;)
                    {
                        int copy = Math.Min(filled, expanded - filled);
                        target[..copy].CopyTo(target[filled..]);
                        filled += copy;
                    }
                }
                written += expanded;
                previous = time;
            }
            if (written != count) throw new InvalidDataException("Repeated VSC count mismatch.");
            return records;
        }
        if (wireVersion == "KGR1" && !nested)
        {
            var records = DecodeChart(stream, title, sourcePath, nested: true);
            var unique = records.Notes.ToArray();
            int count = ReadCount(reader, int.MaxValue);
            if (count != (stream.Length - stream.Position) / 4 || (stream.Length - stream.Position) % 4 != 0)
                throw new InvalidDataException("Invalid compact VSC reference count.");
            records.Notes.Clear();
            records.Notes.EnsureCapacity(count);
            double previous = double.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                uint id = reader.ReadUInt32();
                if (id >= unique.Length) throw new InvalidDataException("Invalid compact VSC record ID.");
                var note = unique[id];
                if (note.Time < previous) throw new InvalidDataException("Unsorted compact VSC references.");
                records.Notes.Add(note);
                previous = note.Time;
            }
            return records;
        }
        if (wireVersion is not ("KGS1" or "KGS2"))
            throw new InvalidDataException("RustBridge chart wire version mismatch.");
        var chart = new Chart
        {
            Title = title,
            VscDialect = reader.ReadByte() != 0 ? VscDialect.Csm340 : VscDialect.Legacy,
            ProxyCountDeclared = reader.ReadByte() != 0,
            Proxies = reader.ReadByte(),
            ObjectName = ReadText(reader)
        };
        int meta = ReadCount(reader, ChartLoadLimits.CountLimit(100_000));
        for (int i = 0; i < meta; i++) chart.Metadata[ReadText(reader)] = ReadText(reader);
        // 按包的剩余字节校验条数。每条音符至少占 33 字节，含时间、类型、轨道、结束、扩展数与 modExtra 标记或数量；常规模式另有 512 MiB 包上限。
        int notes = ReadCount(reader, int.MaxValue);
        if (notes > (stream.Length - stream.Position) / 33)
            throw new InvalidDataException("RustBridge note count exceeds packet contents.");
        chart.Notes.EnsureCapacity(notes);
        for (int i = 0; i < notes; i++)
        {
            double time = reader.ReadDouble();
            int kind = reader.ReadInt32();
            int lane = reader.ReadInt32();
            double end = reader.ReadDouble();
            int xcount = ReadCount(reader, 256);
            Dictionary<int, object>? extras = xcount == 0 ? null : new(xcount);
            for (int x = 0; x < xcount; x++)
            {
                int key = reader.ReadByte();
                int tag = reader.ReadByte();
                extras![key] = tag switch
                {
                    0 => (object)reader.ReadDouble(),
                    1 => ReadText(reader),
                    2 => reader.ReadByte() != 0,
                    3 => checked((byte)reader.ReadDouble()),
                    4 => checked((sbyte)reader.ReadDouble()),
                    5 => checked((uint)reader.ReadDouble()),
                    6 => checked((int)reader.ReadDouble()),
                    _ => throw new InvalidDataException("Invalid Rust extra type.")
                };
            }
            bool hasModExtra = reader.ReadByte() != 0;
            int mcount = ReadCount(reader, ChartLoadLimits.CountLimit(100_000));
            Dictionary<string, string?>? modExtra = hasModExtra ? new Dictionary<string, string?>(StringComparer.Ordinal) : null;
            if (!hasModExtra && mcount > 0) throw new InvalidDataException("Invalid Rust modExtra presence flag.");
            for (int m = 0; m < mcount; m++)
            {
                string key = ReadText(reader);
                modExtra![key] = reader.ReadByte() != 0 ? ReadText(reader) : null;
            }
            chart.Notes.Add(new(time, kind, lane, end, extras ?? Note.EmptyExtra, modExtra));
        }
        int events = ReadCount(reader, ChartLoadLimits.CountLimit((int)MaxEvents));
        if (events > (stream.Length - stream.Position) / 52)
            throw new InvalidDataException("RustBridge event count exceeds packet contents.");
        chart.Mods.EnsureCapacity(events);
        // 只在单次加载内共享重复事件名；限制缓存容量，避免大量唯一名称撑大缓存。
        var symbols = new Dictionary<string, string>(StringComparer.Ordinal);
        string Symbol(string text)
        {
            if (text.Length > 256) return text;
            if (symbols.TryGetValue(text, out var existing)) return existing;
            if (symbols.Count < 4096) symbols.Add(text, text);
            return text;
        }
        for (int i = 0; i < events; i++)
        {
            double beat = reader.ReadDouble(), duration = reader.ReadDouble(), from = reader.ReadDouble(), to = reader.ReadDouble();
            int proxy = reader.ReadInt32(), order = ReadCount(reader, ChartLoadLimits.CountLimit((int)MaxEvents)), line = ReadCount(reader, int.MaxValue);
            string ease = Symbol(ReadText(reader)), name = Symbol(ReadText(reader));
            chart.Mods.Add(new(beat, duration, ease, from, to, name, proxy, order, line));
        }
        int frames = ReadCount(reader, ChartLoadLimits.CountLimit((int)MaxEvents));
        if (frames > (stream.Length - stream.Position) / 20)
            throw new InvalidDataException("RustBridge frame count exceeds packet contents.");
        chart.PerFrame.EnsureCapacity(frames);
        for (int i = 0; i < frames; i++) chart.PerFrame.Add(new(reader.ReadDouble(), reader.ReadDouble(), ReadText(reader)));
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
        if (!nested && stream.Position != stream.Length) throw new InvalidDataException("Trailing bytes in Rust chart packet.");
        return chart;
    }
    private static int ReadCount(BinaryReader r, int limit)
    {
        uint n = r.ReadUInt32();
        if (n > (uint)limit) throw new InvalidDataException("RustBridge count exceeds limit.");
        return (int)n;
    }
    private static string ReadText(BinaryReader r)
    {
        int n = ReadCount(r, ChartLoadLimits.CountLimit(64 * 1024 * 1024));
        if (n > r.BaseStream.Length - r.BaseStream.Position) throw new EndOfStreamException("Truncated native string.");
        if (r.BaseStream is UnmanagedMemoryStream memory)
        {
            var bytes = new ReadOnlySpan<byte>(memory.PositionPointer, n);
            string text = Encoding.UTF8.GetString(bytes);
            memory.Position += n;
            return text;
        }
        if (n <= 256)
        {
            Span<byte> bytes = stackalloc byte[n];
            r.BaseStream.ReadExactly(bytes);
            return Encoding.UTF8.GetString(bytes);
        }
        return Encoding.UTF8.GetString(r.ReadBytes(n));
    }

    internal static bool TryBuildBpm(Chart chart, double baseBpm, double offsetMs, double? fixedBpm, out List<BpmMap.Segment>? result)
    {
        result = null;
        if (!Enabled) return false;
        try
        {
            var changes = chart.Notes.Where(n => n.Type == 3 && n.Extra.ContainsKey(1))
                .Select(n => new BpmChange { Time = n.Time, Bpm = Convert.ToDouble(n.Extra[1]) }).ToArray();
            byte* packet = null;
            nuint length = 0;
            // SAFETY: 变速点数组在调用期间固定，传入的元素数取自数组长度。
            fixed (BpmChange* data = changes)
            {
                int rc = ChartLoadLimits.Unlimited
                    ? BuildBpmUnlimited(data, (nuint)changes.Length, baseBpm, offsetMs, fixedBpm ?? 0, fixedBpm.HasValue ? 1u : 0u, &packet, &length)
                    : BuildBpm(data, (nuint)changes.Length, baseBpm, offsetMs, fixedBpm ?? 0, fixedBpm.HasValue ? 1u : 0u, &packet, &length);
                using var stream = Receive(rc, packet, length);
                using var r = new BinaryReader(stream);
                if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "KGB1") throw new InvalidDataException("Rust BPM wire mismatch.");
                int count = ReadCount(r, int.MaxValue);
                if (count == 0) throw new InvalidDataException("Rust BPM segments empty.");
                if (count > (stream.Length - stream.Position) / 24)
                    throw new InvalidDataException("Rust BPM segment count exceeds packet contents.");
                var segments = new List<BpmMap.Segment>(count);
                for (int i = 0; i < count; i++) segments.Add(new(r.ReadDouble(), r.ReadDouble(), r.ReadDouble()));
                if (stream.Position != stream.Length) throw new InvalidDataException("Rust BPM trailing bytes.");
                if (Verify && !CompareBpm(changes, baseBpm, fixedBpm, segments))
                    throw new InvalidDataException("BPM segment mismatch against C# reference.");
                result = segments;
                System.Threading.Interlocked.Increment(ref nativeBpmHits);
                return true;
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
            or InvalidDataException or IOException or ArgumentException or OverflowException)
        {
            Report(e, "BPM");
            return false;
        }
    }
    /// <summary>实际加载原生库的无窗口集成检查，用于 verify 或 required 模式。</summary>
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

    private static bool CompareBpm(BpmChange[] changes, double bpm, double? fixedBpm, List<BpmMap.Segment> actual)
    {
        var expected = new List<BpmMap.Segment> { new(0, 0, fixedBpm ?? bpm) };
        if (fixedBpm is null)
            foreach (var n in changes)
            {
                if (n.Time < 0)
                {
                    expected[0] = new(0, 0, n.Bpm);
                    continue;
                }
                var prev = expected[^1];
                double beat = prev.Beat + (n.Time - prev.Time) * prev.Bpm / 60;
                if (n.Time == prev.Time) expected[^1] = new(n.Time, beat, n.Bpm);
                else expected.Add(new(n.Time, beat, n.Bpm));
            }
        if (expected.Count != actual.Count) return false;
        for (int i = 0; i < expected.Count; i++)
            if (!Near(expected[i].Time, actual[i].Time) || !Near(expected[i].Beat, actual[i].Beat) || !Near(expected[i].Bpm, actual[i].Bpm)) return false;
        return true;
    }
}
