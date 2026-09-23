using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.UI;

/// <summary>
/// 轨道悬停帮助卡片。它刻意不算作阻塞覆盖层 —— 时间轴不会因为卡片出现而被禁用，
/// 否则 hover 与显示会互相触发出一帧显示一帧隐藏的反馈循环。
/// </summary>
public sealed partial class Viewer
{
    // 这些是窗口 / UI 单位下的真实 em 尺寸，不是图集单元格的大小。字体加载器按声明的 EmSize
    // 换算 64px 的源字形，从而保留 Retina 缩放。
    const float HelpBodySize = 17, HelpBodyLineHeight = 29;
    const float HelpTitleSize = 22, HelpSummarySize = 16, HelpSummaryLineHeight = 27;
    const float HelpPadding = 20, HelpParagraphGap = 9, HelpFooterHeight = 27;
    Color HelpBackground => Theme.HelpBackground;
    Color HelpBodyColor => Theme.Text;
    Color HelpSummaryColor => Mix(Theme.Text, Theme.Muted, .28f);
    Color HelpSecondaryColor => Theme.Muted;
    bool trackHelpVisualActive;
    Rect trackHelpHitRect;
    bool trackHelpHitRectValid;
    long trackHelpLeaveAt;
    const int TrackHelpLeaveGraceMs = 140;

    sealed record HelpRow(string Text, float Top);
    sealed record HelpCardLayout(string[] Summary, HelpRow[] Body, float BodyTop, float BodyHeight);
    HelpCardLayout? cachedHelpLayout;
    string cachedHelpKey = "";
    float helpCardScroll, helpCardScrollMax;
    EditTrack? displayedHelpTrack;

    /// <summary>
    /// 帮助卡片的出现 / 消失与淡入淡出。被其他阻塞覆盖层遮住时直接判定为不显示。
    /// 指针离开轨道标签后留 TrackHelpLeaveGraceMs 的宽限，好让鼠标能移到卡片上滚动。
    /// </summary>
    void DrawAnimatedTrackHelp(Rect timeline, EditTrack? track, float width, float height)
    {
        long now = Environment.TickCount64;
        bool blocked = UiBlockingOverlayVisible;
        if (blocked) track = null;

        if (track != null)
        {
            trackHelpLeaveAt = 0;
            if (displayedHelpTrack?.Key != track.Key)
            {
                displayedHelpTrack = track;
                motion.Snap("track-help-content", 0);
                motion.Snap("track-help-scroll", 0);
                cachedHelpKey = "";
            }
        }

        // 卡片一旦出现，它自身也算进悬停区域。没有这道闩锁的话，卡片会盖住源标签，
        // 导致 hover 有一帧失效，弹窗就开始来回闪。
        bool overCard = !blocked && trackHelpHitRectValid && trackHelpHitRect.Contains(mouseX, mouseY);
        bool keepLatched = false;
        if (!blocked && track == null && displayedHelpTrack != null)
        {
            if (overCard)
            {
                trackHelpLeaveAt = 0;
                keepLatched = true;
            }
            else
            {
                if (trackHelpLeaveAt == 0) trackHelpLeaveAt = now;
                keepLatched = now - trackHelpLeaveAt <= TrackHelpLeaveGraceMs;
            }
        }

        bool wantVisible = !blocked && displayedHelpTrack != null && (track != null || keepLatched);
        float visible = motion.Show("track-help-visible", wantVisible, .13);
        trackHelpVisualActive = visible > .001f && displayedHelpTrack != null;
        if (!trackHelpVisualActive)
        {
            trackHelpHitRectValid = false;
            if (!wantVisible) displayedHelpTrack = null;
            return;
        }
        EditTrack activeTrack = displayedHelpTrack!;
        using var fade = Canvas.Opacity(visible * motion.To("track-help-content", 1, .12, 0));
        DrawTrackHelp(timeline, activeTrack, width, height);
    }

    /// <summary>指针是否归帮助卡片所有：悬停在源标签上或落在卡片矩形内都算，滚轮据此决定给卡片还是给时间轴。</summary>
    bool TrackHelpOwnsPointer()
        => trackHelpVisualActive && displayedHelpTrack != null &&
           ((hoveredEditTrack?.Key == displayedHelpTrack.Key) ||
            (trackHelpHitRectValid && trackHelpHitRect.Contains(mouseX, mouseY)));

