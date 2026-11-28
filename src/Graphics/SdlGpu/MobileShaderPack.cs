using KenomaGMK.SDL3;
using System.Text;
using KuroakiGimmick.Core;
using static KuroakiGimmick.Native.Sdl;

namespace KuroakiGimmick.Graphics;

internal static class MobileShaderPack
{
    internal static ShaderCompiler.Compiled Load(string source, bool vertex, GpuShaderFormat format)
    {
        string path = Path.Combine(Paths.Assets, "MobileShaders", SwitchShaderPack.Key(source, vertex) + ".kgms");
#if KG_MOBILE
        if (!File.Exists(path)) path = Path.Combine(KuroakiGimmick.Native.MobilePlatform.Root, "MobileShaders", SwitchShaderPack.Key(source, vertex) + ".kgms");
#endif
        if (!File.Exists(path)) throw new InvalidOperationException("Missing mobile shader " + Path.GetFileName(path) + ". Bake the song's shaders on desktop before importing it.");
        using var input = File.OpenRead(path);
        var compiled = ShaderPack.Read(input, ShaderPackKind.Mobile, source, vertex, (PackedShaderFormat)format);
        return new(compiled.Code, compiled.Entry, compiled.Uniforms, compiled.Samplers, compiled.NativeSource, format);
    }
    internal static void Bake(string platform, string directory, IEnumerable<string> extraDirectories)
    {
#if KG_MOBILE || KG_SWITCH
        throw new PlatformNotSupportedException("Bake mobile shaders with the desktop build.");
#else
        var format = platform switch
        {
            "android" or "portable" => GpuShaderFormat.SpirV,
            "ios" => GpuShaderFormat.Msl,
            _ => throw new ArgumentException("Shader platform must be android or ios.")
        };
        Directory.CreateDirectory(directory);
        var sources = new HashSet<(string Source, bool Vertex)>
        {
            (Canvas.VertexSource, true), (SceneRenderer.ProxyProjectionVertexSource, true),
            (Canvas.NoteInstanceVertexSource, true), (Canvas.NoteInstanceFragmentSource, false),
            (SceneRenderer.ProxyGuideVertexSource, true), (SceneRenderer.ProxyGuideFragmentSource, false),
            (CustomGimmickRenderer.FireworkCutoutSource, false), (CustomGimmickRenderer.FireworkCompositeSource, false),
            (NoteSkin.GreenleftStraightSource, false),
            (Canvas.BasicFragmentSource, false), (SceneRenderer.CompositeFragmentSource, false),
            (CustomEffects.CombineFragmentSource, false), (CustomEffects.PosterizeFragmentSource, false)
        };
        foreach (var root in new[] { Paths.Assets }.Concat(extraDirectories))
        {
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
            foreach (var file in GameMakerShader.EnumerateFragments(root))
                sources.Add((Shader.AdaptGml(File.ReadAllText(file)), false));
        }
        ShaderCompiler.TargetIosMsl = platform == "ios";
        try
        {
            foreach (var (source, vertex) in sources)
            {
                var compiled = ShaderCompiler.Compile(source, vertex, format);
                if (platform == "portable")
                {
                    using var file = File.Create(Path.Combine(directory,SwitchShaderPack.Key(source,vertex)+".ksp"));
                    PortableShader.Write(file,source,vertex,new(compiled.Code,compiled.Entry,compiled.Uniforms,compiled.Samplers,"",PackedShaderFormat.SpirV));
                    continue;
                }
                using var output = File.Create(Path.Combine(directory, SwitchShaderPack.Key(source, vertex) + ".kgms"));
                ShaderPack.Write(output, ShaderPackKind.Mobile, source, vertex,
                    new(compiled.Code, compiled.Entry, compiled.Uniforms, compiled.Samplers, compiled.NativeSource, (PackedShaderFormat)compiled.Format));
            }
        }
        finally
        {
            ShaderCompiler.TargetIosMsl = false;
        }
        Console.WriteLine($"Baked {sources.Count} {platform} shaders to {Path.GetFullPath(directory)}");
#endif
    }
}
