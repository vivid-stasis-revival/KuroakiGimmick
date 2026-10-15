using System.Text;
using KuroakiGimmick.Core;
using static KuroakiGimmick.Native.Sdl;

namespace KuroakiGimmick.Graphics;

internal static class MobileShaderPack
{
    const int Magic = 0x48534D4B; // KMSH
    const int Version = 1;
    internal static ShaderCompiler.Compiled Load(string source, bool vertex, GpuShaderFormat format)
    {
        string path = Path.Combine(Paths.Assets, "MobileShaders", SwitchShaderPack.Key(source, vertex) + ".kgms");
#if KG_MOBILE
        if (!File.Exists(path)) path = Path.Combine(KuroakiGimmick.Native.MobilePlatform.Root, "MobileShaders", SwitchShaderPack.Key(source, vertex) + ".kgms");
#endif
        if (!File.Exists(path)) throw new InvalidOperationException("Missing mobile shader " + Path.GetFileName(path) + ". Bake the song's shaders on desktop before importing it.");
        using var r = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
        if (r.ReadInt32() != Magic || r.ReadInt32() != Version || r.ReadUInt32() != (uint)format || r.ReadBoolean() != vertex || r.ReadString() != source)
            throw new InvalidDataException("Stale or incompatible mobile shader: " + path);
        string entry = r.ReadString();
        int count = Count(r, 1024);
        var uniforms = new ShaderCompiler.Uniform[count];
        for (int i = 0; i < count; i++) uniforms[i] = new(r.ReadString(), r.ReadString(), r.ReadInt32());
        count = Count(r, 1024);
        var samplers = new string[count];
        for (int i = 0; i < count; i++) samplers[i] = r.ReadString();
        string native = r.ReadString();
        int length = Count(r, 16 * 1024 * 1024);
        byte[] code = r.ReadBytes(length);
        bool valid = code.Length == length && length > 0 && r.BaseStream.Position == r.BaseStream.Length;
        if (format == GpuShaderFormat.SpirV) valid &= length >= 20 && length % 4 == 0 && BitConverter.ToUInt32(code) == 0x07230203;
        else if (format == GpuShaderFormat.Msl) valid &= code.Length > 0 && code[^1] == 0 && Encoding.UTF8.GetString(code) == native + "\0";
        else valid = false;
        if (!valid) throw new InvalidDataException("Invalid mobile shader payload: " + path);
        return new(code, entry, uniforms, samplers, native, format);
    }
    static int Count(BinaryReader r, int max)
    {
        int value = r.ReadInt32();
        return value >= 0 && value <= max ? value : throw new InvalidDataException("Invalid mobile shader length.");
    }
    internal static void Bake(string platform, string directory, IEnumerable<string> extraDirectories)
    {
#if KG_MOBILE || KG_SWITCH
        throw new PlatformNotSupportedException("Bake mobile shaders with the desktop build.");
#else
        var format = platform switch { "android" => GpuShaderFormat.SpirV, "ios" => GpuShaderFormat.Msl,
            _ => throw new ArgumentException("Shader platform must be android or ios.") };
        Directory.CreateDirectory(directory);
        var sources = new HashSet<(string Source, bool Vertex)>
        {
            (Canvas.VertexSource, true), (SceneRenderer.ProxyProjectionVertexSource, true),
            (Canvas.NoteInstanceVertexSource, true), (Canvas.NoteInstanceFragmentSource, false),
            (SceneRenderer.ProxyGuideVertexSource, true), (SceneRenderer.ProxyGuideFragmentSource, false),
            (Canvas.BasicFragmentSource, false), (SceneRenderer.CompositeFragmentSource, false),
            (CustomEffects.CombineFragmentSource, false), (CustomEffects.PosterizeFragmentSource, false)
        };
        foreach (var root in new[] { Paths.Assets }.Concat(extraDirectories))
        {
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                if (Path.GetExtension(file).ToLowerInvariant() is ".frag" or ".fsh")
                    sources.Add((Shader.AdaptGml(File.ReadAllText(file)), false));
        }
        ShaderCompiler.TargetIosMsl = platform == "ios";
        try
        {
            foreach (var (source, vertex) in sources)
            {
                var compiled = ShaderCompiler.Compile(source, vertex, format);
                using var w = new BinaryWriter(File.Create(Path.Combine(directory, SwitchShaderPack.Key(source, vertex) + ".kgms")), Encoding.UTF8);
                w.Write(Magic); w.Write(Version); w.Write((uint)format); w.Write(vertex); w.Write(source); w.Write(compiled.Entry);
                w.Write(compiled.Uniforms.Length);
                foreach (var u in compiled.Uniforms) { w.Write(u.Name); w.Write(u.Type); w.Write(u.Offset); }
                w.Write(compiled.Samplers.Length);
                foreach (var sampler in compiled.Samplers) w.Write(sampler);
                w.Write(compiled.NativeSource); w.Write(compiled.Code.Length); w.Write(compiled.Code);
            }
        }
        finally { ShaderCompiler.TargetIosMsl = false; }
        Console.WriteLine($"Baked {sources.Count} {platform} shaders to {Path.GetFullPath(directory)}");
#endif
    }
}
