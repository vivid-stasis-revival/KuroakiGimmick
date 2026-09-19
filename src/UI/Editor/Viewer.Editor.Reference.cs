using System.Globalization;
using System.Runtime.InteropServices;
using KuroakiGimmick.Core.Documentation;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

/// <summary>
/// VSM 参考文档阅读器的状态与输入（F1 / VSM DOCS）。阻塞覆盖层：可见期间工作区完全不接受输入。
/// 阅读状态、历史与动画全部是纯 UI 状态，绝不进入编辑文档或游戏时间轴。
/// </summary>
public sealed partial class Viewer
{
    // 本地离线文档阅读器：章节树 / 正文 / 页面大纲。
    // 阅读状态与动画永远不会进入编辑文档或游戏时间轴。
    bool referenceOpen, referenceSearchFocus, referenceSelectAll, referenceOutlineOpen, referenceOutlineHidden, referenceOutlineCoversArticle;
    string referenceQuery = "", referenceSelected = "vsm.prepare", referenceComposition = "";
    string referenceStatus = "", referenceLayoutKey = "";
    double referenceStatusUntil;
    float referenceAlpha, referenceListScroll, referenceListMax, referenceBodyScroll, referenceBodyMax;
    float referenceListShown, referenceBodyShown, referenceOutlineScroll, referenceOutlineMax;
    Rect referenceSearchRect, referenceListRect, referenceBodyRect, referenceOutlineRect;
    string referenceDrag = "";
    float referenceDragOffset;
    readonly HashSet<string> referenceExpanded = new(StringComparer.Ordinal);
    sealed record ReferenceNav(string Label, string Id, int Depth, bool Branch, bool Group);
    readonly List<ReferenceNav> referenceNav = [];
    VsmReference.Entry[] referenceResults = [];
    ReferenceArticleLayout.Page referencePage = new([], [], 1);
    sealed record ReferenceVisit(string Id, float Scroll);
    readonly List<ReferenceVisit> referenceBack = [], referenceForward = [];
    const float ReferenceNavHeight = 34;
    bool ReferenceVisible => referenceOpen || referenceAlpha > .001f;

    /// <summary>打开文档。模态或拖拽进行中拒绝。会清掉 seeking / held / click，避免打开这一下被下层当成拖动播放头。</summary>
    void OpenReference(string? entryId = null)
    {
        if (modalActive || pendingDiscard != null || editDrag != null) return;
        transport.SetPlaying(false);
        help = false;
        trackHelpWHeld = false;
        editorScrub = seeking = held = click = false;
        referenceOpen = true;
        referenceSearchFocus = referenceSelectAll = false;
        referenceComposition = referenceStatus = "";
        if (entryId != null)
        {
            referenceQuery = "";
            SelectReference(entryId, ensureVisible: true);
        }
        if (VsmReference.Shared.Find(referenceSelected) == null)
            referenceSelected = VsmReference.Shared.Entries.FirstOrDefault()?.Id ?? "";
        BuildReferenceNavigation();
        Sdl.SDL_StopTextInput(host.Window);
    }

