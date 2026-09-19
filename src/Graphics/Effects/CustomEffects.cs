using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

/// <summary>
/// 常规协议和房间滤镜的组合管线。双缓冲 ping-pong 防止同时读写同一离屏纹理，缺失参数不能用另一个滤镜伪装替代。
/// </summary>
public sealed class CustomEffects : IDisposable
{
    readonly Canvas canvas;
    /// <summary>房间与公共 FX 用到的全部 shader，构造时一次性加载，运行期不重建。</summary>
    readonly Shader main, glow, combine, water, posterize, roomPosterize, heat, largeBlur, colourise, hue, colourBalance, contrast, film, edge;
    /// <summary>glow 的两块模糊中转目标，按 i%2 交替使用；辉光累积另有 glowAccumA/B 一对，两对合起来才构成完整的 ping-pong。</summary>
    readonly Target[] blur;
    readonly Target combined, glowAccumA, glowAccumB, distorted, quantized;
    readonly Texture noise, waterNoise, blurNoise;
    /// <summary>后处理链与背景链各自的 ping-pong 目标对。Canvas.Pass 禁止同一纹理既作输入又作输出，把 A/B 合并成单个目标必然违规。</summary>
    readonly Target fxA, fxB, bgA, bgB;
    readonly Dictionary<string, Texture> roomTextures = [];
    GameFxProfile? loadedProfile;
    public CustomEffects(Canvas canvas)
    {
        this.canvas = canvas;
        var gpu = canvas.Gpu;
        Shader Load(string name) => new(gpu, Canvas.VertexSource, Shader.AdaptGml(File.ReadAllText(Path.Combine(Paths.Assets, "Shaders", name))));
        main = Load("custom.frag");
        film = Load("old-film.frag");
        edge = Load("edge.frag");
        glow = Load("disk-glow.frag");
        water = Load("water.frag");
        heat = Load("heathaze.frag");
        largeBlur = Load("large-blur.frag");
        colourise = Load("colourise.frag");
        hue = Load("hue.frag");
        colourBalance = Load("colour-balance.frag");
        contrast = Load("contrast.frag");
        roomPosterize = Load("room-posterize.frag");
        combine = new(gpu, Canvas.VertexSource,
            "#version 330 core\nin vec2 v_vTexcoord;uniform sampler2D gm_BaseTexture,blurred;uniform float intensity;out vec4 fragColor;void main(){fragColor=vec4(max(texture(gm_BaseTexture,v_vTexcoord).rgb,texture(blurred,v_vTexcoord).rgb*intensity),1);}");
        posterize = new(gpu, Canvas.VertexSource,
            "#version 330 core\nin vec2 v_vTexcoord;uniform sampler2D gm_BaseTexture;uniform float levels;out vec4 fragColor;void main(){vec4 c=texture(gm_BaseTexture,v_vTexcoord);float n=max(2.,levels);fragColor=vec4(floor(c.rgb*n)/n,c.a);}");
        blur = Enumerable.Range(0, 2).Select(_ => new Target(gpu, 320, 180)).ToArray();
        combined = new(gpu, 320, 180);
        glowAccumA = new(gpu, 320, 180);
        glowAccumB = new(gpu, 320, 180);
        distorted = new(gpu, 320, 180);
        quantized = new(gpu, 320, 180);
        // sp_noise2 缺失时退回到确定性的伪随机噪声：alpha 通道恒为 255，其余分量取自 Timeline.Hash，
        // 保证同一帧在不同机器上得到同样的像素，不使用运行时随机数。
        byte[] pixels = new byte[256 * 256 * 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = i % 4 == 3 ? (byte) 255 : (byte)(Timeline.Hash((uint) i + 371) * 255);
        }
        string noisePath = Path.Combine(Paths.Assets, "GameFX", "sp_noise2.png");
        noise = File.Exists(noisePath) ? Texture.Load(gpu, noisePath, repeat: true) : new(gpu, 256, 256, pixels, repeat: true);
        waterNoise = Texture.Load(gpu, Path.Combine(Paths.Assets, "GameFX", "_filter_underwater_noise_sprite.png"), repeat: true, linear: true);
        blurNoise = Texture.Load(gpu, Path.Combine(Paths.Assets, "GameFX", "_filter_large_blur_noise.png"), repeat: true);
        fxA = new(gpu, 320, 180);
        fxB = new(gpu, 320, 180);
        bgA = new(gpu, 320, 180);
        bgB = new(gpu, 320, 180);
    }

