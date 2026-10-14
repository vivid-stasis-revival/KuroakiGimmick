using System.Numerics;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

/// <summary>
/// 通用对象渲染器：消费已验证的绘制命令和 shader 参数，不识别歌曲/对象名称。
/// Session 持有 CPU 数据，本类独占 GPU 对象；切换会话前必须先 Flush 再释放。
/// </summary>
public sealed partial class NativeGimmickRenderer : IDisposable
{
    private readonly Canvas canvas;
    private readonly Target background;
    private readonly Target filtered;
    private readonly Target mask;
    private NativeGimmickProfile? current;
    private GimmickDrawScheduler? scheduler;
    private readonly Dictionary<(string Path, bool Repeat, bool Linear), Texture> textures = [];
    private readonly Dictionary<string, Shader> shaders = new(StringComparer.Ordinal);
    private readonly HashSet<(string Path, bool Repeat, bool Linear)> failedTextures = [];
    private readonly HashSet<GimmickShaderMode> failedModes = [];
    private long textureBytes;
    private long imageClock;
    private readonly Dictionary<(string Path,bool Repeat,bool Linear),long> imageUse=[];
    private readonly Dictionary<Texture,int> imagePins=[];
    private void PinImage(Texture texture)=>imagePins[texture]=imagePins.GetValueOrDefault(texture)+1;
    private void UnpinImage(Texture texture)
    {
        int count=imagePins.GetValueOrDefault(texture);
        if(count<=1)imagePins.Remove(texture);else imagePins[texture]=count-1;
    }
    public NativeGimmickRenderer(Canvas canvas)
    {
        this.canvas = canvas;
        background = new(canvas.Gpu, 320, 180);
        filtered = new(canvas.Gpu, 320, 180);
        mask = new(canvas.Gpu, 320, 180);
    }

    /// <summary>先 Flush 再缩放两块离屏目标，避免仍在队列中的批次引用旧尺寸纹理。</summary>
    public void Resize(int width, int height)
    {
        canvas.Flush();
        background.Resize(width, height);
        filtered.Resize(width, height);
        // The GameMaker obj_starcrashers_mask Surface is ALWAYS 320x180.
        // Keep it 1 physical pixel per game pixel; final upscaling uses Target's
        // nearest sampler. Scaling this with RenderWidth breaks raster/blend parity.
    }

    /// <summary>
    /// 只有 Session 的 NativeGimmickProfile 身份变化才重建 GPU 资源：先 Flush，再 ReleaseSessionResources，最后重新编译 shader。
    /// 单个 shader 编译失败按组件写入诊断，其余资源仍然可用，不整体放弃本次会话。
    /// </summary>
    private void Prepare(Session session)
    {
        if (ReferenceEquals(current, session.NativeGimmick))
        {
            return;
        }
        canvas.Flush();
        ReleaseSessionResources();
        current = session.NativeGimmick;
        scheduler = current.Data == null ? null : new(current.Data);
        foreach (var (name, source) in current.Shaders)
        {
            try
            {
                shaders[name] = new(canvas.Gpu, Canvas.VertexSource, Shader.AdaptGml(source));
            }
            catch (InvalidOperationException exception)
            {
                current.Fail(name, "Shader compilation failed: " + exception.Message);
            }
        }
    }

    /// <summary>
    /// 采样方式属于纹理状态，因此缓存键包括 Repeat/Linear。
    /// 同一路径的噪声采样不能悄悄改变其它像素贴图的放大方式。
    /// 128 MiB 预算把同一路径的每个 sampler 变体分别计入；超限按资源错误处理，不静默降级。
    /// </summary>
    private Texture? Image(string name, int frame = 0, bool repeat = false, bool linear = false)
    {
        if (current == null)
        {
            return null;
        }
        string? path;
        if (current.Sprites.TryGetValue(name, out var sprite))
        {
            int index = ((frame % sprite.Frames.Count) + sprite.Frames.Count) % sprite.Frames.Count;
            path = sprite.Frames[index];
        }
        else
        {
            path = current.TextureFiles.GetValueOrDefault(name);
        }
        if (path == null)
        {
            return null;
        }
        var key = (path, repeat, linear);
        if (textures.TryGetValue(key, out var image))
        {
            imageUse[key]=++imageClock;
            return image;
        }
        if (failedTextures.Contains(key))
        {
            return null;
        }
        try
        {
            // 改变纹理分配/状态之前，先提交已排队的顶点。
            canvas.Flush();
            var size = new ImageResourceBudget().Check(path);
            long bytes = (long) size.Width * size.Height * 4;
            while (textureBytes + bytes > ResourceFiles.DecodedImageLimit)
            {
                (string Path,bool Repeat,bool Linear)? victim=null;
                long oldest=long.MaxValue;
                foreach(var entry in textures)
                    if(!imagePins.ContainsKey(entry.Value) && imageUse.GetValueOrDefault(entry.Key)<oldest)
                    {victim=entry.Key;oldest=imageUse.GetValueOrDefault(entry.Key);}
                if(victim==null)throw new InvalidDataException("Active object GPU images exceed the 128 MiB working-set budget.");
                var removed=textures[victim.Value];
                textureBytes-=(long)removed.Width*removed.Height*4;
                removed.Dispose();textures.Remove(victim.Value);imageUse.Remove(victim.Value);
            }
            image = Texture.Load(canvas.Gpu, path, repeat: repeat, linear: linear);
            textures.Add(key, image);
            imageUse[key]=++imageClock;
            textureBytes += bytes;
            return image;
        }
        catch (Exception exception) when (ResourceFiles.IsResourceError(exception))
        {
            failedTextures.Add(key);
            current.Fail(name, "Image decode failed: " + exception.Message);
            return null;
        }
    }

