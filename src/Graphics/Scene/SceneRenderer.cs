using System.Numerics;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

/// <summary>
/// 场景合成协调器。对象背景、资源图层、轨道、固定 HUD 与后处理按显式边界组织；预览和视频导出共用此入口。
/// </summary>
public sealed partial class SceneRenderer : IDisposable
{
    readonly Canvas canvas;
    readonly NoteSkin noteSkin;
    readonly Texture laneTexture, holdOverlay;
    /// <summary>dustTextures 是 pt_diamonddust 的四帧，gimmick 粒子与命中钻尘共用；noteParticles 是 sp_noteparticle 的五帧，下标即判定档位。</summary>
    readonly Texture[] dustTextures, noteParticles;
    readonly SceneFont sceneFont;
    readonly GameUiRenderer gameUi;
    /// <summary>游戏 HUD 渲染器。信息卡片复用它画原版难度徽章，避免两处各维护一套帧序。</summary>
    public GameUiRenderer GameUi => gameUi;
    readonly CheckerboardRenderer checker;
    readonly CustomEffects customEffects;
    readonly CustomGimmickRenderer customGimmicks;
    readonly NativeSequenceRenderer nativeSequence;
    readonly NativeGimmickRenderer nativeGimmick;
    /// <summary>proxyShader 施加 proxy 的位移/缩放/旋转/剪切；fieldComposite 把透明目标里的预乘 RGB 还原为直通 alpha。两者职责不可互换。</summary>
    readonly Shader proxyShader, projectiveProxyShader, fieldComposite;
    /// <summary>游玩轨道底边，320×180 逻辑空间的 y=165。音符在此裁剪，footer 位于其下方。</summary>
    const float TrackBottom = 165;
    /// <summary>scene 是不透明的合成底；field 是透明的轨道层，必须经 fieldComposite 反预乘后才能贴回 scene。</summary>
    readonly Target scene, field;
    /// <summary>后处理输出，同时是窗口 proxy 的主源。尺寸随 RenderWidth 变化，逻辑空间仍固定 320×180。</summary>
    public Target Final { get; }
    int RenderWidth => Final.Texture.Width;
    int RenderHeight => Final.Texture.Height;
    CustomImages? loadedImages;
    /// <summary>按路径缓存的图片纹理；textureLru 记录淘汰顺序，failedTextures 记录只报一次的失败路径，textureBytes 计量 128 MiB 预算。装载与淘汰见 SceneRenderer.Resources.cs。</summary>
    readonly Dictionary<string, Texture> customTextures = [];
    readonly LinkedList<string> textureLru = [];
    readonly HashSet<string> failedTextures = [];
    long textureBytes;
    // 复用的逐帧音符求值缓存。避免每帧重新分配 lane 数组。
    readonly double[] noteScrollCache = new double[8];
    readonly double[] noteAlphaCache = new double[8];
    readonly double[] noteXOffsetCache = new double[8];
    readonly double[] noteYOffsetCache = new double[8];
    readonly double[] noteBoostTimeCache = new double[8];
    readonly double[] noteBoostDistanceCache = new double[8];
    // slash_anycol 持续区间每帧只展开当前仍可见的实例；复用列表避免渲染循环反复分配。
    readonly List<(double Time, uint Seed, int Index)> slashInstanceCache = [];
    Texture? jacketTexture;
    string? jacketPath;
    public SceneRenderer(Canvas c)
    {
        canvas = c;
        var g = c.Gpu;
        noteSkin = new(c);
        sceneFont = new(c.Gpu);
        gameUi = new(c);
        checker = new(c);
        Texture Common(string name) => Texture.Load(g, Path.Combine(Paths.Assets, "GameCommon", name + ".png"));
        laneTexture = Common("sp_laneOverlay");
        holdOverlay = Common("sp_holdnote_overlay");
        dustTextures = Enumerable.Range(0, 4).Select(i => Common("pt_diamonddust_" + i)).ToArray();
        noteParticles = Enumerable.Range(0, 5).Select(i => Common("sp_noteparticle_" + i)).ToArray();
        proxyShader = new(g, Canvas.VertexSource, Shader.AdaptGml(File.ReadAllText(Path.Combine(Paths.Assets, "Shaders", "proxy.frag"))));
        projectiveProxyShader = new(g, ProxyProjectionVertexSource,
            Shader.AdaptGml(File.ReadAllText(Path.Combine(Paths.Assets, "Shaders", "proxy.frag"))));
        // 绘制进透明离屏目标存下的是预乘 RGB。在常规 source-alpha 混合之前先还原回直通 alpha，
        // 使 lane/音符的 alpha 只被乘一次。把它折进 Basic、或当成冗余删掉，都会让 alpha 被乘两次。
        fieldComposite = new(g, Canvas.VertexSource,
            "#version 330 core\nin vec2 v_vTexcoord;in vec4 v_vColour;uniform sampler2D gm_BaseTexture;out vec4 fragColor;void main(){vec4 c=texture(gm_BaseTexture,v_vTexcoord);if(c.a>0.)c.rgb/=c.a;fragColor=c*v_vColour;}");
        scene = new(g, 320, 180);
        field = new(g, 320, 180);
        Final = new(g, 320, 180);
        customEffects = new(canvas);
        customGimmicks = new(canvas);
        nativeGimmick = new(canvas);
        nativeSequence = new(canvas, nativeGimmick, customEffects);
    }

