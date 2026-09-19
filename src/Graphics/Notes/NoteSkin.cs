using System.Numerics;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

/// <summary>
/// 把皮肤布局转换为音符绘制。chip、hold 与 bumper 使用各自的源帧契约；不把整张源帧错误缩放成裁切前的尺寸。
/// </summary>
// 由 manifest 驱动的音符皮肤。旧的高分辨率 vsnotes 包、紧凑的 UTMT 运行期 dump，
// 以及精确分轨的 v2 原始源 dump，在提交 GPU 之前都会归一到同一套
// GameMaker 局部坐标的绘制矩形。
public sealed class NoteSkin : IDisposable
{
    readonly Canvas canvas;
    Dictionary<string, Texture> textures = new(StringComparer.Ordinal);
    NoteSkinProfile profile = null!;
    public string Description => profile.Description;
    public IReadOnlyList<string> Warnings => profile.Warnings;
    public NoteSkin(Canvas canvas)
    {
        this.canvas = canvas;
        Reload();
    }

    /// <summary>
    /// 事务式重载：新皮肤损坏或不完整时，当前能用的皮肤原封不动继续存活，
    /// 只释放这次半途加载出来的纹理并把异常抛给调用方。纹理尺寸要与 manifest 声明一致，
    /// 不一致同样视为失败 —— 尺寸对不上意味着 UV 契约已经失效，硬画出来只会是错位的图。
    /// 由 Viewer 的 R 键调用，因此可以把新 dump 拷进 Assets 后不重启程序直接生效。
    /// </summary>
    public void Reload()
    {
        NoteSkinProfile next = NoteSkinProfile.Load(Paths.Assets);
        var loaded = new Dictionary<string, Texture>(StringComparer.Ordinal);
        try
        {
            foreach (var frame in next.Frames.GroupBy(f => f.File, StringComparer.Ordinal).Select(g => g.First()))
            {
                var texture = Texture.Load(canvas.Gpu, frame.File);
                bool badWidth = frame.ExpectedTextureWidth is int ew && texture.Width != ew;
                bool badHeight = frame.ExpectedTextureHeight is int eh && texture.Height != eh;
                if (badWidth || badHeight)
                {
                    int aw = texture.Width, ah = texture.Height;
                    texture.Dispose();
                    throw new InvalidDataException($"Note texture size does not match its dump manifest: {Path.GetFileName(frame.File)} is {aw}x{ah}, expected {frame.ExpectedTextureWidth?.ToString() ?? "?"}x{frame.ExpectedTextureHeight?.ToString() ?? "?"}.");
                }
                loaded[frame.File] = texture;
            }
        }
        catch
        {
            foreach (var texture in loaded.Values)
            {
                texture.Dispose();
            }
            throw;
        }
        // 全部加载成功之后才整体换掉字段，最后再释放旧纹理。顺序反过来会在加载失败时
        // 留下一个既没有旧皮肤也没有新皮肤的对象。
        var old = textures;
        textures = loaded;
        profile = next;
        foreach (var texture in old.Values)
        {
            texture.Dispose();
        }
        if (profile.Warnings.Count > 0)
        {
            Console.Error.WriteLine("Note skin: " + String.Join(" | ", profile.Warnings));
        }
        Console.Error.WriteLine("Note skin: " + profile.Description + " @ " + profile.Root);
    }

    /// <summary>
    /// 音符中心在 320×180 逻辑空间的 x。bumper 的原点比 chip 靠右 11 像素，因为它的包围盒是以
    /// 相邻两条 chip 轨的中缝为中心的。轨道绘制、命中特效与编辑器时间轴共用这一个式子。
    /// </summary>
    public static float LaneX(int type, int lane) => (type is 1 or 7 or 8 ? 137 : 126) + 23 * lane;