    /// <summary>绑定附加 sampler。单元 0 留给 gm_BaseTexture，附加纹理一律从 1 开始。</summary>
    void Bind(Texture texture, int unit)
    {
        canvas.Gpu.BindTexture(unit, texture);
    }

    /// <summary>先 Flush 再整批缩放所有中间目标；漏掉其中一个会让后面的 pass 读到尺寸不符的纹理。</summary>
    public void Resize(int width, int height)
    {
        canvas.Flush();
        foreach (var target in blur.Concat(new[]
        {
            combined,
            glowAccumA,
            glowAccumB,
            distorted,
            quantized,
            fxA,
            fxB,
            bgA,
            bgB
        }))
        {
            target.Resize(width, height);
        }
    }

    /// <summary>profile 身份变化才释放房间纹理；释放前必须 Flush，避免队列中的批次引用已销毁的纹理。</summary>
    void Prepare(Session session)
    {
        if (ReferenceEquals(loadedProfile, session.Fx))
        {
            return;
        }
        canvas.Flush();
        foreach (var texture in roomTextures.Values)
        {
            texture.Dispose();
        }
        roomTextures.Clear();
        loadedProfile = session.Fx;
    }

    /// <summary>房间滤镜的噪声纹理按路径缓存，一律 repeat + linear —— 滤镜按 UV 平铺采样，改成钳制会在边缘出现拉伸。</summary>
    Texture Noise(GameFxProfile.Layer layer)
    {
        string path = layer.TexturePath!;
        if (!roomTextures.TryGetValue(path, out var texture))
        {
            roomTextures[path] = texture = Texture.Load(canvas.Gpu, path, repeat: true, linear: true);
        }
        return texture;
    }

