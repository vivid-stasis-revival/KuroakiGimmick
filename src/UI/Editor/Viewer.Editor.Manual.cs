using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

/// <summary>
/// VSM 语法手册浏览器（F1）。阻塞覆盖层：显示期间下面的工作区不接受键盘和滚轮。
/// 它只读，任何操作都不会创建事件或改动谱面。
/// </summary>
public sealed partial class Viewer
{
    // 这个浏览器是覆盖在源文档目录之上的只读模态视图。搜索框复用编辑器的 Unicode 输入对话框，
    // 这样 IME 和粘贴不会被当成时间轴快捷键。
    string manualQuery = "", manualCategory = "";
    string manualCacheKey = "";
    EditorManual.Entry[] manualResults = [];
    HelpRow[] manualRows = [];
    int manualSelected, manualListStart, manualVisible = 1;
    float manualScroll, manualScrollMax, manualTotalHeight;
    Rect manualListRect, manualBodyRect;

    /// <summary>打开手册，可选地直接定位到某个 identifier。模态或拖拽进行中拒绝打开；打开时顺手清掉 W 长按状态，免得帮助卡片留在后面。</summary>
    void OpenManual(string? identifier = null)
    {
        if (modalActive || pendingDiscard != null || editDrag != null) return;
        transport.SetPlaying(false);
        trackHelpWHeld = false;
        trackHelpWDownAt = 0;
        help = true;
        click = false;
        if (identifier != null && EditorManual.LookupMod(identifier) is { } match)
        {
            manualCategory = "";
            manualQuery = match.Entry.Key;
            RefreshManual();
        }
        else if (manualResults.Length == 0) RefreshManual();
    }

    /// <summary>重新搜索并把选择、滚动、排版缓存一并归零；否则会停在新结果集里不存在的下标上。</summary>
    void RefreshManual()
    {
        manualResults = EditorManual.Search(manualQuery, manualCategory);
        manualSelected = manualListStart = 0;
        manualScroll = manualScrollMax = 0;
        manualCacheKey = "";
    }

    void SearchManual()
    {
        OpenValue(L.Get("Search manual: identifier / Chinese / obj / mpf"), manualQuery, value =>
        {
            manualQuery = value;
            manualCategory = "";
            RefreshManual();
        });
    }

    /// <summary>切换选中条目并把它滚进可视区。索引被夹到合法范围，空结果集时停在 0。</summary>
    void SelectManual(int index)
    {
        int next = Math.Clamp(index, 0, Math.Max(0, manualResults.Length - 1));
        if (manualSelected == next) return;
        manualSelected = next;
        manualCacheKey = "";
        manualScroll = 0;
        if (next < manualListStart) manualListStart = next;
        else if (next >= manualListStart + manualVisible) manualListStart = next - manualVisible + 1;
    }

    void CycleManualCategory(int direction)
    {
        string[] categories = ["", .. EditorManual.Categories];
        int index = Array.IndexOf(categories, manualCategory);
        manualCategory = categories[(Math.Max(0, index) + direction + categories.Length) % categories.Length];
        RefreshManual();
    }

    /// <summary>手册可见时吞掉一切键盘与滚轮，只让鼠标位置 / 按键状态继续更新。目录与正文各自独立滚动。</summary>
    bool HandleManualInput(Sdl.Event e)
    {
        if (e.Type == 0x403)
        {
            if (manualListRect.Contains(mouseX, mouseY))
                manualListStart = Math.Clamp(manualListStart - (int)Math.Round(e.WheelY * 3), 0,
                    Math.Max(0, manualResults.Length - manualVisible));
            else manualScroll = Math.Clamp(manualScroll - e.WheelY * 60, 0, manualScrollMax);
            return true; // 阅读期间绝不拖动播放头，也不滚动下面的时间轴。
        }
        if (e.Type == 0x300)
        {
            bool command = (e.Modifiers & 0x0CC0) != 0;
            if (e.Repeat == 0 && (e.Scan is 41 or 58 or 11)) { help = false; trackHelpWHeld = false; click = false; }
            else if (command && e.Scan == 9 && e.Repeat == 0) SearchManual();
            else if (e.Scan == 81) SelectManual(manualSelected + 1);
            else if (e.Scan == 82) SelectManual(manualSelected - 1);
            else if (e.Scan == 75) manualScroll = Math.Max(0, manualScroll - manualBodyRect.H * .85f);
            else if (e.Scan == 78) manualScroll = Math.Min(manualScrollMax, manualScroll + manualBodyRect.H * .85f);
            else if (e.Scan == 74) manualScroll = 0;
            else if (e.Scan == 77) manualScroll = manualScrollMax;
            return true;
        }
        // 鼠标事件放行给共用处理器，让它更新窗口空间坐标与按键状态。
        // DrawEditTimeline 已经判断过 !help，下层所有 EButton 也都处于禁用状态。
        return e.Type is not (0x400 or 0x401 or 0x402);
    }

