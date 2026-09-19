using KuroakiGimmick.Core.Documentation;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.UI;

/// <summary>
/// VSM 手册整页的绘制：导航树、正文、本页目录抽屉、页脚翻页。
/// 输入判定和绘制写在一起而不是拆成独立的 hit-test pass，因为命中区依赖本帧算出的布局与裁剪矩形。
/// </summary>
public sealed partial class Viewer
{
    Color DocsBg => Theme.DocsBackground;
    Color DocsRail => Theme.DocsRail;
    Color DocsText => Theme.Text;
    Color DocsMuted => Theme.Muted;
    Color DocsAccent => Theme.AccentSoft;

    /// <summary>
    /// 手册内的通用按钮。右键加入菜单一旦打开，除菜单自身的按钮（referenceMenuInput）外全页按钮都不收输入，
    /// 免得点穿到菜单背后。命中后立刻吞掉 click 并把 hover 动画 Snap 到 1，避免按下那一帧还停在补间中间。
    /// </summary>
    bool ReferenceButton(string label, Rect r, bool active = false, bool enabled = true, string? key = null)
    {
        bool over = referenceOpen && (referenceAddId.Length == 0 || referenceMenuInput) && r.Contains(mouseX, mouseY);
        string id = "docs-button:" + (key ?? label);
        float hover = motion.To(id + ":hover", over && enabled ? 1 : 0, .12, 0);
        float selected = motion.To(id + ":active", active ? 1 : 0, .16, active ? 1 : 0);
        Color color = Mix(Mix(Theme.PanelAlt, Theme.PanelRaised, hover), Theme.Accent.Alpha(.34f), selected);
        Canvas.Fill(r, color);
        Canvas.Border(r, Mix(Theme.Border, DocsAccent, selected));
        float size = 14, textWidth = fonts.Measure(label, size, unified: true);
        Text(label, r.X + Math.Max(7, (r.W - textWidth) / 2), r.Y + (r.H - 23) / 2, size,
            enabled ? DocsText : DocsMuted.Alpha(.4), max: r.W - 14, unified: true);
        bool hit = referenceOpen && enabled && click && over;
        if (hit) { click = false; motion.Snap(id + ":hover", 1); }
        return hit;
    }

    /// <summary>画滚动条。shown/max 都是内容像素，不是比例；滑块最短 28px，内容短于一屏（max &lt;= 0）时整条不画。</summary>
    void DrawReferenceScrollbar(Rect rect, float shown, float max)
    {
        if (max <= 0 || rect.H <= 0) return;
        float size = Math.Clamp(rect.H * rect.H / (max + rect.H), Math.Min(28, rect.H), rect.H);
        float y = rect.Y + (rect.H - size) * shown / max;
        Canvas.Fill(new(rect.X + rect.W - 6, rect.Y, 3, rect.H), Theme.ScrollTrack);
        Canvas.Fill(new(rect.X + rect.W - 7, y, 4, size), Theme.ScrollThumb);
    }