    /// <summary>
    /// 量测并缓存卡片排版。缓存键包含宽度、字体名与全部文本，字体或宽度一变就重排，避免沿用旧字体的量测结果。
    /// 只缓存最近一次结果：卡片同一时刻只显示一张。重排时把滚动位置归零，否则会停在新内容里不存在的位置。
    /// </summary>
    HelpCardLayout HelpLayout(EditorTrackHelp.Info info, string key, bool detailed, float width)
    {
        string cacheKey = key + "|" + detailed + "|" + width + "|" + fonts.HelpFontName + "|" +
            info.Short + "|" + string.Join("\n", info.Detail);
        if (cacheKey == cachedHelpKey && cachedHelpLayout is { } cached) return cached;
        float contentWidth = Math.Max(1, width - HelpPadding * 2);
        var summary = EditorHelpLayout.Wrap(info.Short, contentWidth,
            s => fonts.Measure(s, HelpSummarySize, unified: true));
        float bodyTop = HelpPadding + 35 + summary.Length * HelpSummaryLineHeight + 23;
        var rows = new List<HelpRow>();
        float bodyHeight = 0;
        if (detailed)
        {
            foreach (string paragraph in info.Detail)
            {
                foreach (string text in EditorHelpLayout.Wrap(paragraph, contentWidth - 10,
                    s => fonts.Measure(s, HelpBodySize, unified: true)))
                {
                    rows.Add(new(text, bodyHeight));
                    bodyHeight += HelpBodyLineHeight;
                }
                bodyHeight += HelpParagraphGap;
            }
            if (rows.Count > 0) bodyHeight -= HelpParagraphGap;
        }
        cachedHelpKey = cacheKey;
        helpCardScroll = 0;
        cachedHelpLayout = new(summary, rows.ToArray(), bodyTop, bodyHeight);
        return cachedHelpLayout;
    }

