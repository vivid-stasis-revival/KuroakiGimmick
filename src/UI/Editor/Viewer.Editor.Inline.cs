using System.Runtime.InteropServices;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;
namespace KuroakiGimmick.UI;

/// <summary>
/// inspector 字段的就地编辑，取代原先那个铺满屏幕的模态输入框。焦点用字段 key 标识，插入点与选区都是 UTF-16 下标。
/// 绘制、命中测试和横向滚动共用同一套前缀量宽（Fonts.Measure 与 Fonts.Text 走同一条字体回退链），
/// 所以光标画在哪一条缝上、鼠标点下去落在哪个字之间，是同一个坐标系算出来的。
/// 模态并没有删掉：值超过 InlineMax 或者本身就是多行文本时仍然回退到模态，单行字段撑不下那些内容。
/// </summary>
public sealed partial class Viewer
{
    /// <summary>当前持有焦点的字段 key，null 表示没有任何就地编辑在进行。</summary>
    string? inlineField;
    string inlineValue = "", inlineOriginal = "", inlineError = "";
    /// <summary>插入点与选区锚点。相等表示只有光标没有选区；谁大谁小都合法，取端点一律走 InlineLow / InlineHigh。</summary>
    int inlineCaret, inlineAnchor;
    bool inlineTrim = true, inlineSelecting;
    Action<string>? inlineAccept;
    /// <summary>本帧真正画出来的输入框与其中的文本区。字段被滚出 inspector 时 inlineVisible 为 false，此时旧矩形不得再参与命中测试。</summary>
    Rect inlineBoxRect, inlineTextRect;
    bool inlineVisible;
    float inlineScroll;
    /// <summary>最后一次改动的时刻。刚动过的半秒内光标常亮再开始闪，否则连续打字时光标一直在眨，反而看不清插入点在哪。</summary>
    double inlineBlinkFrom;
    /// <summary>本帧 inspector 依次画出的字段，Tab / Shift+Tab 依此在字段之间跳转。每帧重建。</summary>
    readonly List<(string Key, string Label, string Value, Action<string> Accept, bool Preserve)> inlineFields = [];
    const float InlineSize = 13;
    /// <summary>就地编辑的长度上限。再长就交给模态：单行框既显示不下，逐字量宽也会变成每帧上千次。</summary>
    const int InlineMax = 1024;

    bool InlineActive => inlineField != null;
    int InlineLow => Math.Min(inlineCaret, inlineAnchor);
    int InlineHigh => Math.Max(inlineCaret, inlineAnchor);
    string InlineSelectedText => inlineValue[InlineLow..InlineHigh];

    /// <summary>
    /// 聚焦一个字段。打开即全选，沿用原模态的手感：直接打字替换整值，先按方向键则落成普通插入点。
    /// preserveWhitespace 用于首尾空白有意义的内容，此时提交不做 Trim。
    /// </summary>
    void OpenInline(string key, string value, Action<string> accept, bool preserveWhitespace = false)
    {
        if (modalActive || menuOpen) return;
        inlineField = key; inlineValue = inlineOriginal = value; inlineAccept = accept;
        inlineError = ""; inlineTrim = !preserveWhitespace; inlineSelecting = false; inlineScroll = 0;
        inlineAnchor = 0; inlineCaret = value.Length;
        inlineBlinkFrom = uptime.Elapsed.TotalSeconds;
        if (!Sdl.SDL_StartTextInput(host.Window)) inlineError = "Text input: " + Sdl.Error;
        click = false;
    }

    /// <summary>放弃编辑并交还键盘。已经提交过的改动不受影响，只是不再有字段持有焦点。</summary>
    void CloseInline()
    {
        if (!InlineActive) return;
        inlineField = null; inlineAccept = null; inlineSelecting = false; inlineVisible = false; inlineError = "";
        // 模态自己也在用文本输入，它还开着的时候不能顺手把输入法关掉。
        if (!modalActive) Sdl.SDL_StopTextInput(host.Window);
    }

    /// <summary>
    /// 提交。accept 抛异常时把错误留在字段上并保持焦点，用户可以接着改 —— 这一点和模态的契约一致：
    /// 值非法时绝不静默关闭，否则刚敲进去的东西会凭空消失。
    /// </summary>
    void AcceptInline()
    {
        if (inlineAccept is not { } accept) { CloseInline(); return; }
        try { accept(inlineTrim ? inlineValue.Trim() : inlineValue); CloseInline(); }
        catch (Exception e) { inlineError = e.Message; }
    }