    /// <summary>
    /// 画整页手册。开头清空 referenceModHits，命中区每帧按绘制顺序重建，所以右键取 LastOrDefault 拿到的就是最上层。
    /// 宽度 &gt;= 1050 时本页目录变成常驻右栏并占掉正文宽度，否则是浮在正文上的抽屉；两种形态的正文宽度在这里就要算准，
    /// 因为排版结果按 (id + 宽度) 缓存，宽度一变才重新 Build。
    /// </summary>
    void DrawReference(int w, int h)
    {
        referenceModHits.Clear();
        using var opacity = Canvas.Opacity(referenceAlpha);
        Canvas.Clip(null);
        Canvas.Fill(new(0, 0, w, h), Color.Hex(0, .72f));
        float slide = (1 - referenceAlpha) * 10;
        var r = new Rect(16, 16 + slide, Math.Max(1, w - 32), Math.Max(1, h - 32));
        Canvas.Fill(r, DocsBg);
        Canvas.Border(r, Theme.BorderStrong);
        float navWidth = Math.Clamp(r.W * .205f, 208, 286);
        bool wide = r.W >= 1050;
        bool outlineRequested = wide ? !referenceOutlineHidden : referenceOutlineOpen;
        float tocWidth = wide && !referenceOutlineHidden ? 194 : 0;
        float centerX = r.X + navWidth + 26;
        float centerW = Math.Max(120, r.W - navWidth - tocWidth - 66);
        Text("VSM 手册", r.X + 20, r.Y + 17, 23, DocsText, max: navWidth - 20, unified: true, bold: true);
        float headerEnd = r.X + r.W - 109;
        referenceSearchRect = new(centerX, r.Y + 15, Math.Max(80, headerEnd - centerX - 175), 36);
        float focus = motion.To("docs-search-focus", referenceSearchFocus ? 1 : 0, .14, 0);
        Canvas.Fill(referenceSearchRect, referenceSelectAll ? Theme.PanelRaised : Theme.PanelAlt);
        Canvas.Border(referenceSearchRect, Mix(Theme.Border, DocsAccent, focus));
        string shown = referenceQuery.Length == 0 && referenceComposition.Length == 0
            ? "搜索文档…  /" : referenceQuery + referenceComposition;
        Text(EditorHelpLayout.FitTitle(shown, referenceSearchRect.W - 22, s => fonts.Measure(s, 15, unified: true)),
            referenceSearchRect.X + 11, referenceSearchRect.Y + 5, 15, referenceQuery.Length > 0 ? DocsText : DocsMuted, unified: true);
        if (ReferenceButton("<", new(headerEnd - 159, r.Y + 16, 32, 34), enabled: referenceBack.Count > 0)) ReferenceHistory(false);
        if (ReferenceButton(">", new(headerEnd - 121, r.Y + 16, 32, 34), enabled: referenceForward.Count > 0)) ReferenceHistory(true);
        if (ReferenceButton("目录", new(headerEnd - 82, r.Y + 16, 73, 34), active: outlineRequested))
        {
            if (wide) referenceOutlineHidden = !referenceOutlineHidden;
            else referenceOutlineOpen = !referenceOutlineOpen;
        }
        if (ReferenceButton("关闭", new(r.X + r.W - 87, r.Y + 17, 67, 32))) CloseReference();
        Canvas.Fill(new(r.X, r.Y + 66, r.W, 1), Theme.Border);

        referenceListRect = new(r.X + 10, r.Y + 79, navWidth - 9, Math.Max(1, r.H - 132));
        Canvas.Fill(new(r.X + 1, r.Y + 67, navWidth + 8, r.H - 68), DocsRail);
        DrawReferenceNavigation();
        Canvas.Fill(new(r.X + navWidth + 9, r.Y + 67, 1, r.H - 68), Theme.Border);

        // 先算出目录抽屉“显示时”的范围，再画可点的正文：
        // 抽屉处于紧凑态或正在淡出时会盖住正文，这段区域内的点击不能落到正文上。
        float outlineAlpha = motion.Show("docs-outline", outlineRequested, .16);
        referenceOutlineCoversArticle = outlineAlpha > .001f && (!wide || referenceOutlineHidden);
        referenceOutlineRect = default;
        if (outlineAlpha > .001f)
        {
            float tw = wide ? 179 : Math.Min(250, centerW);
            float tx = wide ? r.X + r.W - 194 : r.X + r.W - tw - 12;
            referenceOutlineRect = new(tx, r.Y + 80, tw, Math.Max(1, r.H - 107));
        }
        var selected = VsmReference.Shared.Find(referenceSelected);
        if (selected != null)
        {
            string crumb = selected.Category;
            Text(crumb, centerX, r.Y + 79, 13, DocsMuted, max: centerW, unified: true);
            string title = EditorHelpLayout.FitTitle(selected.Name, centerW, s => fonts.Measure(s, 25, unified: true, bold: true));
            Text(title, centerX, r.Y + 109, 25, DocsText, max: centerW, unified: true, bold: true);
            RegisterReferenceMod(new(centerX, r.Y + 105, centerW, 43), r, selected.Id);
            referenceBodyRect = new(centerX, r.Y + 159, centerW, Math.Max(1, r.H - 224));
            string key = selected.Id + "|" + referenceBodyRect.W.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (key != referenceLayoutKey)
            {
                referencePage = ReferenceArticleLayout.Build(selected, centerW - 20,
                    (text, size, bold) => fonts.Measure(text, size, unified: true, bold: bold));
                referenceLayoutKey = key;
            }
            referenceBodyMax = Math.Max(0, referencePage.Height - referenceBodyRect.H);
            referenceBodyScroll = Math.Clamp(referenceBodyScroll, 0, referenceBodyMax);
            referenceBodyShown = Math.Clamp(motion.To("docs-scroll", referenceBodyScroll, .16), 0, referenceBodyMax);
            float pageAlpha = motion.To("docs-page", 1, .16, 0);
            using (Canvas.Opacity(pageAlpha)) DrawReferenceArticle(selected, (1 - pageAlpha) * 6);
            Canvas.Clip(null);
            DrawReferenceScrollbar(referenceBodyRect, referenceBodyShown, referenceBodyMax);
            DrawReferencePageFooter(selected, new(centerX, r.Y + r.H - 51, centerW, 33));
        }
        else
        {
            referenceBodyMax = 0;
            Text("文档不可用", centerX, r.Y + 110, 22, DocsText, unified: true);
            Text(VsmReference.Shared.Error, centerX, r.Y + 155, 15, DocsMuted, max: centerW, unified: true);
        }
        if (outlineAlpha > .001f)
        {
            using (Canvas.Opacity(outlineAlpha))
            {
                Canvas.Fill(referenceOutlineRect, wide ? DocsBg : DocsRail);
                if (!wide) Canvas.Border(referenceOutlineRect, Theme.BorderStrong);
                DrawReferenceOutline();
            }
        }
        Text(string.IsNullOrWhiteSpace(referenceQuery) ? "Ctrl/Cmd+F 搜索 · F1 关闭" : $"{referenceResults.Length} 个结果",
            r.X + 20, r.Y + r.H - 35, 12, DocsMuted, max: navWidth - 20, unified: true);
        float toastAlpha = motion.Show("docs-toast", referenceStatus.Length > 0 && uptime.Elapsed.TotalSeconds < referenceStatusUntil, .12);
        if (toastAlpha > .001f)
        {
            float fade = toastAlpha;
            using var toast = Canvas.Opacity(fade);
            var box = new Rect(r.X + r.W / 2 - 85, r.Y + r.H - 57, 170, 37);
            Canvas.Fill(box, Color.Hex(0x364355));
            Text(referenceStatus, box.X + 16, box.Y + 6, 15, DocsText, max: box.W - 32, unified: true);
        }
        Canvas.Clip(null);
        DrawReferenceAddMenu(w, h);
    }

