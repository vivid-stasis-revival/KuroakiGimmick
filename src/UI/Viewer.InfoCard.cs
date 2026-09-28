using KuroakiGimmick.Core;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.UI;

/// <summary>
/// 乐曲信息卡片覆盖层。卡片按当前难度成立，因此切难度后要重算：BACKSTAGE 的曲名、封面与音频
/// 都来自 enc_data，六项统计也用的是它自己的音频长度。卡片渲染和读回都是 GPU 工作，只在窗口线程发生。
/// </summary>
public sealed partial class Viewer
{
    bool infoCard, infoCardInput;
    InfoCardRenderer? cardRenderer;
    SongInfoCard? cardData;
    /// <summary>会话或难度换了就作废，下一帧重新装配；不在 Draw 里每帧重算统计。</summary>
    int cardRevision = -1;
    Texture? cardTexture;

    public bool InfoCardVisible => infoCard;

    /// <summary>导出宽度，固定 16:9；卡面按 1280x720 的逻辑坐标绘制，这里只决定像素密度。</summary>
    int CardWidth => ViewerSettings.ValidCardWidth(preferences.CardWidth);

    public void OpenInfoCard()
    {
        if (Current.IsEmpty) { message = L.Get("Open a chart to build an info card."); return; }
        infoCard = true;
        settings = help = false;
        cardRevision = -1;
        click = false;
    }

    void CloseInfoCard()
    {
        infoCard = false;
        click = false;
    }

    /// <summary>卡片数据装配失败（例如长度为零算不出统计）只提示，不让覆盖层白屏。</summary>
    SongInfoCard? CardData()
    {
        if (cardData != null && cardRevision == Current.GetHashCode())
        {
            return cardData;
        }
        try
        {
            cardData = SongInfoCard.Build(Current);
            cardRevision = Current.GetHashCode();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or OverflowException)
        {
            cardData = null;
            message = L.Get("Info card unavailable: ") + ex.Message;
        }
        return cardData;
    }

    void DrawInfoCard(int w, int h)
    {
        // 关闭后 alpha 仍大于 .001 时继续绘制，让淡出动画播完；期间 Button 由 UiClosingOverlay 拦截。
        if (!infoCard && infoCardAlpha <= .001f)
        {
            return;
        }
        // 覆盖层铺满整个窗口，必须先解除上层留下的裁剪区，否则卡片会被时间轴/工作区的裁剪切断。
        Canvas.Clip(null);
        using var fade = Canvas.Opacity(infoCardAlpha);
        Canvas.Fill(new(0, 0, w, h), Color.Hex(0x050406).Alpha(.94));
        var card = cardData;
        Text(L.Get("INFO CARD"), 40, 34, 20, white, true);
        Text(card == null ? L.Get("Unavailable") : card.Title + "  /  " + card.DisplayDifficulty, 40, 62, 12, muted, max: w - 520);
        Text($"{CardWidth} x {CardWidth * 9 / 16}", w - 220, 40, 11, soft, true);
        // 预览按整数倍缩小，保持与导出图一致的像素关系。
        float scale = Math.Min((w - 80) / (float)InfoCardRenderer.BaseWidth, (h - 190) / (float)InfoCardRenderer.BaseHeight);
        float cw = InfoCardRenderer.BaseWidth * scale, ch = InfoCardRenderer.BaseHeight * scale;
        // 入场时整体轻微上浮，与设置层同一套手感。
        var frame = new Rect((w - cw) / 2, 96 + (1 - infoCardAlpha) * 10, cw, ch);
        // 卡片本帧已经在 UI 之前画进自己的离屏 target，这里只是把它当一张贴图合成进来。
        if (card != null && cardTexture != null)
        {
            Canvas.Quad(cardTexture, frame, Color.White);
        }
        Canvas.Border(frame, line);
        float by = frame.Y + ch + 20;
        infoCardInput = true;
        if (Button(L.Get("SAVE PNG"), new(frame.X, by, 140, 36), primary: true, enabled: card != null && !Busy))
        {
            SaveInfoCard();
        }
        if (Button(L.Get("COPY IMAGE"), new(frame.X + 150, by, 150, 36), enabled: card != null && !Busy && ImageClipboard.Supported))
        {
            CopyInfoCard();
        }
        if (Button(L.Get("CLOSE"), new(frame.X + cw - 110, by, 110, 36)))
        {
            CloseInfoCard();
        }
        infoCardInput = false;
    }

