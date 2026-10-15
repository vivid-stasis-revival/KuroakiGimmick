using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

if (args.Length is < 3 or > 5) throw new ArgumentException("SwitchChecks KG_SWITCH.dll baked-shaders out-abi.json [fake-SDL-library] [ogg-audio]");
var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
// Persist twice to exercise an existing settings destination, then simulate
// interruption between preserving the previous settings and publishing the new.
var settingsType = assembly.GetType("KuroakiGimmick.Core.ViewerSettings")!;
string settingsDirectory = Path.Combine(Path.GetTempPath(), "kg-settings-" + Guid.NewGuid());
Directory.CreateDirectory(settingsDirectory);
try
{
    string settingsPath = Path.Combine(settingsDirectory, "settings.json");
    object settings = Activator.CreateInstance(settingsType)!;
    var persist = settingsType.GetMethod("Persist")!;
    var loadSettings = settingsType.GetMethod("Load")!;
    persist.Invoke(settings, [settingsPath]);
    settingsType.GetProperty("UiLanguage")!.SetValue(settings, "zh-CN");
    settingsType.GetProperty("UiTheme")!.SetValue(settings, "Kuroaki");
    settingsType.GetProperty("RecentProjects")!.SetValue(settings, new List<string> { settingsDirectory });
    persist.Invoke(settings, [settingsPath]);
    void VerifySettings(object loaded)
    {
        if ((string)settingsType.GetProperty("UiLanguage")!.GetValue(loaded)! != "zh-CN" ||
            (string)settingsType.GetProperty("UiTheme")!.GetValue(loaded)! != "Kuroaki" ||
            !((List<string>)settingsType.GetProperty("RecentProjects")!.GetValue(loaded)!).SequenceEqual([settingsDirectory]))
            throw new Exception("Settings or recents did not survive reopen");
    }
    VerifySettings(loadSettings.Invoke(null, [settingsPath])!);
    File.Move(settingsPath, settingsPath + ".previous");
    VerifySettings(loadSettings.Invoke(null, [settingsPath])!);
    if (!File.Exists(settingsPath)) throw new Exception("Interrupted save recovery did not restore settings");
    Console.WriteLine("Validated settings updates, recents reopening, and interrupted-save recovery.");
}
finally { Directory.Delete(settingsDirectory, true); }

var sdl = assembly.GetType("KuroakiGimmick.Native.Sdl")!;
var abi = sdl.GetNestedTypes().Where(t => t.IsValueType && !t.IsEnum).ToDictionary(t => t.Name, t => new
{
    Size = Marshal.SizeOf(t),
    Fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance).ToDictionary(f => f.Name, f => (int)Marshal.OffsetOf(t, f.Name))
});
File.WriteAllText(args[2], JsonSerializer.Serialize(abi));
Console.WriteLine($"Recorded {abi.Count} managed ABI layouts.");
var shaderType = assembly.GetType("KuroakiGimmick.Graphics.SwitchShaderPack")!;
var load = shaderType.GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!;
var dest = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, "Assets", "SwitchShaders");
Directory.CreateDirectory(dest);
int count = 0;
foreach (var file in Directory.EnumerateFiles(args[1], "*.kgsp"))
{
    using var r = new BinaryReader(File.OpenRead(file));
    _ = r.ReadInt32(); _ = r.ReadInt32(); bool vertex = r.ReadBoolean(); string source = r.ReadString();
    string copy = Path.Combine(dest, Path.GetFileName(file));
    File.Copy(file, copy, true);
    load.Invoke(null, [source, vertex]);
    var original = File.ReadAllBytes(copy);
    // Truncation must be rejected, not passed to the graphics driver.
    File.WriteAllBytes(copy, original[..^1]);
    try { load.Invoke(null, [source, vertex]); throw new Exception("Truncated pack accepted"); }
    catch (TargetInvocationException e) when (e.InnerException is IOException or InvalidDataException) { }
    finally { File.WriteAllBytes(copy, original); }
    count++;
}
if (count < 6) throw new Exception("Expected six built-in shaders.");
try { load.Invoke(null, ["this shader is not baked", false]); throw new Exception("Missing pack accepted"); }
catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }
Console.WriteLine($"Validated {count} shader packs, truncation rejection and missing-shader diagnostic.");