    bool ManualButton(string caption, Rect rect, bool active = false, bool enabled = true)
    {
        bool hover = enabled && rect.Contains(mouseX, mouseY) && !modalActive && pendingDiscard == null;
        Canvas.Fill(rect, active ? Color.Hex(0x4C2839) : hover ? Color.Hex(0x303440) : Color.Hex(0x22252E));
        Canvas.Border(rect, active ? Color.Hex(0xD85B72) : Color.Hex(0x444955));
        Text(caption, rect.X + 10, rect.Y + 5, 14, enabled ? HelpBodyColor : HelpSecondaryColor.Alpha(.5),
            max: rect.W - 20, unified: true);
        if (!hover || !click) return false;
        click = false;
        return true;
    }

    /// <summary>量测并缓存正文排版。缓存键含条目 Id、宽度与字体名，字体换了必须重排，否则会沿用旧字体的量测结果。</summary>
    void BuildManualRows(EditorManual.Entry entry, float width)
    {
        string key = entry.Id + "|" + width + "|" + fonts.HelpFontName;
        if (key == manualCacheKey) return;
        manualCacheKey = key;
        var rows = new List<HelpRow>();
        float top = 0;
        foreach (string paragraph in entry.Detail)
        {
            foreach (string row in EditorHelpLayout.Wrap(paragraph, width,
                s => fonts.Measure(s, HelpBodySize, unified: true)))
            {
                rows.Add(new(row, top));
                top += HelpBodyLineHeight;
            }
            top += entry.Kind == "source" ? 2 : HelpParagraphGap;
        }
        manualRows = rows.ToArray();
        manualTotalHeight = top;
        manualScroll = 0;
    }

    /// <summary>
    /// 只把名字填进"待新增"的 identifier，不创建任何事件、也不切换 obj。
    /// 带占位符的模板名必须由用户替换成具体名字并通过模板正则校验才接受，绝不把 [tid] 这类原样写进谱面。
    /// </summary>
    void UseManualName(EditorManual.Entry entry)
    {
        void Choose(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { ',', '\r', '\n' }) >= 0 ||
                new[] { "[tid]", "[lane]", "[id]", "[图像名]" }.Any(p => name.Contains(p, StringComparison.Ordinal)))
                throw new FormatException(L.Get("Enter a concrete raw identifier, not an unresolved template."));
            if (entry.Pattern.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(name, entry.Pattern,
                System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                throw new FormatException(L.Get("The identifier does not match the selected document template."));
            customMod = name;
            if (entry.Scope == "global") newProxy = -1;
            else if (entry.Scope == "proxy" && Current.Chart.Proxies > 0) newProxy = Math.Clamp(newProxy, 0, Current.Chart.Proxies - 1);
            help = false;
            click = false;
            message = L.Get("Selected identifier only (no event created). Check target, object and compatibility before adding.");
        }
        if (entry.Pattern.Length > 0) OpenValue(L.Get("Concrete VSM identifier (replace bracketed placeholder)"), entry.Key, Choose);
        else Choose(entry.Key);
    }