    /// <summary>
    /// 按上下文打开文档：优先用当前悬停 / 正显示帮助的轨道名，否则用选中片段的 mod 名。
    /// 查不到对应条目就打开默认页，而不是拒绝打开。
    /// </summary>
    void OpenContextReference()
    {
        EditTrack? helpTrack = hoveredEditTrack ?? (trackHelpVisualActive ? displayedHelpTrack : null);
        string? name = helpTrack is { Window: false } track ? track.Property :
            selectedClip is Guid id ? editor?.Vsm.Find(id)?.Name : null;
        OpenReference(name == null ? null : VsmReference.Shared.MatchMod(name).FirstOrDefault()?.Entry.Id);
    }
    public void OpenReferenceForTest(string? entryId = null) => OpenReference(entryId);
    void CloseReference()
    {
        referenceAddId = "";
        referenceOpen = referenceSearchFocus = referenceSelectAll = false;
        referenceComposition = referenceDrag = "";
        held = click = seeking = editorScrub = false;
        Sdl.SDL_StopTextInput(host.Window);
    }
    void FocusReferenceSearch(bool focus, bool selectAll = false)
    {
        referenceSearchFocus = focus;
        referenceSelectAll = selectAll;
        referenceComposition = "";
        if (focus) Sdl.SDL_StartTextInput(host.Window);
        else Sdl.SDL_StopTextInput(host.Window);
    }
    /// <summary>
    /// 构建左侧导航。有搜索词时是平铺的结果列表，否则按两份源文档分组成章节树，
    /// 顺序即源文档顺序。
    /// </summary>
    void BuildReferenceNavigation()
    {
        var catalogue = VsmReference.Shared;
        referenceResults = catalogue.Search(referenceQuery);
        referenceNav.Clear();
        if (!string.IsNullOrWhiteSpace(referenceQuery))
        {
            foreach (var e in referenceResults) referenceNav.Add(new(e.Name, e.Id, 0, false, false));
            return;
        }
        foreach (string source in new[] { "vsm格式说明.md", "Custom Gimmick说明.md" })
        {
            referenceNav.Add(new(source.StartsWith("vsm", StringComparison.Ordinal) ? "VSM" : "Custom Gimmick", "", 0, false, true));
            foreach (var section in catalogue.Entries.Where(e => e.Kind == "section" && e.Source == source))
            {
                referenceNav.Add(new(section.Name, section.Id, 0, true, false));
                if (referenceExpanded.Contains(section.Id))
                    foreach (var child in catalogue.Entries.Where(e => e.ParentId == section.Id))
                        referenceNav.Add(new(child.Name, child.Id, 1, false, false));
            }
        }
    }
    /// <summary>
    /// 切换当前条目。history 为 false 时不记历史，供前进 / 后退自身调用，免得递归污染历史栈；
    /// 历史上限 100 条，超出从最旧一端丢弃。目标不存在时静默忽略，不清空当前页面。
    /// </summary>
    void SelectReference(string id, bool ensureVisible = false, bool history = true)
    {
        if (VsmReference.Shared.Find(id) is not { } entry) return;
        if (history && referenceSelected != id)
        {
            referenceBack.Add(new(referenceSelected, referenceBodyScroll));
            if (referenceBack.Count > 100) referenceBack.RemoveAt(0);
            referenceForward.Clear();
        }
        if (referenceSelected != id)
        {
            referenceSelected = id;
            referenceBodyScroll = referenceBodyShown = 0;
            motion.Snap("docs-scroll", 0);
            motion.Snap("docs-page", 0);
            referenceLayoutKey = "";
            referenceOutlineScroll = 0; motion.Snap("docs-toc-scroll", 0);
        }
        if (entry.ParentId.Length > 0) referenceExpanded.Add(entry.ParentId);
        BuildReferenceNavigation();
        if (ensureVisible)
        {
            int i = referenceNav.FindIndex(row => row.Id == id);
            float top = i * ReferenceNavHeight;
            if (i >= 0 && top < referenceListScroll) referenceListScroll = top;
            else if (i >= 0 && top + ReferenceNavHeight > referenceListScroll + referenceListRect.H)
                referenceListScroll = Math.Max(0, top + ReferenceNavHeight - Math.Max(100, referenceListRect.H));
        }
    }
    /// <summary>前进 / 后退。会连同当时的滚动位置一起还原，回到原来读到的地方而不是跳回页首。</summary>
    void ReferenceHistory(bool forward)
    {
        var from = forward ? referenceForward : referenceBack;
        var to = forward ? referenceBack : referenceForward;
        if (from.Count == 0) return;
        var visit = from[^1]; from.RemoveAt(from.Count - 1);
        to.Add(new(referenceSelected, referenceBodyScroll));
        referenceQuery = "";
        SelectReference(visit.Id, true, false);
        referenceBodyScroll = referenceBodyShown = visit.Scroll;
        motion.Snap("docs-scroll", visit.Scroll);
    }
    /// <summary>更新搜索词。换行与制表符折成空格；长度上限按字素簇 256 个截断，绝不在代理对中间切开。</summary>
    void ChangeReferenceQuery(string text)
    {
        text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        var starts = StringInfo.ParseCombiningCharacters(text);
        if (starts.Length > 256) text = text[..starts[256]];
        referenceQuery = text;
        referenceSelectAll = false;
        referenceListScroll = 0;
        BuildReferenceNavigation();
    }
    /// <summary>复制的是原文，不是折行后的显示行。提示文字 2.5 秒后自动消失。</summary>
    void CopyReferenceText(string text)
    {
        referenceStatus = Sdl.SDL_SetClipboardText(text) ? "已复制" : "复制失败：" + Sdl.Error;
        referenceStatusUntil = uptime.Elapsed.TotalSeconds + 2.5;
        motion.Snap("docs-toast", 0);
    }
    /// <summary>
    /// 文档阅读器的输入。搜索框获得焦点时走 SDL 文本输入，IME 组字期间（referenceComposition 非空）
    /// 一律不处理按键，否则会把候选阶段的按键当成快捷键。始终返回 true，事件不下传。
    /// </summary>
    bool HandleReferenceInput(Sdl.Event e)
    {
        if (!referenceOpen) return true; // 淡出期间的像素也不能穿透点到工作区。
        if (HandleReferenceAddMenu(e)) return true;
        if (e.Type == 0x400)
        {
            mouseX = e.X; mouseY = e.Y;
            if (referenceDrag.Length > 0) DragReferenceScrollbar();
        }
        else if (e.Type == 0x401 && e.Button == 1)
        {
            mouseX = e.X; mouseY = e.Y; held = false; click = true;
            if (BeginReferenceScrollbarDrag()) { click = false; return true; }
            FocusReferenceSearch(referenceSearchRect.Contains(mouseX, mouseY));
        }
        else if (e.Type == 0x402 && e.Button == 1) { referenceDrag = ""; held = false; }
        else if (e.Type == 0x302 && referenceSearchFocus) referenceComposition = Marshal.PtrToStringUTF8(e.TextData) ?? "";
        else if (e.Type == 0x303 && referenceSearchFocus)
        {
            ChangeReferenceQuery((referenceSelectAll ? "" : referenceQuery) + (Marshal.PtrToStringUTF8(e.TextData) ?? ""));
            referenceComposition = "";
        }
        else if (e.Type == 0x403)
        {
            if (referenceListRect.Contains(mouseX, mouseY))
                referenceListScroll = Math.Clamp(referenceListScroll - e.WheelY * 58, 0, referenceListMax);
            else if (referenceOutlineRect.Contains(mouseX, mouseY))
                referenceOutlineScroll = Math.Clamp(referenceOutlineScroll - e.WheelY * 48, 0, referenceOutlineMax);
            else if (referenceBodyRect.Contains(mouseX, mouseY))
                referenceBodyScroll = Math.Clamp(referenceBodyScroll - e.WheelY * 72, 0, referenceBodyMax);
        }
        else if (e.Type == 0x300)
        {
            bool command = (e.Modifiers & 0x0CC0) != 0, alt = (e.Modifiers & 0x0300) != 0;
            if (referenceComposition.Length > 0 && referenceSearchFocus) return true;
            if (e.Scan is 41 or 58) { if (e.Repeat == 0) CloseReference(); return true; }
            if ((command && e.Scan == 9) || (!referenceSearchFocus && e.Scan == 56))
            { FocusReferenceSearch(true, true); return true; }
            if (alt && (e.Scan is 79 or 80)) { ReferenceHistory(e.Scan == 79); return true; }
            if (referenceSearchFocus)
            {
                if (command && e.Scan == 4) { referenceSelectAll = true; return true; }
                if (command && e.Scan == 25)
                {
                    nint pointer = Sdl.SDL_GetClipboardText();
                    try
                    {
                        string text = Marshal.PtrToStringUTF8(pointer) ?? "";
                        if (text.Length <= 65536) ChangeReferenceQuery((referenceSelectAll ? "" : referenceQuery) + text);
                    }
                    finally { Sdl.SDL_free(pointer); }
                    return true;
                }
                if (e.Scan is 42 or 76)
                {
                    int[] starts = StringInfo.ParseCombiningCharacters(referenceQuery);
                    ChangeReferenceQuery(referenceSelectAll || starts.Length == 0 ? "" : referenceQuery[..starts[^1]]);
                    return true;
                }
                if (e.Scan is 40 or 88)
                {
                    if (referenceResults.Length > 0) SelectReference(referenceResults[0].Id, true);
                    FocusReferenceSearch(false); return true;
                }
            }
            if (e.Scan is 81 or 82)
            {
                var ids = referenceNav.Where(row => !row.Group).Select(row => row.Id).ToArray();
                int at = Array.IndexOf(ids, referenceSelected);
                if (ids.Length > 0) SelectReference(ids[Math.Clamp(at + (e.Scan == 81 ? 1 : -1), 0, ids.Length - 1)], true);
            }
            else if (e.Scan is 75 or 78)
                referenceBodyScroll = Math.Clamp(referenceBodyScroll + (e.Scan == 78 ? 1 : -1) * Math.Max(60, referenceBodyRect.H - 40), 0, referenceBodyMax);
            else if (!referenceSearchFocus && e.Scan == 74) referenceBodyScroll = 0;
            else if (!referenceSearchFocus && e.Scan == 77) referenceBodyScroll = referenceBodyMax;
        }
        return true;
    }