    /// <summary>
    /// 音符在 320×180 逻辑空间里占据的横向区间，直接由皮肤自己的帧矩形算出，不另立一套尺寸表。
    /// type/lane 超出该类音符的合法范围时返回 null —— 谱面是外部数据，这里不替它猜。
    ///
    /// 注意这是精灵整帧的范围：bumper 的帧两端还各带一个 L / M / R 字母标记，比它压住的轨道宽。
    /// 要"盖住哪几条轨"请用 <see cref="Lanes"/>，别拿这个去和轨道求交。
    /// </summary>
    public (float Left, float Right)? Span(int type, int lane)
    {
        if (lane < 0 || lane > (type is 1 or 7 or 8 ? 2 : 3))
        {
            return null;
        }
        // hold（2）的头尾用的就是 chip 帧，所以横向区间与 chip 相同。
        NoteSkinFrame? frame = type switch
        {
            0 or 2 => profile.Chip(lane),
            1 => profile.Bumper(lane),
            6 => profile.ChipMine(lane),
            7 => profile.BumperMine(lane),
            8 => profile.JudgeBumper(lane),
            _ => null
        };
        if (frame == null)
        {
            return null;
        }
        float x = LaneX(type, lane);
        return (x + frame.LocalX, x + frame.LocalX + frame.Width);
    }

    /// <summary>
    /// 音符盖住哪几条 chip 轨（闭区间行号 0-3）。chip 系只占自己那条；bumper 系压在相邻两条 chip 轨的
    /// 中缝上，盖的就是这两条 —— L 盖 0-1、M 盖 1-2、R 盖 2-3，三种一样宽。
    /// 编辑器时间轴用它把只读音符按真实宽窄画出来。
    ///
    /// 这里不拿 <see cref="Span"/> 去和 chip 轨求交：bumper 的帧两端各带一个字母标记，包围盒比实体宽得多。
    /// sp_note_bumper_normal 的 M 帧就是字面意义上的 "M ==== M"：107px 宽的帧里，两端各 5px 是 M 字母，
    /// 中间那条 45px 才是音符本体，和没有字母的 sp_note_bumper_mine_normal 一样宽（L / R 帧同理，
    /// 45px 本体 + 一侧的 L / R 字母）。按包围盒求交会把 M 判成横跨 0-3，比它实际盖的多一倍。
    /// </summary>
    public (int First, int Last)? Lanes(int type, int lane) => Span(type, lane) == null
        ? null
        : type is 1 or 7 or 8 ? (lane, lane + 1) : (lane, lane);

    /// <summary>
    /// type 取值沿用原版：0=chip，1=bumper，6=chip mine，7=bumper mine，8=judge bumper；
    /// 其余取值不画，不要顺手补 default 分支去猜。rotation 单位为度。
    /// skin 是 changeskin 选中的皮肤下标，取不到帧就是这套皮肤不画这类音符（例如 stopmotion 没有地雷）。
    /// </summary>
    public void Note(int type, int lane, float x, float y, double alpha, float rotation, int skin = 0)
    {
        bool bumper = type is 1 or 7 or 8;
        NoteSkinFrame? frame = type switch
        {
            0 => profile.Chip(lane, skin),
            1 => profile.Bumper(lane, skin),
            6 => profile.ChipMine(lane, skin),
            7 => profile.BumperMine(lane, skin),
            8 => profile.JudgeBumper(lane, skin),
            _ => null
        };
        if (frame == null)
        {
            return;
        }
        // obj_note_rendering：精灵原点不对称，当旋转后的原点越过左半边/下半边时，
        // 需要一像素的位置补正才能和原版对齐。这不是"魔数"，去掉会整体偏移一像素。
        float angle = ((rotation % 360) + 360) % 360;
        if (bumper && (angle + 90) % 360 >= 180)
        {
            x += 1;
        }
        if (angle >= 180)
        {
            y += 1;
        }
        Draw(frame, x, y, alpha, rotation);
    }