    /// <summary>提交当前字段并跳到相邻字段。只在本帧真正画出来的字段之间循环 —— 跳到一个滚出视口的字段没有意义。</summary>
    void InlineTab(bool back)
    {
        var fields = inlineFields.ToArray();
        int at = Array.FindIndex(fields, f => f.Key == inlineField);
        AcceptInline();
        if (InlineActive || at < 0 || fields.Length < 2) return;
        var next = fields[(at + (back ? fields.Length - 1 : 1)) % fields.Length];
        OpenInline(next.Key, next.Value, next.Accept, next.Preserve);
    }

    /// <summary>内容变过之后清掉旧错误，并让光标重新常亮。</summary>
    void InlineTouched() { inlineError = ""; inlineBlinkFrom = uptime.Elapsed.TotalSeconds; }

    /// <summary>移动插入点。extend 为真时保留锚点形成选区，否则锚点跟着走、选区消失。</summary>
    void InlineMove(int index, bool extend)
    {
        inlineCaret = Math.Clamp(index, 0, inlineValue.Length);
        if (!extend) inlineAnchor = inlineCaret;
        inlineBlinkFrom = uptime.Elapsed.TotalSeconds;
    }

    /// <summary>删掉选区，返回是否真的删了。没有选区时什么都不做，调用方据此决定要不要再删一个字符。</summary>
    bool InlineDeleteSelection()
    {
        if (inlineCaret == inlineAnchor) return false;
        int low = InlineLow;
        inlineValue = inlineValue[..low] + inlineValue[InlineHigh..];
        inlineCaret = inlineAnchor = low; InlineTouched();
        return true;
    }

    /// <summary>在插入点写入文本，先吃掉选区。超出上限时截断，且不会把代理对从中间切断。</summary>
    void InlineInsert(string text)
    {
        if (text.Length == 0) return;
        InlineDeleteSelection();
        int room = InlineMax - inlineValue.Length;
        if (room <= 0) { inlineError = $"Field limit is {InlineMax} characters."; return; }
        if (text.Length > room)
        {
            text = text[..room];
            if (text.Length > 0 && char.IsHighSurrogate(text[^1])) text = text[..^1];
        }
        // 单行输入框放不下换行，粘贴多行内容时压成空格，免得画出一串豆腐块。
        if (text.Contains('\n') || text.Contains('\r')) text = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        inlineValue = inlineValue[..inlineCaret] + text + inlineValue[inlineCaret..];
        inlineCaret = inlineAnchor = inlineCaret + text.Length;
        InlineTouched();
    }

    /// <summary>下标左移一格，代理对整对跳过，光标不会停在半个码位上。</summary>
    int InlineLeft(int index) => index <= 1 ? Math.Max(0, index - 1)
        : char.IsLowSurrogate(inlineValue[index - 1]) && char.IsHighSurrogate(inlineValue[index - 2]) ? index - 2 : index - 1;
    int InlineRight(int index) => index >= inlineValue.Length - 1 ? Math.Min(inlineValue.Length, index + 1)
        : char.IsHighSurrogate(inlineValue[index]) && char.IsLowSurrogate(inlineValue[index + 1]) ? index + 2 : index + 1;
    int InlineWordLeft(int index)
    {
        while (index > 0 && inlineValue[index - 1] == ' ') index--;
        while (index > 0 && inlineValue[index - 1] != ' ') index--;
        return index;
    }
    int InlineWordRight(int index)
    {
        while (index < inlineValue.Length && inlineValue[index] != ' ') index++;
        while (index < inlineValue.Length && inlineValue[index] == ' ') index++;
        return index;
    }

    /// <summary>前 count 个字符的像素宽度。这就是光标该画在哪里。</summary>
    float InlineWidth(int count) => count <= 0 ? 0 : fonts.Measure(inlineValue[..Math.Min(count, inlineValue.Length)], InlineSize, true);

    /// <summary>UI 像素 X → 字符下标，取最近的字符边界（以半个字宽为界），所以点在一个字的左半边会落到它前面。</summary>
    int InlineIndexAt(float x)
    {
        float local = x - inlineTextRect.X + inlineScroll, width = 0;
        if (local <= 0) return 0;
        for (int i = 0; i < inlineValue.Length;)
        {
            int next = InlineRight(i);
            float advance = fonts.Measure(inlineValue[i..next], InlineSize, true);
            if (local < width + advance * .5f) return i;
            width += advance; i = next;
        }
        return inlineValue.Length;
    }