    static Color Rgba(double[] c) => new((float) c[0], (float) c[1], (float) c[2], (float) c[3]);
    public void RenderNativeLayer(Target output, Texture input, GameFxProfile.Layer layer, Session session, double time)
    { Prepare(session); RunLayer(output, input, layer, session, time); }
    /// <summary>
    /// 把一个房间图层跑成一遍 pass：_filter_* 名字决定 shader，同时补齐 GameMaker 的 gm_p* 配套 uniform。
    /// 未知的 filter 直接抛错，不用别的滤镜顶替。
    /// </summary>
    void RunLayer(Target output, Texture input, GameFxProfile.Layer layer, Session session, double time)
    {
        double M(string n) => session.Timeline.Get(n, time);
        bool custom = session.Chart.ObjectName == "obj_custom_gimmick";
        bool nativeFx = session.NativeGimmick.UseNativeColorControls;
        Shader shader = layer.Filter switch
        {
            "_filter_old_film" => film,
            "_filter_edgedetect" => edge,
            "_filter_heathaze" => heat,
            "_filter_underwater" => water,
            "_filter_colourise" => colourise,
            "_filter_hue" => hue,
            "_filter_colour_balance" => colourBalance,
            "_filter_contrast" => contrast,
            "_filter_posterise" => roomPosterize,
            _ => throw new InvalidDataException("Unknown source FX filter")
        };
        Texture? tex = layer.TexturePath == null ? null : Noise(layer);
        if (tex != null)
        {
            Bind(tex, 1);
        }
        canvas.Pass(output, input, shader, s =>
        {
            s.Vec2("gm_pSurfaceDimensions", 320, 180);
            s.Vec2("gm_pSurfaceTexelSize", 1.0 / 320, 1.0 / 180);
            // 房间图层按原版一律取 0；只有 Render 里那个独立的 underwater pass 取 1。两处不要“统一”成同一个值。
            s.Float("gm_pPreMultiplyAlpha", 0);
            s.Vec2("gm_pCamOffset", 0, 0);
            s.Float("gm_pTime", time);
            if (layer.Filter == "_filter_edgedetect") s.Float("g_Threshold", layer.Number("g_Threshold"));
            if (tex != null && layer.Filter == "_filter_old_film")
            {
                s.Int("g_OldFilmTexture", 1);
                s.Vec2("g_OldFilmTextureTexelSize", 1.0 / tex.Width, 1.0 / tex.Height);
                foreach (string name in GameFxProfile.FilmParameters)
                    s.Float(name, layer.Number(name) * (name is "g_OldFilmFlickerSpeed" or "g_OldFilmBarSpeed" ? time : 1));
            }
            else if (tex != null)
            {
                s.Int("g_DistortTexture", 1);
                s.Vec2("g_DistortTextureTexelSize", 1.0 / tex.Width, 1.0 / tex.Height);
                foreach (string n in new[]
                {
                    "g_Distort1Scale",
                    "g_Distort2Scale"
                })
                {
                    var v = layer.Vector(n, 2);
                    s.Vec2(n, layer.Name == "LBG" ? M("BG_ditortScale") : v[0], layer.Name == "LBG" ? M("BG_ditortScale") : v[1]);
                }
                foreach (string n in new[]
                {
                    "g_Distort1Speed",
                    "g_Distort2Speed"
                })
                {
                    s.Float(n, layer.Number(n) * time % 1);
                }
                s.Float("g_Distort1Amount",
                    layer.ParameterMods.TryGetValue("g_Distort1Amount", out var amountMod) ? M(amountMod) : layer.Name == "LBG" ? M("BG_ditortAmount") : layer.Name == "FX_underwater" ? M("fx_underwater") : layer.Number("g_Distort1Amount"));
                s.Float("g_Distort2Amount",
                    layer.Name == "LBG" ? M("BG_ditortAmount") : layer.Name == "FX_chroma" ? M("fx_chroma_distort") : layer.Name == "FX_underwater" ? M("fx_underwater") : layer.Number("g_Distort2Amount"));
                s.Float("g_ChromaSpreadAmount", layer.Number("g_ChromaSpreadAmount"));
                s.Float("g_CamOffsetScale", layer.Number("g_CamOffsetScale"));
                if (layer.Filter == "_filter_underwater")
                {
                    foreach (string n in new[]
                    {
                        "g_GlintCol",
                        "g_TintCol",
                        "g_AddCol"
                    })
                    {
                        s.Vec4(n, Rgba(layer.Vector(n, 4)));
                    }
                }
            }
            if (layer.Filter == "_filter_contrast")
            {
                foreach (string n in new[]
                {
                    "g_ContrastIntensity",
                    "g_ContrastBrightness"
                })
                {
                    s.Float(n, layer.Number(n));
                }
            }
            if (layer.Filter == "_filter_posterise")
            {
                s.Float("g_ColourLevels", Math.Max(.000001, M(custom ? "fx_posterize" : layer.ParameterMods.GetValueOrDefault("g_ColourLevels", "fx_posterize"))));
            }
            if (layer.Filter == "_filter_colourise")
            {
                s.Float("g_Intensity", layer.Name == "FX_contrast" ? 1 - M("fx_contrast") : layer.Name == "FX_red"
                    && custom ? M("fx_red_intensity") : layer.Number("g_Intensity"));
                s.Vec4("g_TintCol", layer.Name == "FX_red" && (custom
                    || session.NativeGimmick.Data?.UseRecolorTint == true) ? Color.Hsv((float) M("curcolor") / 255,
                    1) : Rgba(layer.Vector("g_TintCol", 4)));
            }
            if (layer.Filter == "_filter_hue")
            {
                s.Float("g_HueShift", layer.Name == "FX_hue" && (custom || nativeFx) ? M("fx_hue_hue") : layer.Number("g_HueShift"));
                s.Float("g_HueSaturation", layer.Name == "FX_hue" && (custom || nativeFx) ? M("fx_hue_saturation") : layer.Number("g_HueSaturation"));
            }
            if (layer.Filter == "_filter_colour_balance")
            {
                foreach (string n in new[]
                {
                    "g_ColourBalanceShadows",
                    "g_ColourBalanceMidtones",
                    "g_ColourBalanceHighlights"
                })
                {
                    var v = layer.Vector(n, 3);
                    s.Vec3(n, v[0], v[1], v[2]);
                }
            }
        });
    }