    /// <summary>
    /// 长条的三段（头、尾、body）都按原版锚点规则摆放，坐标是 320×180 逻辑空间的像素。
    /// pressed 表示已经按住：此时只画剩余的尾帽，两个锚点都截到 140，body 一直延伸到判定线 144。
    /// body 还会裁到可见区间 [-8, 188] 并按比例取 UV 子区间，不是整条拉伸。
    /// 头和尾分别取自皮肤的 hold_head / hold_end：正常皮肤两者都是 chip 帧，
    /// stopmotion 和 stargazers 才有各自独立的首尾精灵。
    /// </summary>
    public void Hold(int lane, float x, float startY, float endY, double alpha, bool pressed = false, int skin = 0)
    {
        // 原版尾帽的锚点比它的音符位置低六像素。
        endY += 6;
        float top = Math.Min(startY, endY), bottom = Math.Max(startY, endY);
        // obj_pressed_holds 只画剩下的尾帽。两个锚点都截到 140；
        // body 在判定线处到 144。
        if (pressed)
        {
            top = Math.Min(top, 140);
            bottom = Math.Min(bottom, 140);
        }
        float bodyTop = top + 4, bodyBottom = pressed ? bottom + 4 : bottom - 3;
        float visibleTop = Math.Max(-8, bodyTop), visibleBottom = Math.Min(188, bodyBottom);
        if (visibleBottom > visibleTop && profile.HoldBody(lane, skin) is { } body)
        {
            float relY = (visibleTop - bodyTop) / (bodyBottom - bodyTop);
            float relH = (visibleBottom - visibleTop) / (bodyBottom - bodyTop);
            var uv = RelativeUv(body, 0, relY, 1, relH);
            canvas.Quad(GetTexture(body), new(x + body.LocalX, visibleTop, body.Width, visibleBottom - visibleTop), Color.White.Alpha(alpha), uv);
        }
        Draw(profile.HoldHead(lane, skin), x, top, alpha, 0);
        if (!pressed)
        {
            Draw(profile.HoldEnd(lane, skin), x, bottom, alpha, 0);
        }
    }

    Texture GetTexture(NoteSkinFrame frame) => textures[frame.File];
    void Draw(NoteSkinFrame? frame, float x, float y, double alpha, float rotation)
    {
        if (frame == null || alpha <= 0)
        {
            return;
        }
        // LocalX/Y 已经包含 GameMaker 的 TargetX/Y 和精灵原点，不要再加一遍。紧凑 v1 用的是
        // 完整的带留白帧；原始源 v2 可能只取图集里的一块子矩形，再放回原始包围盒内的位置。
        var transform = Matrix3x2.CreateRotation(-rotation * MathF.PI / 180) * Matrix3x2.CreateTranslation(x, y);
        var a = new Vector2(frame.LocalX, frame.LocalY);
        var b = new Vector2(frame.LocalX + frame.Width, frame.LocalY);
        var c = new Vector2(frame.LocalX, frame.LocalY + frame.Height);
        var d = new Vector2(frame.LocalX + frame.Width, frame.LocalY + frame.Height);
        canvas.Polygon(GetTexture(frame), Vector2.Transform(a, transform), Vector2.Transform(b, transform), Vector2.Transform(c, transform),
            Vector2.Transform(d, transform), Color.White.Alpha(alpha), FrameUv(frame));
    }

    static Rect FrameUv(NoteSkinFrame frame) => new(frame.UvX, frame.UvY, frame.UvW, frame.UvH);
    /// <summary>x/y/w/h 是帧内的归一化比例（0-1），映射到该帧在图集中的 UV 子区间，不是图集全局 UV。</summary>
    static Rect RelativeUv(NoteSkinFrame frame, float x, float y, float w, float h) => new(frame.UvX + frame.UvW * x, frame.UvY + frame.UvH * y,
        frame.UvW * w, frame.UvH * h);
    public void Dispose()
    {
        foreach (var texture in textures.Values)
        {
            texture.Dispose();
        }
        textures.Clear();
    }
}