    /// <summary>背景画在自己的离屏目标上，按需过一遍图层 shader，最后才贴回调用方的 scene；这是本类唯一会切换渲染目标的入口。</summary>
    public void DrawBackground(Target scene, Session session, double time, bool effects)
    {
        Prepare(session);
        if (current?.Data?.Background is not { } layer)
        {
            return;
        }
        canvas.Begin(background, background.Texture.Width, background.Texture.Height, 320, 180, new(0, 0, 0));
        Draw(session, layer.Drawing, time, time, -1);
        canvas.Flush();
        Texture input = background.Texture;
        if (effects && layer.Effect is { } effect && ApplyShader(filtered, input, session, time, effect))
        {
            input = filtered.Texture;
        }
        canvas.Begin(scene, scene.Texture.Width, scene.Texture.Height, 320, 180);
        canvas.Quad(input, new(0, 0, 320, 180), Color.White);
        canvas.Flush();
    }

    /// <summary>绘制烘焙好的对象粒子。速度按原版 60 Hz 逻辑 tick 定义，与显示帧率无关；精灵原点来自 profile，不假定居中。</summary>
    public void DrawParticles(Session session, double time)
    {
        Prepare(session);
        if (current?.Data?.ParticleEmitter is not { } emitter)
        {
            return;
        }
        var particles = session.Timeline.ObjectParticles;
        for (int index = SortedSearch.LowerBound(particles, time - emitter.Lifetime, particle => particle.Time); index < particles.Count
            && particles[index].Time <= time; index++)
        {
            var particle = particles[index];
            if (time >= particle.Death)
            {
                continue;
            }
            double age = time - particle.Time;
            if (!current.Sprites.TryGetValue(emitter.Sprite, out var sprite) || Image(emitter.Sprite, particle.Frame) is not { } image)
            {
                continue;
            }
            canvas.Quad(image, new(particle.X + particle.Vx * (float) age * 60 - sprite.OriginX,
                particle.Y + particle.Vy * (float) age * 60 - sprite.OriginY, sprite.Width, sprite.Height), Color.Hsv(particle.Hue,
                particle.Saturation).Alpha(1 - age / particle.Life));
        }
    }

    /// <summary>调用方先选择离屏目标；阶段内不擅自切换场景/轨道/最终合成目标。</summary>
    public void DrawStage(Session session, double time, string stage, bool notes = true)
    {
        Prepare(session);
        if (scheduler == null)
        {
            return;
        }
        foreach (var command in scheduler.At(session.Timeline, time, stage, notes))
        {
            Draw(session, command.Drawing, command.Age, time, command.EventIndex);
        }
        canvas.Flush();
    }