    /// <summary>
    /// 画左侧导航树，只绘制视口内那几行。展开/选中会重建 referenceNav，所以这两处之后必须 break：
    /// 继续迭代的是已经失效的列表，行与 index 会对不上。
    /// </summary>
    void DrawReferenceNavigation()
    {
        referenceListMax = Math.Max(0, referenceNav.Count * ReferenceNavHeight - referenceListRect.H);
        referenceListScroll = Math.Clamp(referenceListScroll, 0, referenceListMax);
        referenceListShown = Math.Clamp(motion.To("docs-nav-scroll", referenceListScroll, .15), 0, referenceListMax);
        Canvas.Clip(referenceListRect);
        int first = Math.Max(0, (int)(referenceListShown / ReferenceNavHeight));
        int end = Math.Min(referenceNav.Count, first + (int)Math.Ceiling(referenceListRect.H / ReferenceNavHeight) + 1);
        for (int i = first; i < end; i++)
        {
            var item = referenceNav[i];
            var row = new Rect(referenceListRect.X + 2, referenceListRect.Y + i * ReferenceNavHeight - referenceListShown,
                referenceListRect.W - 12, ReferenceNavHeight);
            if (item.Group)
            {
                Text(item.Label, row.X + 10, row.Y + 8, 12, DocsAccent, max: row.W - 20, unified: true, bold: true);
                continue;
            }
            RegisterReferenceMod(row, referenceListRect, item.Id);
            bool over = row.Contains(mouseX, mouseY) && referenceListRect.Contains(mouseX, mouseY) && referenceOpen && referenceAddId.Length == 0;
            float hover = motion.To("docs-row-hover:" + item.Id, over ? 1 : 0, .12, 0);
            float selected = motion.To("docs-row-selected:" + item.Id, item.Id == referenceSelected ? 1 : 0, .18, 0);
            Canvas.Fill(row, Mix(Mix(DocsRail, Theme.PanelRaised, hover), Theme.Accent.Alpha(.28f), selected));
            if (selected > .001f) Canvas.Fill(new(row.X, row.Y + 4, 2, row.H - 8), DocsAccent.Alpha(selected));
            float textX = row.X + (item.Branch ? 29 : 12 + item.Depth * 16);
            float fontSize = item.Depth > 0 ? 14 : 15;
            Text(EditorHelpLayout.FitTitle(item.Label, row.X + row.W - textX - 8,
                s => fonts.Measure(s, fontSize, unified: true)), textX, row.Y + 5, fontSize,
                Mix(DocsMuted, DocsText, Math.Max(selected, hover)), unified: true);
            if (item.Branch)
            {
                float unfold = motion.To("docs-tree:" + item.Id, referenceExpanded.Contains(item.Id) ? 1 : 0, .16, 0);
                float ax = row.X + 15, ay = row.Y + 16;
                Canvas.Line(ax - 3, ay - 4 + unfold * 2, ax + 2 - unfold * 2, ay + unfold * 2, 1.5f, DocsMuted);
                Canvas.Line(ax + 2 - unfold * 2, ay + unfold * 2, ax - 3 + unfold * 7, ay + 4 - unfold * 6, 1.5f, DocsMuted);
            }
            if (over && click)
            {
                click = false; FocusReferenceSearch(false);
                if (item.Branch && mouseX < row.X + 27)
                {
                    if (!referenceExpanded.Add(item.Id)) referenceExpanded.Remove(item.Id);
                    BuildReferenceNavigation(); break;
                }
                SelectReference(item.Id); break;
            }
        }
        Canvas.Clip(null);
        DrawReferenceScrollbar(referenceListRect, referenceListShown, referenceListMax);
    }