    /// <summary>
    /// 每帧只从给定 time 求值。逻辑空间固定 320×180，RenderWidth 仅改变离屏分辨率。
    /// 本方法内的书写顺序就是权威的合成顺序：BeforeRails、BeforePlayfield、FixedJudgment、Gui 四个阶段边界，
    /// 以及穿插其间的 DrawImages(min,max) 优先级窗口都是契约。新歌曲复用已有阶段，不增加曲名阶段；
    /// Base / Custom 组装完整 application surface 后统一变换 Proxy；特殊原生对象保留分段变换。
    /// 分辨率变化时先 Flush 再整批缩放全部目标，不允许只缩放其中一个。
    /// </summary>
    public void Render(Session session, double time, bool notes = true, bool effects = true)
    {
        int width = ViewerSettings.ValidWidth(session.Project.RenderWidth), height = width * 9 / 16;
        if (RenderWidth != width)
        {
            canvas.Flush();
            scene.Resize(width, height);
            field.Resize(width, height);
            Final.Resize(width, height);
            customEffects.Resize(width, height);
            nativeGimmick.Resize(width, height);
        }
        BeginWindowCapture(session);
        PrepareImages(session);
        var timeline = session.Timeline;
        double M(string n, int p = -1) => timeline.Get(n, time, p);
        // Base Draw_64 and Custom Draw_74 sample the gameplay application surface, but not every HUD object.
        // PAUSE / Escape / score belong to the sampled source; combo、判定与底部信息在 proxy 之后按各自层级补画。
        bool applicationProxy = session.Chart.ObjectName is "obj_base_gimmick" or "obj_custom_gimmick"
            && session.Chart.Proxies > 0 && session.Chart.Mods.Any(e => e.Proxy >= 0);
        canvas.Begin(scene, RenderWidth, RenderHeight, 320, 180, new Color(0, 0, 0));
        nativeGimmick.DrawBackground(scene, session, time, effects);
        nativeSequence.Backgrounds(scene, session, time, effects);
        nativeGimmick.DrawParticles(session, time);
        // 深度 801：白色底衬使深度 700 处以乘算绘制的 jacket 可见。
        if (session.DistortBgEnabled && M("ditortedBG_alp") > 0)
        {
            canvas.Fill(new(0, 0, 320, 180), Color.White.Alpha(M("ditortedBG_alp")));
        }
        checker.Draw(session, time, 0, scene, RenderWidth, RenderHeight);
        customGimmicks.DrawStars(session, time, true);
        DrawParticles(session, time);
        DrawJacket(session, time);
        if (session.NativeGimmick.Data?.BurstAfterJacket == true)
        {
            DrawParticles(session, time, burstsOnly: true);
        }
        if (effects && session.DistortBgEnabled)
        {
            customEffects.RenderBackground(scene, session, time);
        }
        canvas.Begin(scene, RenderWidth, RenderHeight, 320, 180);
        double previousPriority = double.NegativeInfinity;
        // 背景 FX 层按深度反号成图片优先级，并夹在 -301 之内；相邻两次 DrawImages 的区间首尾相接，
        // 不能留空隙也不能重叠，否则同一批图片会漏画或画两遍。
        foreach (var layer in session.Fx.Layers.Where(l => l.Name is "Effect_1" or "glow").OrderByDescending(l => l.Depth))
        {
            double priority = Math.Min(-301, -layer.Depth);
            DrawImages(session, time, previousPriority, priority);
            if (effects)
            {
                customEffects.RenderBackdrop(scene, session, time, layer.Name);
            }
            previousPriority = priority;
        }
        DrawImages(session, time, previousPriority, -301);
        checker.Draw(session, time, 1, scene, RenderWidth, RenderHeight);
        DrawImages(session, time, -301, -300);
        nativeGimmick.DrawStage(session, time, GimmickStages.BeforeRails, notes);
        DrawImages(session, time, -300, -260);
        customGimmicks.DrawSides(session, time);
        nativeSequence.Slashes(session, time);
        nativeGimmick.DrawStage(session, time, GimmickStages.BeforePlayfield, notes);
        DrawImages(session, time, -260, -255);
        DrawSlashes(session, time);
        DrawLoreleiSlashes(session, time);
        DrawImages(session, time, -255, -250);
        customGimmicks.DrawDf(session, time);
        DrawImages(session, time, -250, -200);
        canvas.Flush();
        // 特殊原生对象保留分段合成；Base / Custom 先组装完整画面再统一采样。
        bool proxyMode = !applicationProxy && session.Chart.Mods.Any(e => e.Proxy >= 0);
        DrawNotes(session, time, notes);
        CompositeField(session, time, proxyMode, clearFooter: session.NativeGimmick.Data?.ClearProxyFooter ?? true);
        // Base / Custom 只把 PAUSE / Escape / score 放进 application proxy。
        // 其它对象保持固定：非 application proxy 仍在这里按旧顺序绘制；application proxy 稍后补画。
        canvas.Begin(field, RenderWidth, RenderHeight, 320, 180, new(0, 0, 0, 0));
        gameUi.DrawProxyHud(session, time);
        if (!applicationProxy)
        {
            gameUi.DrawFixedHud(session, time);
            nativeSequence.Hud(session, time);
        }
        canvas.Flush();
        canvas.Begin(scene, RenderWidth, RenderHeight, 320, 180);
        canvas.Quad(field.Texture, new(0, 0, 320, 180), Color.White.Alpha(proxyMode ? 1 - M("hom") : 1), shader : fieldComposite);
        canvas.Flush();
        // 优先级 >= 1000 的图片可以同时盖住音符与固定 HUD。
        // 它们仍然走原本的 proxy 变换，不要把谱面美术（包括伪造的 PAUSE/标题图片）
        // 变成固定在屏幕上的覆盖层。
        if (!applicationProxy && session.Images.Items.Any(item => item.LayerPriority >= 1000))
        {
            canvas.Begin(field, RenderWidth, RenderHeight, 320, 180, new(0, 0, 0, 0));
            DrawImages(session, time, 1000, double.PositiveInfinity, field);
            canvas.Flush();
            CompositeField(session, time, proxyMode, clearFooter: false);
        }
        canvas.Begin(scene, RenderWidth, RenderHeight, 320, 180);
        canvas.Flush();
        // Custom 字幕已在 field 的 depth -10 绘制，不能在 cover 上方重复补画。
        if (session.Chart.ObjectName != "obj_custom_gimmick")
        {
            sceneFont.Draw(canvas, session, time);
        }
        canvas.Flush();
        if (applicationProxy)
        {
            // field 现在只包含允许随 proxy 移动的 application source。清空目标后重建固定侧边与 proxy，
            // 防止未变换的中央轨道泄漏出来。
            canvas.Pass(field, scene.Texture, canvas.Basic);
            canvas.Begin(scene, RenderWidth, RenderHeight, 320, 180, Color.Hex(0));
            CompositeField(session, time, true, clearFooter: true);

            // Combo / judgement 属于 Draw GUI，留到 Final；歌曲信息、难度与 sequence HUD 仍参加后处理，
            // 但不进入 proxy。保留原来 fixed HUD 对 hom 的淡出语义。
            canvas.Begin(scene, RenderWidth, RenderHeight, 320, 180);
            using (canvas.Opacity((float) Math.Clamp(1 - M("hom"), 0, 1)))
            {
                gameUi.DrawFixedHud(session, time);
                nativeSequence.Hud(session, time);
            }
            canvas.Flush();

            // 高优先级图片仍必须盖住固定 HUD，同时继续使用谱面自己的 proxy 变换。
            // 把这一层从 application snapshot 中拿出来单独合成，避免为了保留层级又把固定 HUD 捕获回去。
            if (session.Images.Items.Any(item => item.LayerPriority >= 1000))
            {
                canvas.Begin(field, RenderWidth, RenderHeight, 320, 180, new(0, 0, 0, 0));
                DrawImages(session, time, 1000, double.PositiveInfinity, field);
                canvas.Flush();
                CompositeField(session, time, true, clearFooter: false);
            }
        }
        // 到这里 scene 已包含 PAUSE / score、固定 HUD 与全部 VSP 图片，因此 fx_red 等公共后处理会覆盖它们；
        // combo / judgement 刻意留在 Final 之后的 GUI pass，不受 fx_red 影响。这个边界同时是 #9 / #11 的契约。
        // 后处理的回退优先级：关闭 effects 时只做一次 Basic 复制；对象有 post mode 时由对象链负责，
        // 并按 UseCommonPostProcessing 决定是否再套一层公共链；两者都没有时同样退回 Basic 复制。
        // 任何一条分支都必须把 scene 写进 Final，不能让 Final 留着上一帧内容。
        bool commonPost = session.NativeGimmick.Data?.UseCommonPostProcessing ?? true;
        if (!effects)
        {
            canvas.Pass(Final, scene.Texture, canvas.Basic);
        }
        else if (session.NativeGimmick.Data?.PostModes.Count > 0)
        {
            if (commonPost)
            {
                customEffects.Render(Final, scene.Texture, session, time, (output, input) => nativeGimmick.RenderPost(output, input, session, time));
            }
            else
            {
                nativeGimmick.RenderPost(Final, scene.Texture, session, time);
            }
        }
        else if (commonPost)
        {
            customEffects.Render(Final, scene.Texture, session, time);
        }
        else
        {
            canvas.Pass(Final, scene.Texture, canvas.Basic);
        }
        canvas.Begin(Final, RenderWidth, RenderHeight, 320, 180);
        // o_combodisplay / judgement 属于固定 GUI，不进入 application proxy，也不跟 uialpha 淡出。
        gameUi.DrawGuiHud(session, time);
        DrawComboParticles(session, time);
        if (session.NativeGimmick.Data != null)
        {
            nativeGimmick.DrawGui(session, time, notes);
            nativeSequence.Gui(session, time);
            gameUi.DrawSequenceStory(session, time);
            nativeSequence.EndFade(session, time);
        }
        // 谱面内剧情不依赖原生 gimmick：任何自定义曲写了 custom_episode 都能触发，所以在门控之外。
        gameUi.DrawEpisodeStory(session, time);
        if (M("wflash") > 0)
        {
            canvas.Fill(new(0, 0, 320, 180), Color.White.Alpha(M("wflash")));
        }
        canvas.Flush();
    }