    public void RenderBackground(Target scene, Session session, double time)
    {
        Prepare(session);
        var t = session.Timeline;
        Texture input = scene.Texture;
        var layer = session.Fx.Find("LBG");
        if (layer is { Enabled: true, Visible: true } && t.Get("BG_ditortAmount", time) != 0 && t.Get("BG_ditortScale", time) > 0)
        {
            RunLayer(bgA, input, layer, session, time);
            input = bgA.Texture;
        }
        double radius = t.Get("BG_blurRadius", time);
        if (radius != 0)
        {
            Bind(blurNoise, 1);
            canvas.Pass(bgB, input, largeBlur, s =>
            {
                s.Int("g_NoiseTexture", 1);
                s.Vec2("g_NoiseTextureDimensions", blurNoise.Width, blurNoise.Height);
                s.Vec2("gm_pSurfaceDimensions", 320, 180);
                s.Float("g_Radius", radius);
            });
            input = bgB.Texture;
        }
        if (input != scene.Texture)
        {
            canvas.Pass(scene, input, canvas.Basic);
        }
    }

    // 原版 io_gamemaker_gm_effect_glow 的 layer_end：每遍采样上一遍的模糊结果，
    // 半径逐遍取反号，再以 MAX 合成回底图。
    // 两组 ping-pong 对支撑原版可配置的质量档（而不是一次绑定 5 张近似纹理）。
    // 我们的场景是不透明的，所以用 g_GlowAlpha 画自己的副本不会改变底图。
    void RunGlow(Target output, Texture input, GameFxProfile.Layer layer, double intensity)
    {
        if (intensity <= 0)
        {
            canvas.Pass(output, input, canvas.Basic);
            return;
        }
        int count = (int) layer.Number("g_GlowQuality");
        double mult = Math.Pow(layer.Number("g_GlowRadius"), 1.0 / count), radius = mult;
        double tint = Math.Floor(Math.Clamp(intensity, 0, 1) * 255) / 255;
        Texture previous = input, accumulated = input;
        for (int i = 0; i < count; i++)
        {
            var blurred = blur[i % 2];
            canvas.Pass(blurred, previous, glow, s =>
            {
                s.Float("g_GlowRadius", radius);
                s.Float("g_GlowGamma", layer.Number("g_GlowGamma"));
                s.Vec2("gm_pSurfaceTexelSize", 1.0 / 320, 1.0 / 180);
            });
            Bind(blurred.Texture, 1);
            var acc = i % 2 == 0 ? glowAccumA : glowAccumB;
            canvas.Pass(acc, accumulated, combine, s =>
            {
                s.Int("blurred", 1);
                s.Float("intensity", tint);
            });
            accumulated = acc.Texture;
            previous = blurred.Texture;
            // 半径逐遍取反号，与原版一致；去掉负号会让辉光偏向一侧。
            radius *= - mult;
        }
        canvas.Pass(output, accumulated, canvas.Basic);
    }

    // 背景类效果插在 profile 声明的深度处，位于棋盘格、游玩轨道、音符和 HUD 之前。
    public void RenderBackdrop(Target scene, Session session, double time, string name)
    {
        Prepare(session);
        Texture input = scene.Texture;
        foreach (var layer in session.Fx.Layers.Where(l => l.Name == name))
        {
            if (!layer.Visible || !layer.Enabled)
            {
                continue;
            }
            // 背景链同样 ping-pong：输出取当前输入之外的那一块，绝不能读写同一张纹理。
            var target = input == bgA.Texture? bgB : bgA;
            if (layer.Filter == "_effect_glow")
            {
                RunGlow(target, input, layer, session.Timeline.Get("fx_particleglow", time));
            }
            else
            {
                RunLayer(target, input, layer, session, time);
            }
            input = target.Texture;
        }
        if (input != scene.Texture)
        {
            canvas.Pass(scene, input, canvas.Basic);
        }
    }

