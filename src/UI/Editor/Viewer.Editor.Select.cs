using System.Globalization;
using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;
namespace KuroakiGimmick.UI;

/// <summary>
/// 时间轴上的批量选择与右键交互：左键空白处拖出选框多选、同一 mod 的批量改参、右键拖拽平移视图、
/// 右键片段弹出菜单。选择集是唯一事实来源，selectedClip 只是其中的"主选中项"，决定 inspector 显示谁。
/// 批量修改一律先把所有结果算完再进一次 Change：任何一个值非法都会在改动文档之前抛出，撤销也只需要一步。
/// </summary>
public sealed partial class Viewer
{
    /// <summary>批量选择集。不变量：selectedClip 非空且指向 VSM 片段时，它一定也在这里面。</summary>
    readonly HashSet<Guid> selectedClips = [];
    /// <summary>选框拖拽。marqueeBase 是按下那一刻的选择集，按住 Shift 时新命中的片段并入其中而不是替换。</summary>
    bool marqueeActive;
    float marqueeFromX, marqueeFromY;
    Rect? marqueePending;
    readonly HashSet<Guid> marqueeBase = [];
    /// <summary>右键拖拽平移。panMoved 一旦置位，松手时就不再弹菜单 —— 用户是在拖视图，不是在点东西。</summary>
    bool panActive, panMoved;
    float panFromX, panFromY, panScroll;
    double panBeat;
    /// <summary>右键菜单。menuInput 的作用与 modalInput 完全一样：菜单自己的按钮放行，下面的工作区禁用。</summary>
    bool menuOpen, menuInput;
    float menuX, menuY;
    Rect menuRect;
    Guid? menuClip;
    EditTrack? menuTrack;
    double menuBeat;
    /// <summary>"复制参数 / 粘贴参数"用的内部剪贴板，只带可跨片段复用的字段，不含拍位与身份。</summary>
    (string Ease, string From, string To, double Duration)? clipValues;
    /// <summary>本帧画出来的片段矩形，按绘制顺序。选框求交与右键命中都用它，几何只在 DrawEventClip 里算一次。</summary>
    readonly List<(Guid Id, Rect Rect)> clipHits = [];
    /// <summary>本帧画出来的轨道行矩形。右键落在空白处时用它判断是哪条轨道，好把"在这里新建"指向正确的 mod。</summary>
    readonly List<(EditTrack Track, Rect Rect)> rowHits = [];

    /// <summary>
    /// 批量选择是否成立：集合里不止一个片段，且主选中项确实在其中。
    /// 判定写成派生量而不是存一个布尔标志，是因为项目里有十几处直接写 selectedClip 的旧路径；
    /// 它们一改主选中项，这里立刻就不成立，UpdateEditor 随后把集合清掉。
    /// </summary>
    bool BatchSelection => selectedClips.Count > 1 && selectedClip is Guid g && selectedClips.Contains(g);

    /// <summary>Rect 只有 Contains，没有相交判断；选框求交是这里独有的需求，helper 就留在本文件。</summary>
    static bool Overlaps(Rect a, Rect b) => a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;

    /// <summary>选择集里真实存在的片段，按源文件顺序。</summary>
    VsmDocument.Clip[] SelectedClipList() =>
        editor == null ? [] : editor.Vsm.Clips.Where(c => selectedClips.Contains(c.Id)).ToArray();

    /// <summary>把选择集收敛成单个片段（或清空）。所有"只选中一个"的入口都走这里，保证两份状态不会分叉。</summary>
    void SetClipSelection(Guid? id)
    {
        selectedClips.Clear();
        if (id is Guid g) selectedClips.Add(g);
        selectedClip = id;
    }

    /// <summary>
    /// 点中片段时更新选择集。按住 Shift / command 切换该片段的成员资格；
    /// 点一个已经在批量选择里的片段则保留整组不变 —— 否则想整体拖动就永远只能拖到一个。
    /// </summary>
    void ClipClicked(Guid id, bool additive)
    {
        if (additive)
        {
            if (!selectedClips.Add(id))
            {
                selectedClips.Remove(id);
                selectedClip = selectedClips.Count > 0 ? selectedClips.First() : null;
                return;
            }
        }
        else if (!selectedClips.Contains(id)) { selectedClips.Clear(); selectedClips.Add(id); }
        selectedClip = id;
    }

