// 验证编辑器搜索输入、实时过滤与轨道拖动撤销。

using System.Runtime.InteropServices;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    void SmokeEditorIssues()
    {
        if (editor == null) throw new InvalidOperationException("Editor issue tests need a chart.");
        bool grouped = preferences.GroupGimmickTracks;
        SetGimmickGrouping(false, persist: false); // 旧版行拖动用例使用原始平铺视图。
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
        void Frame()
        {
            renderedRevision = editor.Revision;
            Draw(1440, 940);
        }
        void Key(int scan, ushort modifiers = 0) => Handle(new() { Type = 0x300, Scan = scan, Modifiers = modifiers });
        void Mouse(uint type, float x, float y) => Handle(new() { Type = type, Button = 1, X = x, Y = y });

        // 回归 #42：即使工作流阻止下层输入，真实文本事件仍须到达行内搜索框。
        preferences.ModalValueEditor = false;
        OpenAddGimmick();
        Frame();
        Key(9, 0x0040);
        Frame();
        Check(inlineField == AddSearchKey && inlineVisible && !modalActive, "Ctrl+F did not open inline workflow search");
        nint text = Marshal.StringToCoTaskMemUTF8("scrollspeed");
        try
        {
            Handle(new() { Type = 0x303, TextData = text });
        }
        finally
        {
            Marshal.FreeCoTaskMem(text);
        }
        Frame();
        Check(inlineValue == "scrollspeed" && addShownQuery == "scrollspeed", "Workflow swallowed text input or did not filter live");
        Key(40);
        Frame();
        Check(!InlineActive && addQuery == "scrollspeed", "Inline search did not commit on Enter");
        Mouse(0x401, addSearchRect.X + 20, addSearchRect.Y + 16);
        Frame();
        Check(inlineField == AddSearchKey && !modalActive, "Search button did not respect inline preference");
        Key(41);
        Frame();
        Check(!InlineActive && workflow == "add" && addQuery == "scrollspeed", "Escape from inline search closed the workflow or changed the query");
        preferences.ModalValueEditor = true;
        Key(9, 0x0040);
        Frame();
        Check(modalActive && !InlineActive, "Modal search preference was ignored");
        CloseValue();
        workflow = "";
        preferences.ModalValueEditor = false;
        Frame();

        // 回归 #43：覆盖标签命中区、指针移动和释放三个实际输入阶段。
        trackScroll = 0;
        motion.Snap("timeline-track-scroll", 0);
        Frame();
        var before = editTracks.Select(t => t.Key).ToArray();
        string source = editor.Vsm.Text;
        float labelX = editorTimelineRect.X + 45, rowY = editorTracksRect.Y;
        Mouse(0x401, labelX, rowY + 17);
        Frame();
        Check(draggedTrackKey == before[0], "Timeline label did not begin track drag");
        Mouse(0x400, labelX, rowY + 90);
        Frame();
        Mouse(0x402, labelX, rowY + 90);
        Frame();
        Check(draggedTrackKey == null && editTracks[2].Key == before[0] && editor.Vsm.Text == source, "Track drop changed events or failed to reorder rows");
        editor.Undo();
        layoutRevision = -1;
        Frame();
        Check(editTracks.Select(t => t.Key).SequenceEqual(before), "Track drag did not undo in one step");
        editor.Redo();
        layoutRevision = -1;
        Frame();
        Check(editTracks[2].Key == before[0], "Track drag did not redo");
        Mouse(0x401, labelX, rowY + 17);
        Frame();
        Mouse(0x400, labelX, rowY + 90);
        Key(41);
        Frame();
        Check(draggedTrackKey == null && editTracks[2].Key == before[0], "Escape did not cancel track drag");
        Mouse(0x401, labelX, rowY + 17);
        Frame();
        Check(draggedTrackKey != null, "Focus-loss test could not begin a track drag");
        Handle(new() { Type = 0x20F });
        Frame();
        Check(draggedTrackKey == null && !held, $"Focus loss left the track drag captured: key={draggedTrackKey}, held={held}, resize={resizingLayout}");

        // 移动展开的文本对象时，相关原始动画通道也应一起移动。
        string id = editor.TextSources.Keys.First();
        expandedTextTracks.Add(id);
        layoutRevision = -1;
        Frame();
        string groupKey = "T:" + id;
        int groupIndex = editTracks.FindIndex(t => t.Key == groupKey);
        trackScroll = Math.Max(0, groupIndex - 1);
        motion.Snap("timeline-track-scroll", trackScroll);
        Frame();
        float groupY = editorTracksRect.Y + (groupIndex - trackScroll) * 35 + 17;
        float gripX = editorTracksRect.X - 72;
        Mouse(0x401, gripX, groupY);
        Frame();
        Check(draggedTrackKey == groupKey, "Text group grip was consumed by selection");
        Mouse(0x400, gripX, editorTracksRect.Y + 2);
        Frame();
        Mouse(0x402, gripX, editorTracksRect.Y + 2);
        Frame();
        int first = editTracks.FindIndex(t => TrackRoot(t) == groupKey);
        int last = editTracks.FindLastIndex(t => TrackRoot(t) == groupKey);
        Check(first >= 0 && editTracks[first].Key == groupKey && editTracks.Skip(first).Take(last - first + 1).All(t => TrackRoot(t) == groupKey), "Moving a text group split its child channels");
        Console.WriteLine("PASS #42/#43 UI: inline/modal search, real text input, live filtering, track label drag, undo/redo and Escape");
        SetGimmickGrouping(grouped, persist: false);
    }
}
