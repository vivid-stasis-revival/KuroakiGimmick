using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

/// <summary>
/// 由 manifest 角色驱动的分层背景、CG、HP 覆盖层与定时斜线。本类不认识曲名，缺角色即跳过该项，不用别的素材顶替。
/// 自有两块离屏目标，尺寸跟随场景变化。
/// </summary>
public sealed class NativeSequenceRenderer : IDisposable
{
    readonly Canvas canvas;
    readonly NativeGimmickRenderer native;
    readonly CustomEffects effects;
    /// <summary>角色背景的中转与滤镜输出目标；两者尺寸跟随传入的 scene，只在 Backgrounds 里按需缩放。</summary>
    readonly Target layer, filtered;
    public NativeSequenceRenderer(Canvas canvas, NativeGimmickRenderer native, CustomEffects effects)
    { this.canvas = canvas; this.native = native; this.effects = effects; layer = new(canvas.Gpu, 320, 180); filtered = new(canvas.Gpu, 320, 180); }
    /// <summary>按 manifest 角色名查 mod 再求值；角色未声明时返回 fallback，不抛错也不猜测同名 mod。</summary>
    double M(Session session, string role, double time, double fallback = 0) => session.NativeGimmick.Data?.Sequence?.Mod(role) is { } mod ? session.Timeline.Get(mod, time) : fallback;
    /// <summary>
    /// chorus / enddrop 两个角色背景各自先画进 layer，按需过一遍房间 FX 图层，再以角色 alpha 贴回 scene。
    /// 顺序固定，不按 alpha 大小重排；目标缩放前先 Flush。
    /// </summary>
    public void Backgrounds(Target scene, Session session, double time, bool useEffects)
    {
        if (session.NativeGimmick.Data?.Sequence is not { } spec) return;
        if (layer.Texture.Width != scene.Texture.Width) { canvas.Flush(); layer.Resize(scene.Texture.Width, scene.Texture.Height); filtered.Resize(scene.Texture.Width, scene.Texture.Height); }
        foreach (string role in new[] { "chorus", "enddrop" })
        {
            double alpha = M(session, role + "Alpha", time);
            if (alpha <= 0 || !spec.Sprites.TryGetValue(role, out var sprite) || native.SequenceTexture(session, sprite) is not { } image) continue;
            canvas.Begin(layer, layer.Texture.Width, layer.Texture.Height, 320, 180, Color.Hex(0));
            canvas.Quad(image, new(0, 0, 320, 180), Color.White); canvas.Flush();
            Texture input = layer.Texture;
            if (useEffects && spec.BackgroundLayers.TryGetValue(role, out var name) && session.Fx.Find(name) is { Enabled: true } fx)
            { effects.RenderNativeLayer(filtered, input, fx, session, time); input = filtered.Texture; }
            canvas.Begin(scene, scene.Texture.Width, scene.Texture.Height, 320, 180);
            canvas.Quad(input, new(0, 0, 320, 180), Color.White.Alpha(alpha)); canvas.Flush();
        }
    }
    /// <summary>
    /// slash / gun 回调的斜线。按原版用反相目标色、目标系数取零的混合，画完必须恢复常规 source-alpha。
    /// 线条端点与走向全部来自 Timeline.Hash(seed)，同一 e.Index 重放结果一致，不使用运行时随机数。
    /// </summary>
    public void Slashes(Session session, double time)
    {
        if (session.NativeGimmick.Data?.Sequence is not { } spec) return;
        var events = session.Timeline.Callbacks.Where(c => c.Time <= time && c.Time + 1 > time && (c.Name == spec.Mod("slash") || c.Name == spec.Mod("gun")));
        canvas.Blend(BlendFactor.InverseDestinationColor, BlendFactor.Zero);
        foreach (var e in events)
        {
            double age = time - e.Time; float width = (float)(12 * (1 - age) * (1 - age));
            Color color = Color.Hex(e.Name == spec.Mod("slash") ? 0xFF00DCu : 0x4800FFu);
            for (int i = 0; i < e.Value; i++)
            {
                uint seed = unchecked((uint)e.Index * 65537 + (uint)i * 127 + 3127);
                if (Timeline.Hash(seed) < .5) canvas.Line(Timeline.Hash(seed + 1) * 320, -6, Timeline.Hash(seed + 2) * 320, 186, width, color);
                else canvas.Line(-6, Timeline.Hash(seed + 1) * 180, 326, Timeline.Hash(seed + 2) * 180, width, color);
            }
        }
        canvas.Blend(BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha);
    }
    /// <summary>返回替换用的 hold 覆盖贴图（固定取第 3 帧）；返回 null 表示条件不成立，调用方继续用公共的 sp_holdnote_overlay。</summary>
    public Texture? HoldOverlay(Session session, double time)
    {
        var spec = session.NativeGimmick.Data?.Sequence;
        return spec != null && M(session, "whiteOverlay", time) >= .5 && spec.Sprites.TryGetValue("holdOverlay", out var sprite)
            ? native.SequenceTexture(session, sprite, 3) : null;
    }
    /// <summary>HP 条 HUD，与固定游戏 HUD 同一遍绘制。受 GameUiEnabled 与 uialpha 双重门控；填充条按 hpAmount/100 横向缩放。</summary>
    public void Hud(Session session, double time)
    {
        if (!session.Project.GameUiEnabled || session.Timeline.Get("uialpha", time) == 0 || session.NativeGimmick.Data?.Sequence is not { } spec) return;
        double alpha = M(session, "hpAlpha", time);
        if (spec.Sprites.TryGetValue("hpBar", out var bar)) native.DrawSequenceSprite(session, bar, 0, 0, 165, 1, 1, alpha, time);
        if (spec.Sprites.TryGetValue("hpFill", out var fill)) native.DrawSequenceSprite(session, fill, 0, 56, 169, M(session, "hpAmount", time) / 100, 1, alpha, time);
    }
    /// <summary>CG 画在 GUI 阶段，位于后处理之后，因此不受公共后处理影响；filtered 为真，走 post mode 0 的 shader。</summary>
    public void Gui(Session session, double time)
    {
        if (session.NativeGimmick.Data?.Sequence is not { } spec || !spec.Sprites.TryGetValue("cg", out var sprite)) return;
        native.DrawSequenceSprite(session, sprite, (int)Math.Floor(M(session, "cgFrame", time)), 160, 0,
            M(session, "cgScale", time) / 4, .25, M(session, "cgAlpha", time, 1), time, true);
    }
    public void Dispose() { layer.Dispose(); filtered.Dispose(); }
    /// <summary>
    /// 结尾白场。时间过了 EndFadeTime 还不够，必须同时存在已触发的 EndFadeTrigger mod 事件，跳到尾部预览才不会凭空全白。
    /// 矩形按 (-1,-1,322,182) 略微外扩，避免缩放后边缘漏出一像素。
    /// </summary>
    public void EndFade(Session session, double time)
    {
        if (session.NativeGimmick.Data?.Sequence is not { EndFadeTime: { } start } spec || time < start ||
            !session.Chart.Mods.Any(e => e.Name == spec.EndFadeTrigger && session.Timeline.Bpm.Time(e.Beat) <= time)) return;
        canvas.Fill(new(-1, -1, 322, 182), Color.White.Alpha((time - start) / spec.EndFadeDuration));
    }
}