    /// <summary>选择同一条轨道（同 mod、同 proxy）上的全部片段，批量改参最常用的起手式。</summary>
    void SelectSameTrack(VsmDocument.Clip clip)
    {
        if (editor == null) return;
        selectedClips.Clear();
        foreach (var c in editor.Vsm.Clips.Where(c => c.TrackKey == clip.TrackKey)) selectedClips.Add(c.Id);
        selectedClip = clip.Id; inspectorScroll = 0;
        message = L.Format($"Selected {selectedClips.Count} {clip.Name} clips.");
    }

    /// <summary>
    /// 批量修改。先把所有结果算完再提交：apply 对任何一个片段抛异常，都发生在文档被改动之前，
    /// 因此要么整批生效要么完全不动，撤销栈上也只留一步。
    /// </summary>
    void EditClips(Func<VsmDocument.Clip, VsmDocument.Clip> apply, string name)
    {
        if (editor == null || Busy) return;
        var targets = SelectedClipList();
        if (targets.Length == 0) return;
        var next = targets.Select(apply).ToArray();
        editor.Change(name, () => { foreach (var c in next) editor.Vsm.Replace(c); });
        layoutRevision = -1; message = L.Format($"{name} / {next.Length} clips");
    }

    /// <summary>批量删除，一步撤销。</summary>
    void DeleteClips()
    {
        if (editor == null) return;
        var ids = SelectedClipList().Select(c => c.Id).ToArray();
        if (ids.Length == 0) return;
        Edit(L.Format($"Delete {ids.Length} clips"), () => { foreach (var id in ids) editor.Vsm.Delete(id); });
        SetClipSelection(null);
    }

    /// <summary>
    /// 批量复制到插入点。整组按最早的那一拍对齐搬到插入点，组内相对间距原样保留；
    /// 落点与原件重合时整组顺延到原件之后，免得两份完全叠在一起看不出来。
    /// </summary>
    void DuplicateClips()
    {
        if (editor == null) return;
        var source = SelectedClipList();
        if (source.Length == 0) return;
        double first = source.Min(c => c.Beat), destination = InsertionBeat;
        if (Math.Abs(destination - first) < 1e-7) destination += Math.Max(.25, source.Max(c => c.End) - first);
        double delta = destination - first;
        var copies = source.Select(c => c with { Id = Guid.NewGuid(), Beat = c.Beat + delta, RepeatEnd = c.RepeatEnd + delta }).ToArray();
        Edit(L.Format($"Duplicate {copies.Length} clips"), () => { foreach (var c in copies) editor.Vsm.Add(c); });
        selectedClips.Clear();
        foreach (var c in copies) selectedClips.Add(c.Id);
        selectedClip = copies[0].Id;
    }

    void ConvertSelectedLoops(bool split)
    {
        if (editor == null || Busy) return;
        var ids = SelectedClipList().Select(c => c.Id).ToArray();
        Guid[]? next = null;
        Edit(split ? L.Get("Split loops") : L.Get("Merge loop"), () =>
            next = split ? editor.Vsm.SplitLoops(ids) : [editor.Vsm.MergeLoop(ids)]);
        if (next == null) return;
        selectedClips.Clear();
        foreach (var id in next) selectedClips.Add(id);
        selectedClip = next.FirstOrDefault();
        inspectorScroll = 0;
    }

    // ---- 选框 ----