    /// <summary>
    /// 在右侧 12 像素的滚动条区域内按下才算开始拖动。按在滑块本体上保持抓取偏移，
    /// 按在轨道空白处则让滑块中心对齐指针。大纲遮住正文时不接管正文滚动条。
    /// </summary>
    bool BeginReferenceScrollbarDrag()
    {
        foreach (var (name, rect, max, shown) in new[]
        { ("list", referenceListRect, referenceListMax, referenceListShown), ("body", referenceBodyRect, referenceBodyMax, referenceBodyShown) })
        {
            if (max <= 0 || (referenceOutlineCoversArticle && referenceOutlineRect.Contains(mouseX, mouseY)) || !new Rect(rect.X + rect.W - 12, rect.Y, 12, rect.H).Contains(mouseX, mouseY)) continue;
            float size = Math.Clamp(rect.H * rect.H / (rect.H + max), Math.Min(28, rect.H), rect.H);
            float top = rect.Y + (rect.H - size) * shown / max;
            referenceDrag = name;
            referenceDragOffset = mouseY >= top && mouseY <= top + size ? mouseY - top : size / 2;
            DragReferenceScrollbar(); return true;
        }
        return false;
    }
    /// <summary>拖动时同时写目标值与显示值并 Snap 动画，让滑块紧跟指针，而不是慢一拍地缓动过去。</summary>
    void DragReferenceScrollbar()
    {
        bool list = referenceDrag == "list";
        var rect = list ? referenceListRect : referenceBodyRect;
        float max = list ? referenceListMax : referenceBodyMax;
        float size = Math.Clamp(rect.H * rect.H / Math.Max(1, rect.H + max), Math.Min(28, rect.H), rect.H);
        float value = Math.Clamp((mouseY - rect.Y - referenceDragOffset) / Math.Max(1, rect.H - size), 0, 1) * max;
        if (list) { referenceListScroll = referenceListShown = value; motion.Snap("docs-nav-scroll", value); }
        else { referenceBodyScroll = referenceBodyShown = value; motion.Snap("docs-scroll", value); }
    }
}