var sha = assembly.GetType("KuroakiGimmick.Native.SwitchSha256")!;
var hashBytes = sha.GetMethod("HashData", [typeof(byte[])])!;
var hashStream = sha.GetMethod("HashData", [typeof(Stream)])!;
var incremental = assembly.GetType("KuroakiGimmick.Native.SwitchIncrementalHash")!;
var create = incremental.GetMethod("CreateHash")!;
var append = incremental.GetMethod("AppendData")!;
var finish = incremental.GetMethod("GetHashAndReset")!;
var rng = new Random(1771);
foreach (int length in new[] {0, 1, 3, 55, 56, 63, 64, 65, 119, 120, 127, 128, 4096, 1000000})
{
    byte[] data = new byte[length]; rng.NextBytes(data);
    byte[] expected = System.Security.Cryptography.SHA256.HashData(data);
    void Match(object? actual) { if (!expected.SequenceEqual((byte[])actual!)) throw new Exception("Managed SHA-256 mismatch at " + length); }
    Match(hashBytes.Invoke(null, [data]));
    using var stream = new MemoryStream(data); Match(hashStream.Invoke(null, [stream]));
    using var hash = (IDisposable)create.Invoke(null, [System.Security.Cryptography.HashAlgorithmName.SHA256])!;
    for (int at = 0; at < length; at += 37) append.Invoke(hash, [data, at, Math.Min(37, length - at)]);
    Match(finish.Invoke(hash, []));
}
Console.WriteLine("Validated managed SHA-256 against BCL: boundary sizes, byte array, stream and incremental chunks.");