    /// <summary>选框的当前矩形，由按下点与当前指针拉出，并夹在轨道区内。</summary>
    Rect MarqueeRect()
    {
        float x0 = Math.Min(marqueeFromX, mouseX), x1 = Math.Max(marqueeFromX, mouseX);
        float y0 = Math.Min(marqueeFromY, mouseY), y1 = Math.Max(marqueeFromY, mouseY);
        var area = editorTracksRect;
        x0 = Math.Clamp(x0, area.X, area.X + area.W); x1 = Math.Clamp(x1, area.X, area.X + area.W);
        y0 = Math.Clamp(y0, area.Y, area.Y + area.H); y1 = Math.Clamp(y1, area.Y, area.Y + area.H);
        return new(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
    }

    /// <summary>
    /// 在轨道空白处按下左键：开始拉选框。按住 Shift 时保留原有选择做增量，否则先清空。
    /// 必须排在片段命中之后调用 —— 片段自己会先吃掉 click，点在片段上就不会误开选框。
    /// </summary>
    void BeginMarquee()
    {
        marqueeActive = true; marqueeFromX = mouseX; marqueeFromY = mouseY;
        marqueeBase.Clear();
        if ((Sdl.SDL_GetModState() & (3 | 0x0CC0)) != 0) foreach (var id in selectedClips) marqueeBase.Add(id);
        selectedClips.Clear();
        foreach (var id in marqueeBase) selectedClips.Add(id);
        if (selectedClips.Count == 0) selectedClip = null;
        selectedWindowEvent = -1; selectedNoteTime = null;
        transport.SetPlaying(false);
    }

    /// <summary>
    /// 每帧按当前选框重算选择集。clipHits 是本帧刚画出来的几何，所以框到哪里就选到哪里，拖的过程中就能看见。
    /// 滚出可视区的片段不在 clipHits 里，也就不会被框到 —— 框选只对看得见的东西负责。
    /// </summary>
    void UpdateMarquee()
    {
        var band = MarqueeRect();
        marqueePending = band;
        selectedClips.Clear();
        foreach (var id in marqueeBase) selectedClips.Add(id);
        foreach (var (id, rect) in clipHits) if (Overlaps(band, rect)) selectedClips.Add(id);
        if (selectedClip is Guid current && selectedClips.Contains(current)) return;
        // 主选中项被框走了就换成框里最靠前的那个，inspector 不会因为一次框选变成空面板。
        Guid firstHit = clipHits.FirstOrDefault(h => selectedClips.Contains(h.Id)).Id;
        selectedClip = firstHit == Guid.Empty ? null : firstHit;
    }

    /// <summary>松开左键：结束选框。选择集在拖的过程中已经逐帧更新过，这里只收尾并报数。</summary>
    void FinishMarquee()
    {
        if (!marqueeActive) return;
        marqueeActive = false; marqueePending = null; inspectorScroll = 0;
        if (BatchSelection) message = L.Format($"{selectedClips.Count} clips selected. Same mod: edit them together on the right.");
        else if (selectedClips.Count == 0) selectedClip = null;
    }

    /// <summary>画选框本身。半透明填充加一圈实线，落在轨道区内。</summary>
    void DrawMarquee()
    {
        if (marqueePending is not Rect band || band.W < 1 && band.H < 1) return;
        Canvas.Clip(editorTracksRect);
        Canvas.Fill(band, soft.Alpha(.18));
        Canvas.Border(band, white.Alpha(.75));
        Canvas.Clip(null);
    }

    // ---- 右键：拖拽平移 / 松手弹菜单 ----

    /// <summary>
    /// 右键按下。先记下起点，这一下到底是"平移"还是"弹菜单"要等松手时看位移决定 ——
    /// 按下就弹菜单的话，想右键拖动视图的人每次都会先被菜单糊一脸。
    /// </summary>
    void BeginPan(float x, float y)
    {
        panActive = true; panMoved = false; panFromX = x; panFromY = y;
        panBeat = beatStart; panScroll = trackScroll;
    }

    /// <summary>右键拖动：横向按像素换算成拍平移，纵向按 35 像素一行滚动轨道。</summary>
    void UpdatePan(float x, float y)
    {
        if (!panActive) return;
        float dx = x - panFromX, dy = y - panFromY;
        if (Math.Abs(dx) + Math.Abs(dy) > 4) panMoved = true;
        ScrollTimeline(panBeat - dx / pixelsPerBeat);
        trackScroll = Math.Max(0, (int)Math.Round(panScroll - dy / 35));
    }

    /// <summary>
    /// 右键松开。拖过就只是平移；没拖动则在指针处弹菜单，并按指针下方是片段还是空白决定菜单内容。
    /// </summary>
    void FinishPan(float x, float y)
    {
        if (!panActive) return;
        panActive = false;
        if (panMoved || Busy || editor == null) return;
        if (!editorTracksRect.Contains(x, y)) return;
        var hit = clipHits.FirstOrDefault(h => h.Rect.Contains(x, y));
        var row = rowHits.FirstOrDefault(h => h.Rect.Contains(x, y));
        if (hit.Id == Guid.Empty && row.Track == null) return;
        CloseInline();
        menuClip = hit.Id == Guid.Empty ? null : hit.Id;
        menuTrack = row.Track;
        menuBeat = Snap(BeatAt(x));
        // 右键一个不在选择集里的片段：先把它选上再弹菜单，菜单作用的对象和用户看到的高亮始终一致。
        if (menuClip is Guid id && !selectedClips.Contains(id)) { SetClipSelection(id); inspectorScroll = 0; }
        // 真正的菜单矩形要等这一帧画出来才知道（贴边时会翻转），先按未翻转的位置占一个位，
        // 免得菜单还没画过就收到点击时拿旧矩形做命中。
        menuRect = new(x, y, 232, 39);
        menuOpen = true; menuX = x; menuY = y;
        transport.SetPlaying(false);
    }

    void CloseClipMenu() { menuOpen = false; menuClip = null; menuTrack = null; }

    /// <summary>
    /// 组装菜单条目。选中多个时所有条目都作用于整组，条目文字里直接写上数量，不让用户去猜作用范围。
    /// </summary>
    List<(string Label, bool Enabled, Action Run)> BuildClipMenu()
    {
        var items = new List<(string, bool, Action)>();
        if (editor == null) return items;
        var picked = SelectedClipList();
        int count = picked.Length;
        if (menuClip is Guid && count > 0)
        {
            string suffix = count > 1 ? $" ({count})" : "";
            var first = picked[0];
            items.Add((L.Get("SELECT SAME MOD"), true, () => SelectSameTrack(first)));
            items.Add((L.Get("DUPLICATE") + suffix, true, () => { if (count > 1) DuplicateClips(); else DuplicateSelection(); }));
            items.Add((L.Get("SPLIT LOOP") + suffix, picked.Any(c => c.RepeatEnd != null), () => ConvertSelectedLoops(true)));
            items.Add((L.Get("MERGE INTO LOOP") + suffix, count > 1, () => ConvertSelectedLoops(false)));
            items.Add((L.Get("MOVE TO PLAYHEAD") + suffix, true, () =>
            {
                double delta = Current.Timeline.Bpm.Beat(transport.Position) - picked.Min(c => c.Beat);
                EditClips(c => c with { Beat = c.Beat + delta, RepeatEnd = c.RepeatEnd + delta }, L.Get("Move to playhead"));
            }));
            items.Add((L.Get("SWAP FROM / TO") + suffix, true, () => EditClips(c => c with { From = c.To, To = c.From }, L.Get("Swap from/to"))));
            items.Add((L.Get("COPY VALUES"), count == 1, () =>
            { clipValues = (first.Ease, first.From, first.To, first.Duration); message = L.Get("Copied ease / from / to / duration."); }));
            items.Add((L.Get("PASTE VALUES") + suffix, clipValues != null, () =>
            {
                var v = clipValues!.Value;
                EditClips(c => c with { Ease = v.Ease, From = v.From, To = v.To, Duration = v.Duration }, L.Get("Paste values"));
            }));
            items.Add((L.Get("DELETE") + suffix, true, () => { if (count > 1) DeleteClips(); else DeleteSelection(); }));
        }
        else if (menuTrack is { } track)
        {
            string label = track.TextId != null ? L.Get("TEXT KEY") : track.ImageGroup ? L.Get("IMAGE KEY") : track.Window ? track.Property : track.Property;
            items.Add((L.Format($"ADD {label} AT {menuBeat:0.###}"), !Busy, () =>
            {
                if (track.TextId is { } textId) { SelectText(textId); textAt = menuBeat; transport.Seek(Current.Timeline.Bpm.Time(menuBeat)); }
                else if (track.ImageGroup) { SelectImageObject(track.ImageId!); imageKeyBeat = menuBeat; imagePoseTarget = ImagePoseTarget.Key; transport.Seek(Current.Timeline.Bpm.Time(menuBeat)); }
                else if (track.Window) AddWindowEvent(track.Property, menuBeat, track.Target);
                else AddMod(menuBeat, track.Property, track.Target);
            }));
            items.Add((L.Get("SEEK HERE"), true, () => { selectedNoteTime = null; transport.Seek(Current.Timeline.Bpm.Time(menuBeat)); }));
            items.Add((L.Get("MARK HERE"), true, () => { transport.Seek(Current.Timeline.Bpm.Time(menuBeat)); MarkTimestamp(); }));
            items.Add((L.Get("LOOP IN HERE"), true, () => loopIn = menuBeat));
            items.Add((L.Get("LOOP OUT HERE"), true, () => loopOut = Math.Max(loopIn + .125, menuBeat)));
            if (!track.Window && track.TextId == null && !track.ImageGroup)
                items.Add((L.Get("SELECT WHOLE TRACK"), true, () =>
                {
                    selectedClips.Clear();
                    foreach (var c in editor.Vsm.Clips.Where(c => c.TrackKey == track.Key)) selectedClips.Add(c.Id);
                    selectedClip = selectedClips.Count > 0 ? selectedClips.First() : null;
                    selectedWindowEvent = -1; inspectorScroll = 0;
                    message = L.Format($"Selected {selectedClips.Count} clips on {track.Label}.");
                }));
        }
        return items;
    }

    /// <summary>
    /// 画右键菜单。菜单贴着指针出现，靠近窗口边缘时朝内翻转，不会有一半在屏幕外点不到。
    /// 期间 menuInput 置位，菜单自己的按钮可点，其余控件由 EButton 统一禁用。
    /// </summary>
    void DrawClipMenu(int w, int h)
    {
        if (!menuOpen) return;
        var items = BuildClipMenu();
        if (items.Count == 0) { CloseClipMenu(); return; }
        const float itemH = 27, width = 232;
        float height = items.Count * itemH + 12;
        float x = menuX + width > w - 8 ? Math.Max(8, menuX - width) : menuX;
        float y = menuY + height > h - 8 ? Math.Max(8, menuY - height) : menuY;
        menuRect = new(x, y, width, height);
        Canvas.Fill(new(menuRect.X + 3, menuRect.Y + 3, menuRect.W, menuRect.H), Color.Hex(0, .45f));
        Canvas.Fill(menuRect, panel); Canvas.Border(menuRect, soft);
        menuInput = true;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (EButton(item.Label, new(menuRect.X + 6, menuRect.Y + 6 + i * itemH, width - 12, itemH - 3), enabled: item.Enabled, key: "clip-menu:" + i))
            { CloseClipMenu(); item.Run(); }
        }
        menuInput = false;
    }