    public void DrawGui(Session session, double time, bool notes = true, Action<GimmickTextOverlay>? drawText=null)
    {
        Prepare(session);
        if(scheduler!=null && current?.Data is { } definition)
        {
            var commands=scheduler.At(session.Timeline,time,GimmickStages.Gui,notes);
            var text=definition.TextOverlays.OrderByDescending(t=>t.Depth).ThenBy(t=>t.SortOrder).ToArray();
            int index=0;
            foreach(var command in commands)
            {
                while(index<text.Length && (text[index].Depth>command.Drawing.Depth || text[index].Depth==command.Drawing.Depth
                    && text[index].SortOrder<command.Drawing.SortOrder))
                {var overlay=text[index++];drawText?.Invoke(overlay);}
                Draw(session,command.Drawing,command.Age,time,command.EventIndex);
            }
            while(index<text.Length){var overlay=text[index++];drawText?.Invoke(overlay);}
        }
        canvas.Flush();
        DrawLastwishText(session, time);
    }
    private void Draw(Session session, GimmickDrawing drawing, double age, double time, int eventIndex)
    {
        if (current == null || !current.Sprites.TryGetValue(drawing.Sprite, out var sprite))
        {
            return;
        }
        // 先对帧号取模再转 int，长时间预览时才不会溢出。
        double frameValue = drawing.FrameMod == null
            ? drawing.Frame + Math.Floor(Math.Max(0, age) * drawing.FramesPerSecond)
            : session.Timeline.Get(drawing.FrameMod, time);
        if (drawing.Bindings.TryGetValue("frame", out var frameBinding)) frameValue = frameBinding.Value(session.Timeline, time);
        if(session.Chart.ObjectName=="obj_ram_gimmick" && drawing.Sprite=="sp_ram_plane")
        {
            double start=0;
            double reset=session.Timeline.Bpm.Time(152);
            if(time>reset)start=(Math.Floor(reset*60+1e-8)+1)/60.0;
            frameValue=Math.Clamp(Math.Floor((Math.Max(0,time-start)+1.0/60)*18-1e-8),0,60);
        }
        if (!double.IsFinite(frameValue)) return;
        int frame = (int)((frameValue % sprite.Frames.Count + sprite.Frames.Count) % sprite.Frames.Count);
        if (Image(drawing.Sprite, frame) is not { } texture)
        {
            return;
        }
        double V(string property) => drawing.Value(property, age, session.Timeline, time);
        double x = V("x"), y = V("y");
        double packedColor = drawing.Bindings.TryGetValue("color", out var colorBinding)
            ? colorBinding.Value(session.Timeline, time) : drawing.ColorBgr;
        if (!double.IsFinite(packedColor) || packedColor < 0 || packedColor > 0xFFFFFF) return;
        Color color = Color.GameMaker((uint)packedColor);
        if (drawing.Random is { } random)
        {
            uint seed = unchecked((uint) Math.Max(0, eventIndex) * random.SeedMultiplier + random.SeedOffset);
            double RandomCoordinate(uint value, double maximum) => random.IntegerCoordinates? Math.Min(maximum,
                MathF.Floor(Timeline.Hash(value) * (float)(maximum + 1))) : Timeline.Hash(value) * maximum;
            x += RandomCoordinate(seed, random.Width);
            y += RandomCoordinate(seed + 1, random.Height);
            if (random.RandomHue)
            {
                color = Color.Hsv(Timeline.Hash(seed + 2), 1);
            }
        }
        double alpha = V("alpha") * (drawing.AlphaMod == null ? 1 : session.Timeline.Get(drawing.AlphaMod, time));
        double scaleX = V("scaleX"), scaleY = V("scaleY"), angle = V("angle");
        if (!double.IsFinite(alpha) || alpha <= drawing.MinimumAlpha || !double.IsFinite(x) || !double.IsFinite(y)
            || !double.IsFinite(scaleX) || !double.IsFinite(scaleY) || !double.IsFinite(angle)
            || Math.Abs(x) > float.MaxValue || Math.Abs(y) > float.MaxValue
            || Math.Abs(scaleX) > float.MaxValue || Math.Abs(scaleY) > float.MaxValue)
        {
            return;
        }
        if(Math.Abs(scaleX)<=drawing.MinimumAbsScale || Math.Abs(scaleY)<=drawing.MinimumAbsScale)return;
        var transform = Matrix3x2.CreateTranslation(-sprite.OriginX, -sprite.OriginY) * Matrix3x2.CreateScale((float)scaleX,
            (float)scaleY) * Matrix3x2.CreateRotation((float)(-(angle % 360) * Math.PI / 180)) * Matrix3x2.CreateTranslation((float)x, (float)y);
        canvas.Polygon(texture, Vector2.Transform(new(0, 0), transform), Vector2.Transform(new(sprite.Width, 0), transform),
            Vector2.Transform(new(0, sprite.Height), transform), Vector2.Transform(new(sprite.Width, sprite.Height), transform), color.Alpha(alpha));
    }

    /// <summary>释放本会话的全部 GPU 对象并清空失败记录与调度器。调用方负责在此之前 Flush；本方法自身不提交。</summary>
    private void ReleaseSessionResources()
    {
        picturedTimeline = null;
        picturedObjects = null;
        plauditeTimeline = null;
        plauditeObjects = null;
        sceneStoryTimeline = null;
        sceneStoryState = null;
        sceneStoryTime = double.NaN;
        starcrashNoteMix?.Dispose();
        starcrashNoteMix = null;
        foreach (var image in textures.Values)
        {
            image.Dispose();
        }
        foreach (var shader in shaders.Values)
        {
            shader.Dispose();
        }
        textures.Clear();
        imageUse.Clear();imagePins.Clear();imageClock=0;
        shaders.Clear();
        failedTextures.Clear();
        failedModes.Clear();
        textureBytes = 0;
        scheduler = null;
        current = null;
    }

    /// <summary>先提交队列再释放：纹理与 shader 先于两块离屏目标销毁。</summary>
    public void Dispose()
    {
        canvas.Flush();
        ReleaseSessionResources();
        background.Dispose();
        filtered.Dispose();
        DisposeStarcrashersObjects();
        DisposePostTargets();
        DisposeStopmotionTargets();
        DisposeDesktopTargets();
        DisposeStopmotionTargets();
        mask.Dispose();
    }
}