    /// <summary>
    /// 按 ReferenceArticleLayout 排好的 Item 逐条画正文。offset 是切页时的入场位移，只影响绘制不影响滚动量。
    /// 表格首列带 Link 的格子可跳转，表头不登记命中区。
    /// </summary>
    void DrawReferenceArticle(VsmReference.Entry entry, float offset)
    {
        Canvas.Clip(referenceBodyRect);
        bool articleInput = referenceOpen && referenceAddId.Length == 0 && !(referenceOutlineCoversArticle && referenceOutlineRect.Contains(mouseX, mouseY));
        int index = 0;
        foreach (var item in referencePage.Items)
        {
            float y = referenceBodyRect.Y + item.Top - referenceBodyShown + offset;
            int itemIndex = index++;
            if (y + item.Height < referenceBodyRect.Y || y >= referenceBodyRect.Y + referenceBodyRect.H) continue;
            var box = new Rect(referenceBodyRect.X, y, referenceBodyRect.W - 20, item.Height);
            if (item.Kind is "table-header" or "table-row")
            {
                bool head = item.Kind == "table-header";
                if (!head) RegisterReferenceMod(box, referenceBodyRect, item.Link);
                bool over = articleInput && !head && item.Link.Length > 0 && box.Contains(mouseX, mouseY) && referenceBodyRect.Contains(mouseX, mouseY);
                float hover = motion.To($"docs-cell:{entry.Id}:{itemIndex}", over ? 1 : 0, .12, 0);
                Canvas.Fill(box, head ? Theme.PanelRaised : Mix(Theme.PanelAlt, Theme.PanelRaised, hover));
                Canvas.Fill(new(box.X, box.Y + box.H - 1, box.W, 1), Theme.Border);
                float x = box.X;
                for (int col = 0; col < item.Lines.Length; col++)
                {
                    for (int row = 0; row < item.Lines[col].Length; row++)
                        Text(item.Lines[col][row], x + 12, y + 8 + row * 25, 15,
                            col == 0 && item.Link.Length > 0 ? DocsAccent : DocsText, max: item.Widths[col] - 24, unified: true, bold: head);
                    x += item.Widths[col];
                }
                if (referenceOpen && over && click)
                { click = false; SelectReference(item.Link, true); break; }
            }
            else if (item.Kind == "code")
            {
                Canvas.Fill(box, Theme.PanelAlt);
                Canvas.Border(box, Theme.Border);
                Text("CODE", box.X + 14, box.Y + 10, 10, DocsMuted, unified: true);
                // 复制按钮会画在裁剪区之外，只有整颗按钮都在正文视口内时才允许响应点击。
                var button = new Rect(box.X + box.W - 75, box.Y + 6, 64, 26);
                bool buttonVisible = button.Y >= referenceBodyRect.Y && button.Y + button.H <= referenceBodyRect.Y + referenceBodyRect.H;
                if (ReferenceButton("复制", button, enabled: buttonVisible && articleInput, key: entry.Id + ":code:" + itemIndex)) CopyReferenceText(item.Text);
                for (int row = 0; row < item.Lines[0].Length; row++)
                    Text(item.Lines[0][row], box.X + 14, box.Y + 40 + row * 26, 16, Mix(Theme.Text, Theme.AccentSoft, .22f), max: box.W - 28, unified: true);
            }
            else
            {
                bool heading = item.Kind == "heading", note = item.Kind == "callout", list = item.Kind == "list-item";
                if (note)
                {
                    Canvas.Fill(box, Theme.PanelRaised);
                    Canvas.Fill(new(box.X, box.Y, 3, box.H), DocsAccent);
                }
                if (list) Canvas.Fill(new(box.X + 3, box.Y + 11, 4, 4), DocsMuted);
                for (int row = 0; row < item.Lines[0].Length; row++)
                    Text(item.Lines[0][row], box.X + (note ? 14 : list ? 20 : 0), box.Y + (note ? 10 : 0) + row * (heading ? 30 : 28),
                        heading ? 20 : 17, heading ? DocsText : HelpBodyColor, max: box.W - (note ? 28 : list ? 20 : 0), unified: true, bold: heading);
            }
        }
        Canvas.Clip(null);
    }

