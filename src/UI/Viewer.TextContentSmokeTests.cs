using System.Numerics;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    void SmokeTextContentIssues(string id)
    {
        if (editor == null) throw new InvalidOperationException("Text content checks need an editor.");
        void Check(bool condition, string why) { if (!condition) throw new InvalidOperationException(why); }
        void Frame() { renderedRevision = editor.Revision; Draw(1440, 940); }
        void Click(Vector2 at)
        {
            Handle(new() { Type = 0x401, Button = 1, X = at.X, Y = at.Y }); Frame();
            Handle(new() { Type = 0x402, Button = 1, X = at.X, Y = at.Y }); Frame();
        }
        string Content() => editor.EditableTexts().Tracks.Single(t => t.Id == id).At(0);
        preferences.ModalValueEditor = false;
        editor.SetTextCue(id, 0, "  first\nsecond  ");
        SelectText(id); inspectorScroll = 0; Frame();
        Click(new(editorInspectorRect.X + editorInspectorRect.W * .7f, editorInspectorRect.Y + 140));
        Check(InlineActive && !modalActive && inlineValue == "  first{n}second  ", "Multiline content did not open inline in source notation.");
        Handle(new() { Type = 0x300, Scan = 40 });
        Check(Content() == "  first\nsecond  ", "Unchanged inline content lost whitespace or line breaks.");
        Frame();
        Click(new(editorInspectorRect.X + editorInspectorRect.W * .7f, editorInspectorRect.Y + 140));
        InlineInsert("  pasted\r\nline{n}three  ");
        Handle(new() { Type = 0x300, Scan = 40 });
        Check(Content() == "  pasted\nline\nthree  ", "Inline content lost pasted or escaped newlines.");
        editor.Undo(); Check(Content() == "  first\nsecond  ", "Inline content edit failed to undo.");
        editor.Redo(); Check(Content() == "  pasted\nline\nthree  ", "Inline content edit failed to redo.");
        editor.SetTextCue(id, 4, "next"); editor.SetTextCue(id, 8, "");
        beatStart = 0; pixelsPerBeat = 30; Frame();
        int row = editTracks.FindIndex(t => t.TextId == id);
        trackScroll = row; motion.Snap("timeline-track-scroll", row); Frame();
        float CueY() => editorTracksRect.Y + (editTracks.FindIndex(t => t.TextId == id) - trackShownScroll) * 35 + 10;
        Click(new(BeatX(5), CueY()));
        Check(selectedTextId == id && textAnimation == null && textAt == 4, "Content interval did not select its starting cue.");
        Check(Math.Abs(transport.Position - Current.Timeline.Bpm.Time(4)) < 1e-6, "Content cue click did not seek.");
        Click(new(BeatX(9), CueY()));
        Check(textAt == 8, "Empty content interval was not selectable.");
        // Scrolling past a switch must still show the cue that began to the left of the viewport.
        beatStart = 5; Frame();
        Click(new(BeatX(6), CueY()));
        Check(textAt == 4, "Timeline lost the active content interval when its start was offscreen.");
        Console.WriteLine("PASS #48 UI: inline newlines, pasted content, whitespace, undo/redo, content switches, empty cues and clipped intervals");
    }
}