    // ---- 批量参数面板 ----

    /// <summary>选择集里所有片段的某个字段是否一致；不一致时显示 (mixed)，提交时原样跳过。</summary>
    static string Mixed => L.Get("(mixed)");
    static string Shared(VsmDocument.Clip[] clips, Func<VsmDocument.Clip, string> read)
    {
        string first = read(clips[0]);
        return clips.All(c => read(c) == first) ? first : Mixed;
    }

    /// <summary>
    /// 批量参数面板。同一个 mod 时开放全部字段；混选了不同 mod 时只开放与 mod 语义无关的那几项 ——
    /// 把一组 fov 和一组 prx 的 To 改成同一个数字没有任何意义，不如不给。
    /// Beat 按位移生效：填进去的是整组最早那一拍的新位置，组内相对间距原样保留。
    /// </summary>
    void DrawBatchClipInspector(Rect r)
    {
        if (editor == null) return;
        var picked = SelectedClipList();
        if (picked.Length == 0) { SetClipSelection(null); return; }
        bool sameMod = picked.All(c => c.Name == picked[0].Name);
        Label(L.Format($"BATCH / {picked.Length} CLIPS") + (sameMod ? " / " + picked[0].Name : L.Get(" / MIXED MODS")), r.X, r.Y);
        if (EButton(L.Get("DUP"), new(r.X, r.Y + 22, 65, 25))) DuplicateClips();
        if (EButton(L.Get("DELETE"), new(r.X + 71, r.Y + 22, 76, 25))) DeleteClips();
        if (EButton(L.Get("ONE"), new(r.X + 153, r.Y + 22, 60, 25), key: "batch-single")) SetClipSelection(picked[0].Id);
        if (EButton("^", new(r.X + r.W - 62, r.Y + 22, 27, 25))) inspectorScroll = Math.Max(0, inspectorScroll - 1);
        if (EButton("v", new(r.X + r.W - 30, r.Y + 22, 27, 25))) inspectorScroll++;
        int fieldCount = 0;
        float shownScroll = motion.To("inspector-scroll", inspectorScroll, .13);
        double first = picked.Min(c => c.Beat);
        void Field(string name, string value, Action<string> action)
        {
            float row = fieldCount++ - shownScroll; float y = r.Y + 57 + row * 31;
            if (row < 0 || y + 27 > r.Y + r.H - 31) return;
            // 值不一致时显示 (mixed)：原样提交等于没改，只有真的敲了新值才会写下去。
            ValueField(name, value, r.X, y, r.W, v => { if (v != Mixed) action(v); });
        }
        if (sameMod) Field(L.Get("Mod"), picked[0].Name, v => EditClips(c => c with { Name = v }, L.Get("Change mod")));
        Field(L.Get("Beat / first"), VsmDocument.N(first), v =>
        {
            double delta = VsmDocument.Number(v) - first;
            EditClips(c => c with { Beat = c.Beat + delta, RepeatEnd = c.RepeatEnd + delta }, L.Get("Shift beats"));
        });
        Field(L.Get("Duration / beat"), Shared(picked, c => VsmDocument.N(c.Duration)), v =>
        { double d = VsmDocument.Number(v); EditClips(c => c with { Duration = d }, L.Get("Set duration")); });
        if (sameMod)
        {
            Field(L.Get("From / _"), Shared(picked, c => c.From), v => EditClips(c => c with { From = v }, L.Get("Set from")));
            Field(L.Get("To / _"), Shared(picked, c => c.To), v => EditClips(c => c with { To = v }, L.Get("Set to")));
        }
        Field(L.Get("Ease"), Shared(picked, c => c.Ease), v => { string e = Easings.Normalize(v); EditClips(c => c with { Ease = e }, L.Get("Set ease")); });
        Field(L.Get("Proxy / -1 global"), Shared(picked, c => c.Proxy.ToString(CultureInfo.InvariantCulture)), v =>
        {
            int p = int.Parse(v, CultureInfo.InvariantCulture);
            if (p >= Current.Chart.Proxies) throw new FormatException(L.Get("Proxy exceeds !proxies."));
            EditClips(c => c with { Proxy = p }, L.Get("Set proxy"));
        });
        Text(sameMod ? L.Get("Beat moves the group; spacing is kept.") : L.Get("Mixed mods: from / to stay per clip."),
            r.X, r.Y + r.H - 22, 10, soft, true, r.W);
        inspectorScroll = Math.Clamp(inspectorScroll, 0, Math.Max(0, fieldCount - Math.Max(1, (int)((r.H - 88) / 31))));
    }

    /// <summary>
    /// 右键菜单期间的输入。菜单是阻塞覆盖层：键盘全吃，点在菜单外只负责关掉菜单，那一次点击不再往下传，
    /// 否则"想关菜单"会顺手把底下的片段选中或者移动。
    /// </summary>
    bool HandleClipMenuInput(Sdl.Event e)
    {
        if (e.Type == 0x300) { if (e.Scan == 41) CloseClipMenu(); return true; }
        if (e.Type is 0x400 or 0x401 or 0x402) { mouseX = e.X; mouseY = e.Y; }
        if (e.Type == 0x401 && !menuRect.Contains(e.X, e.Y)) { CloseClipMenu(); click = false; return true; }
        // 菜单自己的按钮要靠 0x401 / 0x402 生成 click，这两个事件必须继续往下走。
        return e.Type is not (0x400 or 0x401 or 0x402);
    }
}