    /// <summary>
    /// 画本页目录。高亮项取“顶端不超过当前滚动位置 + 10px”的最后一个 anchor，这 10px 容差是为了
    /// 刚好滚到标题上沿时就切换高亮，而不是差几像素还停在上一节。
    /// </summary>
    void DrawReferenceOutline()
    {
        var r = referenceOutlineRect;
        Text("本页目录", r.X + 12, r.Y + 4, 14, DocsText, max: r.W - 24, unified: true, bold: true);
        var list = new Rect(r.X + 5, r.Y + 39, r.W - 10, Math.Max(1, r.H - 39));
        referenceOutlineMax = Math.Max(0, referencePage.Anchors.Length * 37 - list.H);
        referenceOutlineScroll = Math.Clamp(referenceOutlineScroll, 0, referenceOutlineMax);
        float scroll = motion.To("docs-toc-scroll", referenceOutlineScroll, .14);
        int current = -1;
        for (int i = 0; i < referencePage.Anchors.Length; i++)
            if (referencePage.Anchors[i].Top <= referenceBodyShown + 10) current = i;
        Canvas.Clip(list);
        for (int i = 0; i < referencePage.Anchors.Length; i++)
        {
            var anchor = referencePage.Anchors[i];
            var row = new Rect(list.X, list.Y + i * 37 - scroll, list.W, 35);
            if (row.Y + row.H < list.Y || row.Y > list.Y + list.H) continue;
            bool over = referenceOpen && list.Contains(mouseX, mouseY) && row.Contains(mouseX, mouseY);
            float active = motion.To($"docs-anchor:{referenceSelected}:{i}", current == i ? 1 : 0, .16, 0);
            Canvas.Fill(new(row.X + 1, row.Y, 2, row.H), Mix(Theme.Border, DocsAccent, active));
            Text(EditorHelpLayout.FitTitle(anchor.Title, row.W - 18, s => fonts.Measure(s, 14, unified: true)),
                row.X + 12, row.Y + 5, 14, over ? DocsText : Mix(DocsMuted, DocsAccent, active), unified: true);
            if (over && click)
            {
                referenceBodyScroll = Math.Clamp(anchor.Top, 0, referenceBodyMax);
                if (referenceOutlineOpen) referenceOutlineOpen = false;
                click = false;
            }
        }
        Canvas.Clip(null);
    }

    /// <summary>
    /// 页脚翻页。同级页面取同 Kind 且同 ParentId 的条目，section 例外——所有 section 互为同级。
    /// 顺序沿用 VsmReference.Shared.Entries 的原始顺序，不另行排序。
    /// </summary>
    void DrawReferencePageFooter(VsmReference.Entry entry, Rect rect)
    {
        bool footerInput = !(referenceOutlineCoversArticle && referenceOutlineRect.Contains(mouseX, mouseY));
        var siblings = VsmReference.Shared.Entries.Where(e => e.Kind == entry.Kind &&
            (entry.Kind == "section" || e.ParentId == entry.ParentId)).ToArray();
        int at = Array.FindIndex(siblings, e => e.Id == entry.Id);
        if (ReferenceButton("上一页", new(rect.X, rect.Y, 88, 31), enabled: footerInput && at > 0)) SelectReference(siblings[at - 1].Id, true);
        if (entry.ParentId.Length > 0 && ReferenceButton("返回章节", new(rect.X + 99, rect.Y, 100, 31), enabled: footerInput)) SelectReference(entry.ParentId, true);
        if (ReferenceButton("下一页", new(rect.X + rect.W - 88, rect.Y, 88, 31), enabled: footerInput && at >= 0 && at + 1 < siblings.Length))
            SelectReference(siblings[at + 1].Id, true);
    }
}