    /// <summary>
    /// 在 UI 合成之前把卡片画进它自己的离屏 target。切换渲染目标要发生在本帧 UI 开始之前，
    /// 不能画到一半再跳走，否则已提交的 UI 几何会落到错误的目标上。
    /// </summary>
    void RenderInfoCard()
    {
        if (!infoCard || Current.IsEmpty)
        {
            cardTexture = null;
            return;
        }
        var card = CardData();
        if (card == null)
        {
            cardTexture = null;
            return;
        }
        cardRenderer ??= new();
        cardTexture = cardRenderer.Render(Canvas, fonts, Renderer.GameUi, Current, card, CardWidth).Texture;
    }

    /// <summary>重画一张卡片并读回 PNG 字节。只能在窗口线程调用，且不在 UI 合成中途。</summary>
    byte[]? RenderCardPng(int? width = null)
    {
        var card = CardData();
        if (card == null)
        {
            return null;
        }
        cardRenderer ??= new();
        var rendered = cardRenderer.Render(Canvas, fonts, Renderer.GameUi, Current, card, width ?? CardWidth);
        var png = Canvas.EncodePng(rendered);
        cardTexture = rendered.Texture;
        return png;
    }

    void SaveInfoCard()
    {
        var card = CardData();
        if (card == null || Busy)
        {
            return;
        }
        string suggested = SuggestedExportPath("InfoCard",
            Sanitize(card.Title) + "_" + Sanitize(card.DisplayDifficulty) + ".png");
        Dialog(true, suggested, paths =>
        {
            string path = paths[0];
            RememberExportDestination("InfoCard", path);
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                path += ".png";
            }
            try
            {
                var png = RenderCardPng();
                if (png == null) { message = L.Get("Info card unavailable: ") + L.Get("no chart loaded"); return; }
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                ExportFiles.WriteBytes(path, png, preferences.OverwriteExports);
                message = L.Get("Info card saved: ") + path;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                message = L.Get("Info card save failed: ") + ex.Message;
            }
        });
    }

    void CopyInfoCard()
    {
        if (Busy)
        {
            return;
        }
        try
        {
            var png = RenderCardPng();
            if (png == null)
            {
                return;
            }
            string? error = ImageClipboard.CopyPng(png);
            message = error == null ? L.Get("Info card copied to the clipboard.") : L.Get("Copy image failed: ") + error;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            message = L.Get("Copy image failed: ") + ex.Message;
        }
    }

    /// <summary>
    /// 命令行用：直接把当前难度的卡片写成 PNG，不经界面。<paramref name="width"/> 覆盖设置里的导出尺寸。
    /// 调用方负责保证在窗口线程上；装配失败按异常抛出，由 Program 统一报错，不静默写出一张空图。
    /// </summary>
    public void SaveInfoCardTo(string path, int? width = null, bool? overwrite = null)
    {
        var png = RenderCardPng(width) ?? throw new InvalidDataException("No chart loaded: nothing to put on an info card.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        ExportFiles.WriteBytes(path, png, overwrite ?? preferences.OverwriteExports);
    }

    /// <summary>文件名里去掉路径分隔符与控制字符，只保留能安全落盘的部分；空串退回 card。</summary>
    static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string cleaned = new string([.. name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c)]).Trim();
        return cleaned.Length == 0 ? "card" : cleaned[..Math.Min(60, cleaned.Length)];
    }
}