    /// <summary>读系统剪贴板。SDL 返回的是它自己分配的缓冲，取完字符串必须立刻还给 SDL_free。</summary>
    static string InlineClipboard()
    {
        nint ptr = Sdl.SDL_GetClipboardText();
        try { return Marshal.PtrToStringUTF8(ptr) ?? ""; }
        finally { Sdl.SDL_free(ptr); }
    }

    /// <summary>
    /// 就地编辑期间独占键盘：0x300 一律消费。否则打一个 "a" 会顺手把循环入点设到播放头，退格会删掉选中的片段。
    /// 鼠标只在点进输入框本身时才消费；点在框外先提交再放行，这一次点击照常落到用户真正瞄准的那个控件上。
    /// </summary>
    bool HandleInlineInput(Sdl.Event e)
    {
        if (e.Type == 0x303) { InlineInsert(Marshal.PtrToStringUTF8(e.TextData) ?? ""); return true; }
        if (e.Type == 0x401 && e.Button == 1)
        {
            mouseX = e.X; mouseY = e.Y;
            if (!inlineVisible || !inlineBoxRect.Contains(e.X, e.Y)) { AcceptInline(); return false; }
            // 双击整值全选；单击定位插入点并开始框选，拖到哪里选到哪里。
            if (e.Clicks >= 2) { inlineAnchor = 0; inlineCaret = inlineValue.Length; }
            else { InlineMove(InlineIndexAt(e.X), false); inlineSelecting = true; }
            inlineBlinkFrom = uptime.Elapsed.TotalSeconds; click = false;
            return true;
        }
        if (e.Type == 0x400 && inlineSelecting) { mouseX = e.X; mouseY = e.Y; InlineMove(InlineIndexAt(e.X), true); return true; }
        if (e.Type == 0x402 && e.Button == 1 && inlineSelecting) { mouseX = e.X; mouseY = e.Y; inlineSelecting = false; return true; }
        if (e.Type != 0x300) return false;
        bool command = (e.Modifiers & 0x0CC0) != 0, shift = (e.Modifiers & 3) != 0, alt = (e.Modifiers & 0x0300) != 0;
        bool selected = inlineCaret != inlineAnchor;
        switch (e.Scan)
        {
            case 41: CloseInline(); break;                                              // Esc：放弃这次编辑
            case 40 or 88: AcceptInline(); break;                                       // Enter / 小键盘 Enter：提交
            case 43: InlineTab(shift); break;                                           // Tab：提交并跳到相邻字段
            case 4 when command: inlineAnchor = 0; inlineCaret = inlineValue.Length; break;
            case 6 when command: if (selected) Sdl.SDL_SetClipboardText(InlineSelectedText); break;
            case 27 when command: if (selected) { Sdl.SDL_SetClipboardText(InlineSelectedText); InlineDeleteSelection(); } break;
            case 25 when command: InlineInsert(InlineClipboard()); break;
            // 字段里的 Ctrl/Cmd+Z 撤销的是本次输入，不是整篇文档 —— 焦点在输入框里时按 Z 谁也不会指望文档被回滚。
            case 29 when command: inlineValue = inlineOriginal; inlineAnchor = 0; inlineCaret = inlineValue.Length; InlineTouched(); break;
            case 42:                                                                    // Backspace：往左删
                if (!InlineDeleteSelection() && inlineCaret > 0)
                {
                    int left = InlineLeft(inlineCaret);
                    inlineValue = inlineValue[..left] + inlineValue[inlineCaret..];
                    inlineCaret = inlineAnchor = left; InlineTouched();
                }
                break;
            case 76:                                                                    // Delete：往右删
                if (!InlineDeleteSelection() && inlineCaret < inlineValue.Length)
                {
                    inlineValue = inlineValue[..inlineCaret] + inlineValue[InlineRight(inlineCaret)..];
                    InlineTouched();
                }
                break;
            // 有选区时不带 Shift 的左右键收拢到选区的那一端，而不是从插入点再走一格。
            case 80: InlineMove(!shift && selected ? InlineLow : command ? 0 : alt ? InlineWordLeft(inlineCaret) : InlineLeft(inlineCaret), shift); break;
            case 79: InlineMove(!shift && selected ? InlineHigh : command ? inlineValue.Length : alt ? InlineWordRight(inlineCaret) : InlineRight(inlineCaret), shift); break;
            case 74: InlineMove(0, shift); break;                                       // Home
            case 77: InlineMove(inlineValue.Length, shift); break;                      // End
        }
        return true;
    }