    /// <summary>
    /// 画卡片本体。按住 W 超过 220 ms 才切到详情态（宽 740），避免轻按一下就弹出大卡片。
    /// 卡片位置被夹在视口内并做平滑跟随；详情态固定贴在时间轴上方，摘要态跟在指针上方。
    /// 标题用的是 track.Property，即源文件里的原始 identifier，只做截断不做改写。
    /// </summary>
    void DrawTrackHelp(Rect timeline, EditTrack track, float viewportWidth, float viewportHeight)
    {
        var info = EditorTrackHelp.Get(track.Property, track.Window, track.Target, Current);
        bool detailed = trackHelpWHeld && Environment.TickCount64 - trackHelpWDownAt >= 220;
        float width = Math.Min(detailed ? 740 : 480, Math.Max(1, viewportWidth - 32));
        var layout = HelpLayout(info, track.Key, detailed, width);
        float naturalHeight = detailed
            ? layout.BodyTop + layout.BodyHeight + 12 + HelpFooterHeight + HelpPadding
            : HelpPadding + 35 + layout.Summary.Length * HelpSummaryLineHeight + 12 + HelpFooterHeight + HelpPadding;
        float height = Math.Min(motion.To("track-help-height", naturalHeight, .16), Math.Max(1, viewportHeight - 32));
        float x = Math.Clamp(Math.Max(mouseX + 18, timeline.X + 202), 16, Math.Max(16, viewportWidth - width - 16));
        float preferredY = detailed ? timeline.Y - height - 12 : mouseY - height - 9;
        float y = Math.Clamp(preferredY, 16, Math.Max(16, viewportHeight - height - 16));
        float shownX = Math.Clamp(motion.To("track-help-x", x, .12), 16, Math.Max(16, viewportWidth - width - 16));
        float shownY = Math.Clamp(motion.To("track-help-y", y, .12), 16, Math.Max(16, viewportHeight - height - 16));
        var card = new Rect(shownX, shownY, width, height);
        trackHelpHitRect = card;
        trackHelpHitRectValid = true;

        Canvas.Fill(new(card.X + 5, card.Y + 6, card.W, card.H), Color.Hex(0x000000, .4f));
        Canvas.Fill(card, HelpBackground); // 必须不透明：正在动的画面不能透过正文透出来。
        Canvas.Border(card, Theme.BorderStrong);
        Canvas.Fill(new(card.X + 1, card.Y + 1, card.W - 2, 2), Theme.AccentSoft);
        Canvas.Clip(card);

        string target = track.Window ? L.Get("WINDOW ") + track.Target : track.Target < 0 ? L.Get("GLOBAL") : L.Get("PROXY ") + track.Target;
        float targetWidth = fonts.Measure(target, 12, unified: true) + 18;
        float textX = card.X + HelpPadding, textWidth = Math.Max(1, card.W - HelpPadding * 2);
        float titleWidth = Math.Max(1, textWidth - targetWidth - 20);
        string title = EditorHelpLayout.FitTitle(track.Property, titleWidth,
            s => fonts.Measure(s, HelpTitleSize, unified: true, bold: true));
        Text(title, textX, card.Y + HelpPadding - 5, HelpTitleSize, HelpBodyColor,
            max: titleWidth, unified: true, bold: true);
        var badge = new Rect(card.X + card.W - HelpPadding - targetWidth, card.Y + HelpPadding + 2, targetWidth, 24);
        Canvas.Fill(badge, Theme.PanelRaised);
        Text(target, badge.X + 9, badge.Y + 1, 12, HelpSummaryColor, max: badge.W - 18, unified: true);

        float summaryY = card.Y + HelpPadding + 35;
        for (int i = 0; i < layout.Summary.Length; i++)
            Text(layout.Summary[i], textX, summaryY + i * HelpSummaryLineHeight,
                HelpSummarySize, HelpSummaryColor, max: textWidth, unified: true);

        float footerTop = card.Y + card.H - HelpPadding - HelpFooterHeight;
        helpCardScrollMax = 0;
        if (detailed)
        {
            float dividerY = card.Y + layout.BodyTop - 13;
            Canvas.Fill(new(textX, dividerY, textWidth, 1), Theme.Border);
            float bodyHeight = Math.Max(0, footerTop - card.Y - layout.BodyTop - 12);
            var bodyClip = new Rect(textX - 2, card.Y + layout.BodyTop, textWidth + 3, bodyHeight);
            helpCardScrollMax = Math.Max(0, layout.BodyHeight - bodyHeight);
            helpCardScroll = Math.Clamp(helpCardScroll, 0, helpCardScrollMax);
            float shownScroll = Math.Clamp(motion.To("track-help-scroll", helpCardScroll, .12), 0, helpCardScrollMax);
            Canvas.Clip(bodyClip);
            foreach (var row in layout.Body)
            {
                float rowY = bodyClip.Y + row.Top - shownScroll;
                if (rowY + HelpBodyLineHeight <= bodyClip.Y || rowY >= bodyClip.Y + bodyClip.H) continue;
                Text(row.Text, textX, rowY, HelpBodySize, HelpBodyColor, max: textWidth - 10, unified: true);
            }
            Canvas.Clip(card);
            if (helpCardScrollMax > 0 && bodyHeight > 0)
            {
                float thumbHeight = Math.Min(bodyHeight, Math.Max(22, bodyHeight * bodyHeight / layout.BodyHeight));
                float thumbY = bodyClip.Y + (bodyHeight - thumbHeight) * shownScroll / helpCardScrollMax;
                Canvas.Fill(new(card.X + card.W - 10, bodyClip.Y, 3, bodyHeight), Theme.ScrollTrack);
                Canvas.Fill(new(card.X + card.W - 10, thumbY, 3, thumbHeight), HelpSecondaryColor);
            }
        }

        string hint = detailed
            ? helpCardScrollMax > 0 ? L.Get("松开 W 收起 · W + 滚轮翻阅 · F1 打开手册") : L.Get("松开 W 收起 · F1 打开手册")
            : trackHelpWHeld ? L.Get("正在展开详情…") : L.Get("按住 W 查看详细说明 · F1 打开手册");
        if (!fonts.HasReadableHelpFont) hint = L.Get("字体资源缺失，请完整覆盖源码包");
        Text(hint, textX, footerTop, 12, fonts.HasReadableHelpFont ? HelpSecondaryColor : Color.Hex(0xFFA8B3),
            max: textWidth, unified: true);
        Canvas.Clip(null);
    }
}
