using System.Text;
using KuroakiGimmick.Core;
using static KuroakiGimmick.Native.Sdl;

namespace KuroakiGimmick.Graphics;

// Versioned binary format avoids reflection-based JSON on the interpreter.
internal static class SwitchShaderPack
{
    const int Magic = 0x5053474b; // KGSP
    const int Version = 1;
    internal static string Key(string source, bool vertex)
    {
        // A cache identifier, not a security hash. Pure managed arithmetic avoids OpenSSL
        // imports on mono-nx. The full original source is verified on every load.
        ulong a = 14695981039346656037UL, b = 1099511628211UL;
        foreach (byte value in Encoding.UTF8.GetBytes((vertex ? "V\n" : "F\n") + source))
        {
            a = unchecked((a ^ value) * 1099511628211UL);
            b = unchecked((b ^ value) * 14029467366897019727UL);
            b = (b << 13) | (b >> 51);
        }
        return a.ToString("X16") + b.ToString("X16");
    }

    internal static ShaderCompiler.Compiled Load(string source, bool vertex)
    {
        string path = Path.Combine(Paths.Assets, "SwitchShaders", Key(source, vertex) + ".kgsp");
        if (!File.Exists(path))
            throw new InvalidOperationException($"Missing Switch shader {Path.GetFileName(path)}. Re-bake this source on desktop.");
        using var r = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
        if (r.ReadInt32() != Magic || r.ReadInt32() != Version || r.ReadBoolean() != vertex || r.ReadString() != source)
            throw new InvalidDataException("Stale or corrupt Switch shader pack: " + path);
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
        if (code.Length != length || length < 20 || length % 4 != 0 || BitConverter.ToUInt32(code) != 0x07230203 || r.BaseStream.Position != r.BaseStream.Length)
            throw new InvalidDataException("Invalid SPIR-V payload: " + path);
        return new(code, entry, uniforms, samplers, native, GpuShaderFormat.SpirV);
    }

    static int Count(BinaryReader r, int max)
    {
        int count = r.ReadInt32();
        return count is >= 0 && count <= max ? count : throw new InvalidDataException("Invalid shader pack length.");
    }

    static void Write(string directory, string source, bool vertex)
    {
        var compiled = ShaderCompiler.Compile(source, vertex, GpuShaderFormat.SpirV);
        using var w = new BinaryWriter(File.Create(Path.Combine(directory, Key(source, vertex) + ".kgsp")), Encoding.UTF8);
        w.Write(Magic); w.Write(Version); w.Write(vertex); w.Write(source); w.Write(compiled.Entry);
        w.Write(compiled.Uniforms.Length);
        foreach (var u in compiled.Uniforms) { w.Write(u.Name); w.Write(u.Type); w.Write(u.Offset); }
        w.Write(compiled.Samplers.Length);
        foreach (var sampler in compiled.Samplers) w.Write(sampler);
        w.Write(compiled.NativeSource); w.Write(compiled.Code.Length); w.Write(compiled.Code);
    }

    internal static void Bake(string directory, IEnumerable<string> extraDirectories)
    {
#if KG_SWITCH || KG_MOBILE
        throw new PlatformNotSupportedException("Bake shaders with the desktop build.");
#else
        Directory.CreateDirectory(directory);
        var sources = new HashSet<(string Source, bool Vertex)>
        {
            (Canvas.VertexSource, true), (SceneRenderer.ProxyProjectionVertexSource, true),
            (Canvas.NoteInstanceVertexSource, true), (Canvas.NoteInstanceFragmentSource, false),
            (SceneRenderer.ProxyGuideVertexSource, true), (SceneRenderer.ProxyGuideFragmentSource, false),
            (Canvas.BasicFragmentSource, false), (SceneRenderer.CompositeFragmentSource, false),
            (CustomEffects.CombineFragmentSource, false), (CustomEffects.PosterizeFragmentSource, false)
        };
        // External resource packs use exactly the same AdaptGml transformation as the renderer.
        foreach (var root in new[] { Paths.Assets }.Concat(extraDirectories))
        {
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                if (Path.GetExtension(file).ToLowerInvariant() is ".frag" or ".fsh")
                    sources.Add((Shader.AdaptGml(File.ReadAllText(file)), false));
        }
        foreach (var (source, vertex) in sources) Write(directory, source, vertex);
        Console.WriteLine($"Baked {sources.Count} Switch shaders to {Path.GetFullPath(directory)}");
#endif
    }
}