    /// <summary>
    /// 画一行"标签 + 值"。未聚焦时就是一个按钮，点一下当场进入编辑；聚焦后在同一个矩形里直接改，不弹模态。
    /// key 必须能区分不同的被选中对象，否则换了选择之后同名字段会被当成还是原来那一个，焦点和内容会串。
    /// </summary>
    void InlineField(string key, string name, string value, float x, float y, float width, Action<string> accept,
        bool preserveWhitespace = false, bool enabled = true, float labelSize = 10)
    {
        if (enabled) inlineFields.Add((key, name, value, accept, preserveWhitespace));
        Text(name, x, y + 7, labelSize, muted, max: width * .38f);
        var box = new Rect(x + width * .4f, y, width * .6f, 27);
        if (inlineField == key && enabled) { DrawInlineEditor(box); return; }
        // 按实际字宽省略，而不是数字符：先粗切 64 个字符（再宽的字段也放不下更多），再按宽度精修，
        // 否则一个 1024 字符的值会让逐字量宽退化成每帧 O(n²)。
        string shown = value.Replace('\n', ' ');
        if (shown.Length > 64) shown = shown[..(char.IsHighSurrogate(shown[63]) ? 63 : 64)];
        if (!EButton(ImageShortText(shown, box.W - 16, 13), box, enabled: enabled, key: "field:" + key)) return;
        // 多行文本和超长值在单行框里改不了，这两种情况无论设置怎么选都仍然走模态。
        if (preferences.ModalValueEditor || value.Length > InlineMax || value.Contains('\n')) OpenValue(name, value, accept, preserveWhitespace);
        else OpenInline(key, value, accept, preserveWhitespace);
    }

    /// <summary>画聚焦状态：选区高亮、文本、插入点光标。文本按 inlineScroll 横向平移并裁剪，长值不会溢出字段。</summary>
    void DrawInlineEditor(Rect box)
    {
        inlineVisible = true; inlineBoxRect = box;
        var text = new Rect(box.X + 9, box.Y + 6, Math.Max(8, box.W - 18), 16);
        inlineTextRect = text;
        float caret = InlineWidth(inlineCaret), total = InlineWidth(inlineValue.Length);
        // 先保证光标在可视范围内，再夹回内容实际长度：文本比框短时一律靠左，不会出现右边留着白还能继续滚。
        inlineScroll = Math.Clamp(inlineScroll, Math.Max(0, caret - text.W + 2), Math.Max(0, caret));
        inlineScroll = Math.Clamp(inlineScroll, 0, Math.Max(0, total - text.W + 2));
        Canvas.Fill(box, Color.Hex(0x07060A));
        Canvas.Border(box, inlineError.Length > 0 ? red : Color.Hex(0xC24A78));
        Canvas.Clip(text);
        // 文本原点会被 Fonts.Text 吸附到物理像素，这里先吸附好再算光标，两者才落在同一条缝上。
        float origin = Canvas.SnapX(text.X - inlineScroll);
        if (inlineCaret != inlineAnchor)
        {
            float from = InlineWidth(InlineLow), to = InlineWidth(InlineHigh);
            Canvas.Fill(new(origin + from, box.Y + 4, Math.Max(1, to - from), 19), soft.Alpha(.45));
        }
        Text(inlineValue, origin, text.Y, InlineSize, white, true, inlineScroll + text.W);
        double since = uptime.Elapsed.TotalSeconds - inlineBlinkFrom;
        if (since < .5 || since % 1.06 < .64) Canvas.Fill(new(Canvas.SnapX(origin + caret), box.Y + 4, 1.5f, 19), white);
        Canvas.Clip(null);
        // 校验失败的提示压在下一行上方。焦点没走，值也还在框里，改完接着按 Enter 就行。
        if (inlineError.Length == 0) return;
        var tip = new Rect(box.X, box.Y + 28, box.W, 18);
        Canvas.Fill(tip, Color.Hex(0x2A0E16)); Canvas.Border(tip, red);
        Text(inlineError, tip.X + 5, tip.Y + 4, 10, red, max: tip.W - 10);
    }
}