    /// <summary>
    /// 公共后处理链。房间 profile 没提供对应图层时，才用内置的 underwater/posterize/FX_red/FX_hue 兜底。
    /// finalPass 非空时由调用方接管最后一遍，本方法不再绘制 main。
    /// </summary>
    public void Render(Target output, Texture input, Session session, double time, Action<Target, Texture>? finalPass = null)
    {
        Prepare(session);
        var timeline = session.Timeline;
        double M(string name) => timeline.Get(name, time);
        if (session.Fx.Find("FX_underwater") == null && Math.Abs(M("fx_underwater")) > .02)
        {
            Bind(waterNoise, 1);
            canvas.Pass(distorted, input, water, s =>
            {
                s.Int("g_DistortTexture", 1);
                s.Vec2("gm_pSurfaceDimensions", 320, 180);
                s.Vec2("gm_pSurfaceTexelSize", 1.0 / 320, 1.0 / 180);
                // 这遍独立的 underwater 是唯一取 1 的地方：它直接吃预乘的场景纹理。
                s.Float("gm_pPreMultiplyAlpha", 1);
                s.Vec2("gm_pCamOffset", 0, 0);
                s.Float("gm_pTime", time);
                s.Vec2("g_DistortTextureTexelSize", 1.0 / waterNoise.Width, 1.0 / waterNoise.Height);
                s.Vec2("g_Distort1Scale", 20, 20);
                s.Vec2("g_Distort2Scale", 100, 100);
                s.Float("g_Distort1Speed", time * .036 % 1);
                s.Float("g_Distort2Speed", time * .011 % 1);
                s.Float("g_Distort1Amount", M("fx_underwater"));
                s.Float("g_Distort2Amount", M("fx_underwater"));
                s.Float("g_ChromaSpreadAmount", 0);
                s.Float("g_CamOffsetScale", 1);
                s.Vec4("g_GlintCol", new(0, 0, 0));
                s.Vec4("g_TintCol", Color.White);
                s.Vec4("g_AddCol", new(0, 0, 0));
            });
            input = distorted.Texture;
        }
        if (session.Fx.Find("FX_posterize") == null && M("fx_posterize_vis") != 0)
        {
            canvas.Pass(quantized, input, posterize, s => s.Float("levels", M("fx_posterize")));
            input = quantized.Texture;
        }
        // ping-pong 的核心：输出永远取当前输入之外的那一块。Canvas.Pass 不允许一遍里同时读写同一张纹理，
        // 把 fxA/fxB 并成一个目标就会踩这条禁令。
        Target Next() => input == fxA.Texture? fxB : fxA;
        bool tintDrawn = false;
        // 染色插在深度 < -2400 的图层之前；整条链没有图层触发时，结尾再强制补一次，保证只画一遍。
        void Tint()
        {
            tintDrawn = true;
            if (!session.NonBaseFxEnabled || M("fx_colorise_intensity") == 0)
            {
                return;
            }
            var target = Next();
            canvas.Pass(target, input, colourise, s =>
            {
                s.Float("gm_pPreMultiplyAlpha", 0);
                s.Float("g_Intensity", M("fx_colorise_intensity"));
                s.Vec4("g_TintCol", Color.Hex((uint) Math.Clamp(M("fx_colorise_col_rgb"), 0, 16777215)).Alpha(M("fx_colorise_col_alpha")));
            });
            input = target.Texture;
        }
        foreach (var layer in session.Fx.Layers)
        {
            if (!layer.Enabled)
            {
                continue;
            }
            bool custom = session.Chart.ObjectName == "obj_custom_gimmick";
            bool active = RoomFxState.IsForegroundActive(layer, custom, session.NonBaseFxEnabled, session.NativeGimmick.UseNativeColorControls, M);
            if (!tintDrawn && layer.Depth < -2400)
            {
                Tint();
            }
            if (!active)
            {
                continue;
            }
            var target = Next();
            if (layer.Filter == "_effect_glow")
            {
                RunGlow(target, input, layer, M("fx_glow"));
            }
            else
            {
                RunLayer(target, input, layer, session, time);
            }
            input = target.Texture;
        }
        if (session.NonBaseFxEnabled && (session.Fx.Source == "none" || session.Fx.AutoFallback))
        {
            // 文档里的 FX_red 用的就是同一个 colourise 滤镜。外部房间 profile 会给出它真实的深度；
            // 没有 profile 时保持已报告的顺序。
            if (session.Fx.Find("FX_red") == null && M("fx_red") >= .5)
            {
                var target = Next();
                canvas.Pass(target, input, colourise, s =>
                {
                    s.Float("gm_pPreMultiplyAlpha", 0);
                    s.Float("g_Intensity", M("fx_red_intensity"));
                    s.Vec4("g_TintCol", Color.Hsv((float) M("curcolor") / 255, 1));
                });
                input = target.Texture;
            }
            if (session.Fx.Find("FX_hue") == null && (M("fx_hue_hue") != 0 || M("fx_hue_saturation") != 1))
            {
                var target = Next();
                canvas.Pass(target, input, hue, s =>
                {
                    s.Float("g_HueShift", M("fx_hue_hue"));
                    s.Float("g_HueSaturation", M("fx_hue_saturation"));
                });
                input = target.Texture;
            }
        }
        if (!tintDrawn)
        {
            Tint();
        }
        if (finalPass != null)
        {
            finalPass(output, input);
            return;
        }
        Bind(noise, 1);
        canvas.Pass(output, input, main, s =>
        {
            s.Float("time", time);
            s.Int("samplerRandom", 1);
            foreach (var (uniform, mod) in new[]
            {
                ("uGrayAmp", "gray"),
                ("uFishAmp", "fish"),
                ("uVigAmp", "vig"),
                ("uHDistortAmp", "hdistort"),
                ("uVDistortAmp", "vdistort"),
                ("uHnoise", "uhnoise"),
                ("bloom", "bloom"),
                ("glitchAmp", "glitchamp"),
                ("stime", "glitchoffset"),
                ("abX", "abx"),
                ("abY", "aby"),
                ("aberrationx", "barrelabx"),
                ("aberrationy", "barrelaby")
            })
            {
                s.Float(uniform, M(mod));
            }
            s.Float("uBarrelAmp", M("barrel") + M("barrel2"));
            s.Vec2("move", M("posx"), M("posy"));
            for (int i = 1; i <= 4; i++)
            {
                s.Vec4("twist" + i, new((float) M("twx" + i), (float) M("twy" + i), (float)(M("twa" + i) * .6), (float) M("twr" + i)));
            }
            s.Vec3("sinm", M("sina"), M("sinp"), M("sino"));
            // 源码 Draw_74 就是把 cosm.y 绑到 sinp，而不是没被用到的 cosp 字段。看着像笔误，但不是，别“修”。
            s.Vec3("cosm", M("cosa"), M("sinp"), M("coso"));
            s.Vec3("tanm", M("tana"), M("tanp"), M("tano"));
        });
    }

    public void Dispose()
    {
        main.Dispose();
        film.Dispose();
        edge.Dispose();
        glow.Dispose();
        combine.Dispose();
        water.Dispose();
        posterize.Dispose();
        roomPosterize.Dispose();
        heat.Dispose();
        largeBlur.Dispose();
        colourise.Dispose();
        hue.Dispose();
        colourBalance.Dispose();
        contrast.Dispose();
        glowAccumA.Dispose();
        glowAccumB.Dispose();
        foreach (var t in blur)
        {
            t.Dispose();
        }
        foreach (var t in roomTextures.Values)
        {
            t.Dispose();
        }
        combined.Dispose();
        distorted.Dispose();
        quantized.Dispose();
        fxA.Dispose();
        fxB.Dispose();
        bgA.Dispose();
        bgB.Dispose();
        noise.Dispose();
        waterNoise.Dispose();
        blurNoise.Dispose();
    }
}