    /// <summary>画手册界面：左侧目录 + 右侧正文，两栏各自滚动。</summary>
    void DrawVsmManual(int w, int h)
    {
        if (manualResults.Length == 0 && manualQuery.Length == 0 && manualCategory.Length == 0) RefreshManual();
        Canvas.Fill(new(0, 0, w, h), Color.Hex(0, .86f));
        var outer = new Rect(20, 20, Math.Max(1, w - 40), Math.Max(1, h - 40));
        Canvas.Fill(outer, HelpBackground);
        Canvas.Border(outer, Color.Hex(0x55505E));
        float left = outer.X + 20, top = outer.Y + 15;
        Text(L.Get("VSM / CUSTOM GIMMICK 手册"), left, top, HelpTitleSize, HelpBodyColor, max: outer.W - 175,
            unified: true, bold: true);
        if (ManualButton(L.Get("关闭  Esc"), new(outer.X + outer.W - 130, top, 110, 32))) help = false;
        Text(L.Get("附件原文索引 · 收录不代表已支持预览 · ") + EditorManual.Entries.Length + L.Get(" 个条目"), left, top + 38,
            14, HelpSecondaryColor, max: outer.W - 40, unified: true);
        if (ManualButton(L.Get("搜索  Ctrl/Cmd+F"), new(left, top + 69, 170, 34))) SearchManual();
        if (ManualButton(L.Get("清空"), new(left + 180, top + 69, 65, 34)))
        { manualQuery = manualCategory = ""; RefreshManual(); }
        Text(manualQuery.Length > 0 ? manualQuery : L.Get("输入原始名字、中文作用、配置、obj 或 mpf"), left + 260, top + 76,
            16, HelpSummaryColor, max: outer.W - 300, unified: true);

        float listWidth = Math.Clamp(outer.W * .29f, 260, 365);
        float columnTop = top + 119;
        if (ManualButton("<", new(left, columnTop, 32, 34))) CycleManualCategory(-1);
        if (ManualButton(manualCategory.Length == 0 ? L.Get("全部分类 >") : manualCategory,
            new(left + 40, columnTop, listWidth - 40, 34))) CycleManualCategory(1);
        manualListRect = new(left, columnTop + 46, listWidth, Math.Max(1, outer.Y + outer.H - 56 - columnTop - 46));
        Canvas.Fill(manualListRect, Color.Hex(0x101218));
        manualVisible = Math.Max(1, (int)(manualListRect.H / 61));
        manualListStart = Math.Clamp(manualListStart, 0, Math.Max(0, manualResults.Length - manualVisible));
        Canvas.Clip(manualListRect);
        for (int row = 0; row < manualVisible && manualListStart + row < manualResults.Length; row++)
        {
            int index = manualListStart + row;
            var entry = manualResults[index];
            var rect = new Rect(left, manualListRect.Y + row * 61, listWidth - 9, 58);
            bool hover = !modalActive && rect.Contains(mouseX, mouseY);
            Canvas.Fill(rect, index == manualSelected ? Color.Hex(0x442836) : hover ? Color.Hex(0x252A35) : Color.Hex(0x191C24));
            string title = EditorHelpLayout.FitTitle(entry.Key, rect.W - 18,
                s => fonts.Measure(s, 16, unified: true));
            Text(title, rect.X + 9, rect.Y + 5, 16, HelpBodyColor, max: rect.W - 18, unified: true);
            string summary = EditorHelpLayout.FitTitle(entry.Short.Replace('\n', ' '), rect.W - 18,
                s => fonts.Measure(s, 12, unified: true));
            Text(summary, rect.X + 9, rect.Y + 32, 12, HelpSecondaryColor, max: rect.W - 18, unified: true);
            if (hover && click) { SelectManual(index); click = false; }
        }
        Canvas.Clip(null);
        DrawManualScrollbar(manualListRect, manualListStart, Math.Max(0, manualResults.Length - manualVisible),
            manualVisible, Math.Max(1, manualResults.Length));

        float contentX = left + listWidth + 25, contentWidth = Math.Max(1, outer.X + outer.W - 24 - contentX);
        Canvas.Fill(new(contentX - 13, columnTop, 1, manualListRect.Y + manualListRect.H - columnTop), Color.Hex(0x3E424D));
        if (manualResults.Length == 0)
        {
            Text(EditorManual.LoadError.Length > 0 ? L.Get("手册加载失败：") + EditorManual.LoadError : L.Get("没有找到条目。请清空分类或换个搜索词。"),
                contentX, columnTop + 10, 17, HelpBodyColor, max: contentWidth, unified: true);
        }
        else
        {
            var entry = manualResults[Math.Clamp(manualSelected, 0, manualResults.Length - 1)];
            string heading = EditorHelpLayout.FitTitle(entry.Key, contentWidth,
                s => fonts.Measure(s, HelpTitleSize, unified: true, bold: true));
            Text(heading, contentX, columnTop, HelpTitleSize, HelpBodyColor, max: contentWidth, unified: true, bold: true);
            Text(entry.Category + " · " + entry.Citation, contentX, columnTop + 38, 13, HelpSecondaryColor,
                max: contentWidth, unified: true);
            if (ManualButton(L.Get("原文"), new(contentX, columnTop + 67, 72, 32), enabled: entry.Source.Length > 0))
            {
                var original = EditorManual.Entries.FirstOrDefault(e => e.Kind == "source" && e.Source == entry.Source);
                if (original != null)
                {
                    manualCategory = "原文全文";
                    manualQuery = "";
                    RefreshManual();
                    SelectManual(Array.FindIndex(manualResults, e => e.Id == original.Id));
                }
            }
            if (ManualButton(L.Get("选用名字"), new(contentX + 82, columnTop + 67, 112, 32), enabled: entry.Kind == "mod"))
                UseManualName(entry);
            Text(L.Get("只选名字，不自动创建事件或切换 obj"), contentX + 210, columnTop + 74, 12, HelpSecondaryColor,
                max: contentWidth - 210, unified: true);
            manualBodyRect = new(contentX, columnTop + 115, contentWidth,
                Math.Max(1, manualListRect.Y + manualListRect.H - columnTop - 115));
            BuildManualRows(entry, contentWidth - 15);
            manualScrollMax = Math.Max(0, manualTotalHeight - manualBodyRect.H);
            manualScroll = Math.Clamp(manualScroll, 0, manualScrollMax);
            Canvas.Clip(manualBodyRect);
            foreach (var row in manualRows)
            {
                float y = manualBodyRect.Y + row.Top - manualScroll;
                if (y + HelpBodyLineHeight <= manualBodyRect.Y || y >= manualBodyRect.Y + manualBodyRect.H) continue;
                Text(row.Text, contentX, y, HelpBodySize, HelpBodyColor, max: contentWidth - 15, unified: true);
            }
            Canvas.Clip(null);
            DrawManualScrollbar(manualBodyRect, manualScroll, manualScrollMax, manualBodyRect.H, manualTotalHeight);
        }
        Text(L.Format($"{manualResults.Length} 个结果 · 目录/正文分别滚动 · ↑↓选择 · PgUp/PgDn 翻页 · Home/End 首尾"),
            left, outer.Y + outer.H - 35, 13, HelpSecondaryColor, max: outer.W - 40, unified: true);
        // 末尾吞掉未被任何按钮消费的点击，防止它穿透到下面的片段选择上。
        if (!modalActive) click = false;
    }

    void DrawManualScrollbar(Rect rect, float position, float maximum, float visible, float total)
    {
        if (maximum <= 0 || total <= 0 || rect.H <= 0) return;
        float thumb = Math.Min(rect.H, Math.Max(20, rect.H * visible / total));
        float y = rect.Y + (rect.H - thumb) * Math.Clamp(position / maximum, 0, 1);
        Canvas.Fill(new(rect.X + rect.W - 4, rect.Y, 3, rect.H), Color.Hex(0x303642));
        Canvas.Fill(new(rect.X + rect.W - 4, y, 3, thumb), HelpSecondaryColor);
    }
}
