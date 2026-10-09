using System.Numerics;
using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    /// <summary>
    /// 用真实的 SDL 输入与生产环境的文字画布做自测，不落盘任何歌曲文件。关闭 UI 动画并固定 UI 缩放为 1，
    /// 使每次 Draw 拿到的都是稳定后的命中矩形；合成坐标基于未缩放的 1440x940 测试画布。失败即抛异常。
    /// </summary>
    public void SmokeTextUi()
    {
        preferences.UiAnimations = false;
        preferences.Workspace = new WorkspaceLayout { FollowDisplayScale = false, UiScale = 1 };
        OpenEditor();
        if (editor == null) throw new InvalidOperationException("Text smoke test needs a chart.");
        string id = editor.AddText("Text canvas", 0, true);
        // 把编辑器当前内容重建成会话并强制重绘：命中矩形要等这一帧画完才更新。
        void Sync()
        {
            Current = new Session(editor.Project.Copy(), editedVsm: editor.Vsm.Text, editedWindows: editor.CompiledWindows(),
                editedVsp: editor.Images.Text, imageResourceRoot: editor.Images.ResourceRoot, editedTexts: editor.TextSources);
            renderedRevision = editor.Revision; textRevision = -1; Draw(1440, 940);
        }
        // 0x401 按下 / 0x400 移动 / 0x402 抬起，坐标已是 UI 逻辑坐标。
        void Mouse(uint type, Vector2 at) => Handle(new() { Type = type, Button = 1, X = at.X, Y = at.Y });
        Sync(); SelectText(id); Draw(1440, 940);
        if (editTracks.Count(t => t.TextId == id) != 1 || editTracks.Any(t => t.Property == "textX_" + id))
            throw new InvalidOperationException("Text channels did not collapse into one object track.");
        if (!textBounds.TryGetValue(id, out var bounds)) throw new InvalidOperationException("Text object has no rendered canvas bounds.");
        var center = (bounds[0] + bounds[2]) / 2;
        // 预览区是 320 宽逻辑空间的缩放显示；屏幕上的位移要乘这个比例，写回的才是期望的局部坐标。
        float scale = previewImage.W / 320;
        Mouse(0x401, center); Draw(1440, 940);
        if (textDrag?.Kind != 0) throw new InvalidOperationException("Text body click did not begin a move gesture.");
        Mouse(0x400, center + new Vector2(25, 10) * scale); Draw(1440, 940);
        Mouse(0x402, center + new Vector2(25, 10) * scale);
        double X() => new TextValueSampler(editor.Vsm, Current.Timeline.Bpm).Get("textX", id, 0);
        if (Math.Abs(X() - 185) > .01) throw new InvalidOperationException("Canvas movement did not write local X coordinates.");
        editor.Undo(); if (Math.Abs(X() - 160) > .01) throw new InvalidOperationException("Text drag did not undo in one action.");
        editor.Redo(); Sync();
        bounds = textBounds[id]; center = (bounds[0] + bounds[2]) / 2;
        // scancode 41 = Esc：拖动中途取消，数值必须停在拖动开始前的 185，而不是回滚到更早的状态。
        Mouse(0x401, center); Draw(1440, 940); Mouse(0x400, center + new Vector2(25, 10));
        Handle(new() { Type = 0x300, Scan = 41 });
        if (textDrag != null || Math.Abs(X() - 185) > .01) throw new InvalidOperationException("Escape did not cancel the text gesture.");
        Draw(1440, 940); bounds = textBounds[id];
        var corner = bounds[2];
        Mouse(0x401, corner); Draw(1440, 940);
        if (textDrag is not { Kind: 1 } sizing) throw new InvalidOperationException("Text corner did not start uniform scaling.");
        var enlarged = sizing.Anchor + (corner - sizing.Anchor) * 1.5f;
        Mouse(0x400, enlarged); Mouse(0x402, enlarged); Sync();
        if (Math.Abs(new TextValueSampler(editor.Vsm, Current.Timeline.Bpm).Get("textscale", id, 0) - 1.5) > .03)
            throw new InvalidOperationException("Text corner did not write uniform scale.");
        bounds = textBounds[id];
        var top = (bounds[0] + bounds[1]) / 2;
        var handle = top + Vector2.Normalize(top - (bounds[2] + bounds[3]) / 2) * 24;
        Mouse(0x401, handle); Draw(1440, 940);
        if (textDrag is not { Kind: 2 } rotating) throw new InvalidOperationException("Text rotation handle did not start rotation.");
        var arm = handle - rotating.Anchor;
        // 把旋转手柄逆时针转 90°，检验写回的 textrot 是 -90：沿用 GameMaker 的角度方向，不做符号翻转。
        var rotated = rotating.Anchor + new Vector2(-arm.Y, arm.X);
        Mouse(0x400, rotated); Mouse(0x402, rotated); Sync();
        if (Math.Abs(new TextValueSampler(editor.Vsm, Current.Timeline.Bpm).Get("textrot", id, 0) + 90) > .1)
            throw new InvalidOperationException("Text rotation did not preserve GameMaker's angle direction.");
        editor.Undo(); Sync();
        // scancode 40 = Enter 提交输入框；首尾空白和换行必须原样保留，不能被 trim。
        OpenValue("Content", "  leading\ntrailing  ", value => editor.SetTextCue(id, 0, value), preserveWhitespace: true);
        Handle(new() { Type = 0x300, Scan = 40 });
        if (modalActive || editor.EditableTexts().Tracks.Single(t => t.Id == id).At(0) != "  leading\ntrailing  ")
            throw new InvalidOperationException("Text input did not preserve multiline content and whitespace.");
        Sync();
        // 换几个窗口尺寸再画回来：缩放后被选中的轨道仍必须留在可见行范围内（每行 35 px）。
        Draw(1180, 860); Draw(1920, 1080); Draw(1440, 940);
        int selectedRow = editTracks.FindIndex(t => t.TextId == id);
        if (selectedRow < trackScroll || selectedRow >= trackScroll + Math.Max(1, (int)(editorTracksRect.H / 35)))
            throw new InvalidOperationException("Resizing hid the selected text object track.");
        // #28: stale inspector position must not override the playhead or an explicit marker.
        activeMarker = null;
        SelectText(id);
        transport.Seek(Current.Timeline.Bpm.Time(32));
        AddTextCueHere();
        if (!editor.EditableTexts().Tracks.Single(t => t.Id == id).Cues.Any(c => c.Beat == 32)
            || textAt != 32 || textAnimation != null)
            throw new InvalidOperationException("Content cue did not use the current insertion beat.");
        editor.Undo();
        if (editor.EditableTexts().Tracks.Single(t => t.Id == id).Cues.Any(c => c.Beat == 32))
            throw new InvalidOperationException("New content cue did not undo in one action.");
        editor.Redo();
        activeMarker = editor.Mark(48);
        transport.Seek(Current.Timeline.Bpm.Time(16));
        AddTextCueHere();
        if (textAt != 48 || !editor.EditableTexts().Tracks.Single(t => t.Id == id).Cues.Any(c => c.Beat == 48))
            throw new InvalidOperationException("Content cue ignored the active insertion marker.");
        activeMarker = null;
        Sync();
        Console.WriteLine("PASS text UI: grouped tracks, real glyph canvas, drag/undo/redo, Escape, scale, rotation, multiline input, resized layouts, playhead/marker cue insertion and cue undo/redo");
        SmokeEditorIssues();
        SmokeProxyFooter();
    }
}