    /// <summary>结果与除数同号的取模；负时间或负角度不会落到负区间。</summary>
    static double Mod(double n, double d) => (n % d + d) % d;
    /// <summary>二分定位第一个 key 不小于 time 的元素。依赖列表已按时间升序，这里不排序也不校验。</summary>
    static int LowerBound<T>(List<T> list, double time, Func<T, double> key)
    {
        int lo = 0, hi = list.Count;
        while (lo < hi)
        {
            int m = (lo + hi) / 2;
            if (key(list[m]) < time)
            {
                lo = m + 1;
            }
            else
            {
                hi = m;
            }
        }
        return lo;
    }

    /// <summary>
    /// 音符盖住哪几条 chip 轨（闭区间行号 0-3）。编辑器时间轴按这个把只读音符画成真实宽窄，
    /// 于是轨道里的音符和时间轴里的音符对同一个 type/lane 永远给出同一个宽度，不会各画各的。
    /// </summary>
    public (int First, int Last)? NoteLanes(int type, int lane) => noteSkin.Lanes(type, lane);

    /// <summary>按与构造相反的次序释放：子渲染器和纹理先于目标与 shader。调用前应确保引用这些资源的批次已经提交。</summary>
    public void Dispose()
    {
        jacketTexture?.Dispose();
        customEffects.Dispose();
        customGimmicks.Dispose();
        nativeSequence.Dispose();
        nativeGimmick.Dispose();
        checker.Dispose();
        gameUi.Dispose();
        sceneFont.Dispose();
        noteSkin.Dispose();
        laneTexture.Dispose();
        holdOverlay.Dispose();
        foreach (var texture in dustTextures.Concat(noteParticles))
        {
            texture.Dispose();
        }
        foreach (var texture in customTextures.Values)
        {
            texture.Dispose();
        }
        scene.Dispose();
        field.Dispose();
        Final.Dispose();
        windowField?.Dispose();
        proxyShader.Dispose();
        projectiveProxyShader.Dispose();
        fieldComposite.Dispose();
    }
}
