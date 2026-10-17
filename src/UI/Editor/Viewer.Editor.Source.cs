using System.Runtime.InteropServices;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    SourceTextBuffer? sourceVsm, sourceVsp;
    EditorDocument? sourceOwner;
    long sourceRevision;
    bool sourceImageTab, sourceSelecting;
    int sourceTop;
    float sourceLeft;
    Rect sourceTextRect;
    const float SourceRowHeight = 21, SourceFontSize = 13;
    SourceTextBuffer? SourceBuffer => sourceImageTab ? sourceVsp : sourceVsm;
    static string SourceDisplay(string value) => value.Replace("\t", "    ");
    float SourceWidth(string value) => fonts.Measure(SourceDisplay(value), SourceFontSize, monospaced: true);

    void OpenSourceEditor()
    {
        if (Busy) return;
        if (editor == null && !EnsureAuthoring()) return;
        if (editor == null) return;
        CloseInline(); CloseClipMenu(); transport.SetPlaying(false);
        sourceOwner = editor; sourceRevision = editor.Revision;
        sourceVsm = new(editor.Vsm.Text); sourceVsp = new(editor.Images.Text);
        sourceImageTab = sourceSelecting = false; sourceTop = 0; sourceLeft = 0;
        workflow = displayedWorkflow = "source"; workflowError = "";
        settings = help = false; held = click = false;
        Sdl.SDL_StartTextInput(host.Window);
    }

    void CloseSourceEditor()
    {
        workflow = ""; sourceSelecting = false; held = click = false;
        Sdl.SDL_CaptureMouse(false); Sdl.SDL_StopTextInput(host.Window);
    }

    void ApplySourceEditor()
    {
        if (sourceOwner == null || sourceVsm == null || sourceVsp == null) return;
        if (editor != sourceOwner || editor.Revision != sourceRevision)
        { workflowError = L.Get("Document changed. Reopen the source editor before applying."); return; }
        try
        {
            editor.ApplySource(sourceVsm.Text, sourceVsp.Text);
            layoutRevision = -1; SetClipSelection(null); selectedWindowEvent = -1;
            imageInspector = textInspector = false;
            CloseSourceEditor();
            message = L.Get("Source applied. Undo restores the previous VSM and VSP.");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException or FormatException)
        { workflowError = ex.Message; }
    }

    int SourceHit(float x, float y)
    {
        var buffer = SourceBuffer!;
        int row = Math.Clamp(sourceTop + (int)Math.Floor((y - sourceTextRect.Y) / SourceRowHeight), 0, buffer.LineStarts.Length - 1);
        int start = buffer.LineStarts[row], length = buffer.LineEnd(row) - start;
        float wanted = x - sourceTextRect.X + sourceLeft;
        int lo = 0, hi = length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (SourceWidth(buffer.Text.Substring(start, mid + 1)) < wanted) lo = mid + 1; else hi = mid;
        }
        if (lo < length && wanted > (SourceWidth(buffer.Text.Substring(start, lo)) + SourceWidth(buffer.Text.Substring(start, lo + 1))) / 2) lo++;
        return start + lo;
    }

    void RevealSourceCursor()
    {
        if (SourceBuffer is not { } buffer) return;
        int row = buffer.LineAt(buffer.Cursor), visible = Math.Max(1, (int)(sourceTextRect.H / SourceRowHeight));
        if (row < sourceTop) sourceTop = row;
        if (row >= sourceTop + visible) sourceTop = row - visible + 1;
        float x = SourceWidth(buffer.Text.Substring(buffer.LineStarts[row], buffer.Cursor - buffer.LineStarts[row]));
        if (x < sourceLeft) sourceLeft = Math.Max(0, x - 20);
        if (x > sourceLeft + sourceTextRect.W - 16) sourceLeft = Math.Max(0, x - sourceTextRect.W + 32);
    }

    bool HandleSourceInput(Sdl.Event e)
    {
        if (SourceBuffer is not { } buffer) return true;
        try
        {
            if (e.Type is 0x400 or 0x401 or 0x402)
            {
                mouseX = e.X; mouseY = e.Y;
                if (e.Type == 0x401 && e.Button == 1)
                {
                    if (sourceTextRect.Contains(mouseX, mouseY))
                    {
                        buffer.Select(SourceHit(mouseX, mouseY), (Sdl.SDL_GetModState() & 3) != 0);
                        sourceSelecting = true; Sdl.SDL_CaptureMouse(true); click = false;
                    }
                    else click = true;
                }
                else if (e.Type == 0x400 && sourceSelecting)
                {
                    if (mouseY < sourceTextRect.Y) sourceTop = Math.Max(0, sourceTop - 1);
                    if (mouseY > sourceTextRect.Y + sourceTextRect.H) sourceTop = Math.Min(buffer.LineStarts.Length - 1, sourceTop + 1);
                    buffer.Select(SourceHit(mouseX, mouseY), true);
                }
                else if (e.Type == 0x402 && e.Button == 1) { sourceSelecting = false; Sdl.SDL_CaptureMouse(false); }
                held = false; return true;
            }
            if (e.Type == 0x20F) { sourceSelecting = false; Sdl.SDL_CaptureMouse(false); return true; }
            if (e.Type == 0x403)
            {
                if ((Sdl.SDL_GetModState() & 3) != 0) sourceLeft = Math.Max(0, sourceLeft - e.WheelY * 45);
                else sourceTop = Math.Clamp(sourceTop - (int)Math.Round(e.WheelY * 3), 0, buffer.LineStarts.Length - 1);
                return true;
            }
            if (e.Type == 0x303) { buffer.Insert(Marshal.PtrToStringUTF8(e.TextData) ?? ""); RevealSourceCursor(); return true; }
            if (e.Type != 0x300) return true;
            bool command = (e.Modifiers & 0x0CC0) != 0, shift = (e.Modifiers & 3) != 0;
            if (e.Scan == 41) { CloseSourceEditor(); return true; }
            if (command)
            {
                switch (e.Scan)
                {
                    case 40: ApplySourceEditor(); return true;
                    case 4: buffer.SelectAll(); break;
                    case 6: if (buffer.SelectionLength > 0) Sdl.SDL_SetClipboardText(buffer.Selection); break;
                    case 27: if (buffer.SelectionLength > 0) { Sdl.SDL_SetClipboardText(buffer.Selection); buffer.Insert(""); } break;
                    case 25:
                        nint ptr = Sdl.SDL_GetClipboardText();
                        try { buffer.Insert(Marshal.PtrToStringUTF8(ptr) ?? ""); } finally { Sdl.SDL_free(ptr); }
                        break;
                    case 29: if (shift) buffer.Redo(); else buffer.Undo(); break;
                    case 28: buffer.Redo(); break;
                    case 74: buffer.Select(0, shift); break;
                    case 77: buffer.Select(buffer.Text.Length, shift); break;
                }
            }
            else
            {
                int row = buffer.LineAt(buffer.Cursor);
                switch (e.Scan)
                {
                    case 40: case 88: buffer.Insert(buffer.NewLine); break;
                    case 43: buffer.Insert("    "); break;
                    case 42: buffer.Delete(true); break;
                    case 76: buffer.Delete(false); break;
                    case 80: buffer.Move(-1, shift); break;
                    case 79: buffer.Move(1, shift); break;
                    case 82: buffer.MoveLine(-1, shift); break;
                    case 81: buffer.MoveLine(1, shift); break;
                    case 75: buffer.MoveLine(-Math.Max(1, (int)(sourceTextRect.H / SourceRowHeight)), shift); break;
                    case 78: buffer.MoveLine(Math.Max(1, (int)(sourceTextRect.H / SourceRowHeight)), shift); break;
                    case 74: buffer.Select(buffer.LineStarts[row], shift); break;
                    case 77: buffer.Select(buffer.LineEnd(row), shift); break;
                }
            }
            RevealSourceCursor();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException) { workflowError = ex.Message; }
        return true;
    }

    void DrawSourceEditor(int width, int height)
    {
        var box = new Rect(24, 24, width - 48, height - 48);
        Canvas.Fill(box, panel); Canvas.Border(box, soft);
        Text(L.Get("VSM / VSP SOURCE"), box.X + 20, box.Y + 16, 20, white, true);
        if (WorkflowButton("VSM" + (sourceVsm?.Dirty == true ? " *" : ""), new(box.X + 20, box.Y + 52, 100, 28), active: !sourceImageTab))
        { sourceImageTab = false; sourceTop = 0; sourceLeft = 0; RevealSourceCursor(); }
        if (WorkflowButton("VSP" + (sourceVsp?.Dirty == true ? " *" : ""), new(box.X + 128, box.Y + 52, 100, 28), active: sourceImageTab))
        { sourceImageTab = true; sourceTop = 0; sourceLeft = 0; RevealSourceCursor(); }
        Text(L.Get("Enter: newline | Ctrl/Cmd+Enter: apply | Ctrl/Cmd+Z: undo draft"), box.X + 244, box.Y + 60, 11, muted, max: box.W - 264);
        var area = new Rect(box.X + 16, box.Y + 91, box.W - 32, box.H - 185);
        Canvas.Fill(area, bg); Canvas.Border(area, line);
        sourceTextRect = new(area.X + 70, area.Y + 8, area.W - 80, area.H - 16);
        if (SourceBuffer is { } buffer)
        {
            int visible = Math.Max(1, (int)(sourceTextRect.H / SourceRowHeight));
            sourceTop = Math.Clamp(sourceTop, 0, buffer.LineStarts.Length - 1);
            Canvas.Clip(area);
            for (int row = sourceTop; row < Math.Min(buffer.LineStarts.Length, sourceTop + visible); row++)
            {
                float y = sourceTextRect.Y + (row - sourceTop) * SourceRowHeight;
                Text((row + 1).ToString(), area.X + 8, y, 12, muted, true, 54);
            }
            Canvas.Clip(sourceTextRect);
            for (int row = sourceTop; row < Math.Min(buffer.LineStarts.Length, sourceTop + visible); row++)
            {
                int start = buffer.LineStarts[row], end = buffer.LineEnd(row);
                string text = buffer.Text[start..end];
                float y = sourceTextRect.Y + (row - sourceTop) * SourceRowHeight, x = sourceTextRect.X - sourceLeft;
                int left = Math.Max(start, buffer.SelectionStart), right = Math.Min(end, buffer.SelectionStart + buffer.SelectionLength);
                if (buffer.SelectionLength > 0 && left <= right && buffer.SelectionStart <= end && buffer.SelectionStart + buffer.SelectionLength > start)
                    Canvas.Fill(new(x + SourceWidth(buffer.Text[start..left]), y, Math.Max(5, SourceWidth(buffer.Text[left..right])), SourceRowHeight), Theme.Accent.Alpha(.4));
                Text(SourceDisplay(text), x, y, SourceFontSize, text.TrimStart().StartsWith("//") ? muted : white, true, sourceTextRect.W + sourceLeft);
                if (buffer.LineAt(buffer.Cursor) == row)
                {
                    float caret = x + SourceWidth(buffer.Text.Substring(start, buffer.Cursor - start));
                    Canvas.Line(caret, y, caret, y + 17, 1, white);
                }
            }
            Canvas.Clip(null);
            Text($"Ln {buffer.LineAt(buffer.Cursor) + 1} / Col {buffer.Cursor - buffer.LineStarts[buffer.LineAt(buffer.Cursor)] + 1}", box.X + 20, box.Y + box.H - 80, 11, muted, true);
        }
        if (workflowError.Length > 0) Text(workflowError, box.X + 20, box.Y + box.H - 62, 11, soft, max: box.W - 40);
        if (WorkflowButton(L.Get("CANCEL"), new(box.X + box.W - 280, box.Y + box.H - 42, 120, 28))) CloseSourceEditor();
        if (WorkflowButton(L.Get("APPLY SOURCE"), new(box.X + box.W - 150, box.Y + box.H - 42, 130, 28), primary: true)) ApplySourceEditor();
    }
}