if (args.Length >= 4)
{
    nint library = NativeLibrary.Load(Path.GetFullPath(args[3]));
    NativeLibrary.SetDllImportResolver(assembly, (name, _, _) => name == "SDL3" ? library : 0);
    var enable = Marshal.GetDelegateForFunctionPointer<NativeVoid>(NativeLibrary.GetExport(library, "kg_test_allow_transfer"));
    var allocations = Marshal.GetDelegateForFunctionPointer<NativeInt>(NativeLibrary.GetExport(library, "kg_test_vertex_allocations"));
    var releasedOld = Marshal.GetDelegateForFunctionPointer<NativeInt>(NativeLibrary.GetExport(library, "kg_test_released_old"));
    var releasedNew = Marshal.GetDelegateForFunctionPointer<NativeInt>(NativeLibrary.GetExport(library, "kg_test_released_new"));
    var gpu = assembly.GetType("KuroakiGimmick.Graphics.GpuDevice")!;
    object instance = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(gpu);
    const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
    void Set(string name, object value) => gpu.GetField(name, fields)!.SetValue(instance, value);
    object Get(string name) => gpu.GetField(name, fields)!.GetValue(instance)!;
    foreach (string name in new[] { "batches", "vertices", "textures" })
        Set(name, Activator.CreateInstance(gpu.GetField(name, fields)!.FieldType)!);
    Set("<Handle>k__BackingField", (nint)1);
    Set("vertexBuffer", (nint)10); Set("uploadBuffer", (nint)11); Set("vertexCapacity", 64u);
    gpu.GetMethod("Begin", fields)!.Invoke(instance, [null, 1, 1, null]);
    ((System.Collections.IList)Get("vertices")).Add(1f);
    var ensure = gpu.GetMethod("EnsureVertices", fields)!;
    try { ensure.Invoke(instance, [128u]); throw new Exception("Injected transfer allocation failure was ignored"); }
    catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException failure)
    {
        if (!failure.Message.Contains("bytes=1048576") || !failure.Message.Contains("injected allocation failure"))
            throw new Exception("Allocation failure lost its size or native diagnostic", failure);
    }
    if ((nint)Get("vertexBuffer") != 10 || (nint)Get("uploadBuffer") != 11 || (uint)Get("vertexCapacity") != 64 ||
        releasedOld() != 0 || releasedNew() != 1 ||
        ((System.Collections.IList)Get("batches")).Count != 0 || ((System.Collections.IList)Get("vertices")).Count != 0)
        throw new Exception("Failed vertex growth corrupted buffers or left pending cleanup commands");
    enable(); ensure.Invoke(instance, [128u]);
    if ((uint)Get("vertexCapacity") != 1048576 || releasedOld() != 3 || allocations() != 2)
        throw new Exception("Retry failed to commit replacement buffers and release old buffers");
    ensure.Invoke(instance, [128u]);
    if (allocations() != 2) throw new Exception("Vertex buffer was unnecessarily reallocated");
    Console.WriteLine("Validated injected transfer allocation failure: original error preserved, pending commands discarded, old buffers retained, replacement released, retry and reuse passed.");

    var setOom = Marshal.GetDelegateForFunctionPointer<NativeMode>(NativeLibrary.GetExport(library, "kg_test_oom_mode"));
    var waits = Marshal.GetDelegateForFunctionPointer<NativeInt>(NativeLibrary.GetExport(library, "kg_test_wait_calls"));
    var transfers = Marshal.GetDelegateForFunctionPointer<NativeInt>(NativeLibrary.GetExport(library, "kg_test_transfer_calls"));
    var transfer = gpu.GetMethod("Transfer", fields)!;
    setOom(1);
    if ((nint)transfer.Invoke(instance, [128u, false])! == 0 || waits() != 1 || transfers() != 2)
        throw new Exception("Transient OOM did not recover with exactly one idle/drain/retry");
    setOom(2);
    try { transfer.Invoke(instance, [128u, false]); throw new Exception("Persistent OOM was ignored"); }
    catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException failure)
    {
        if (!failure.Message.Contains("VK_ERROR_OUT_OF_HOST_MEMORY")) throw;
    }
    if (waits() != 1 || transfers() != 2) throw new Exception("Persistent OOM retried more than once");
    gpu.GetMethod("AbortPendingCommands")!.Invoke(instance, []);
    // Canvas.Dispose can Flush again after a failure. Abort must reject that new batch.
    gpu.GetMethod("Begin", fields)!.Invoke(instance, [null, 1, 1, null]);
    if (((System.Collections.IList)Get("batches")).Count != 0)
        throw new Exception("Aborted device accepted a new cleanup-time render batch");
    gpu.GetMethod("Submit")!.Invoke(instance, [false, (nint)0]);
    Console.WriteLine("Validated transient/persistent OOM: one drain/retry; terminal abort rejects cleanup-time requeue and performs no GPU submission.");
}

if (args.Length == 5)
{
    var audioType = assembly.GetType("KuroakiGimmick.Core.AudioData")!;
    object audio = audioType.GetMethod("Decode")!.Invoke(null, [Path.GetFullPath(args[4])])!;
    var samples = (float[])audioType.GetProperty("Samples")!.GetValue(audio)!;
    int rate = (int)audioType.GetProperty("Rate")!.GetValue(audio)!;
    int channels = (int)audioType.GetProperty("Channels")!.GetValue(audio)!;
    double duration = (double)audioType.GetProperty("Duration")!.GetValue(audio)!;
    if (samples.Length == 0 || rate <= 0 || channels <= 0 || samples.Length % channels != 0 ||
        !samples.Any(s => Math.Abs(s) > 0.001f) || samples.Any(s => !float.IsFinite(s)))
        throw new Exception("Audio decode returned empty, silent or invalid PCM");
    Console.WriteLine($"Validated Switch-build Vorbis decoder on supplied song: {rate} Hz / {channels} channels / {duration:F3} seconds; non-silent finite PCM.");
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate void NativeVoid();
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int NativeInt();
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate void NativeMode(int mode);
