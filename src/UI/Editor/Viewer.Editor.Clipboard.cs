using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

/// <summary>
/// 选择集的复制 / 粘贴。内容以文本形式进出系统剪贴板，因此两个编辑器实例之间可以互相粘贴，
/// 也可以把这段文本发给别人。粘贴一律走 Edit 记进撤销历史，副本必定获得新 Id —— 事件身份不共享。
/// </summary>
public sealed partial class Viewer
{
    /// <summary>
    /// 复制当前选择。批量选择优先于主选中项；图片动画在模型里本身就是 VSM 事件，因此与普通片段同路。
    /// 什么都没选中时不清空剪贴板，避免一次误触把别处复制好的内容冲掉。
    /// </summary>
    void CopySelection() => CopyOrCutSelection(false, Sdl.SDL_SetClipboardText);
    void CutSelection() => CopyOrCutSelection(true, Sdl.SDL_SetClipboardText);

    void CopyOrCutSelection(bool cut, Func<string, bool> writeClipboard)
    {
        if (editor == null || cut && (Busy || editDrag != null || ImageGestureActive)) return;
        var clips = SelectedClipList();
        if (clips.Length == 0 && imageInspector && ActiveImageGroup is { } group) clips = group.Clips;
        if (clips.Length == 0 && selectedClip is Guid id && EditorFindClip(id) is { } single) clips = [single];
        var windows = new List<JsonObject>();
        if (clips.Length == 0 && selectedWindowEvent >= 0 && selectedWindowEvent < (editor.Windows.Events?.Count ?? 0)
            && editor.Windows.Events![selectedWindowEvent] is JsonObject e)
        {
            windows.Add(e);
        }
        string text = EditorClipboard.Write(clips, windows);
        if (text.Length == 0)
        { message = L.Get(cut ? "Select something to cut first." : "Select something to copy first."); return; }
        int count = clips.Length + windows.Count;
        // A successful OS write is not enough: the payload must also be fully pasteable.
        if (cut && EditorClipboard.Read(text)?.Count != count)
        { message = L.Get("Selection cannot be cut because it exceeds clipboard limits or contains unsupported events."); return; }
        if (!writeClipboard(text)) { message = L.Get("Clipboard unavailable: ") + Sdl.Error; return; }
        if (!cut) { message = L.Format($"Copied {count} items to the clipboard."); return; }
        try
        {
            // Delete precisely what was copied, never an unrelated marker or image selection.
            editor.Change(L.Format($"Cut {count} items"), () =>
            {
                foreach (var clip in clips) editor.Vsm.Delete(clip.Id);
                if (windows.Count > 0) editor.Windows.Events!.RemoveAt(selectedWindowEvent);
            });
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or InvalidDataException)
        { message = L.Get("Cut rejected: ") + ex.Message; return; }
        SetClipSelection(null); selectedWindowEvent = -1;
        if (selectedImageGroup is Guid imageGroupId && clips.Any(c => c.Id == imageGroupId))
        { selectedImageGroup = null; imagePoseTarget = ImagePoseTarget.Initial; }
        layoutRevision = -1;
        message = L.Format($"Cut {count} items to the clipboard.");
    }

    /// <summary>
    /// 从剪贴板粘贴到插入点。整批按最早的一拍对齐搬到插入点，组内相对间距原样保留 —— 与批量复制的落点规则一致。
    /// 单条事件校验失败时整次粘贴放弃，不留下半批内容。
    /// </summary>
    void PasteClipboard()
    {
        if (editor == null || Busy) return;
        nint pointer = Sdl.SDL_GetClipboardText();
        string text;
        try { text = Marshal.PtrToStringUTF8(pointer) ?? ""; }
        finally { Sdl.SDL_free(pointer); }
        PasteClipboardText(text);
    }

    void PasteClipboardText(string text)
    {
        if (editor == null || Busy) return;
        if (EditorClipboard.Read(text) is not { } payload)
        {
            message = L.Get("Clipboard has no events to paste.");
            return;
        }
        double delta = InsertionBeat - payload.Beat;
        var copies = payload.Events.Select(c => c with
        {
            Id = Guid.NewGuid(),
            Beat = c.Beat + delta,
            RepeatEnd = c.RepeatEnd + delta
        }).ToArray();
        double time = Current.Timeline.Bpm.Time(InsertionBeat);
        var windows = payload.Windows.Select(w =>
        {
            var copy = (JsonObject)w.DeepClone();
            copy["t"] = time;
            return copy;
        }).ToArray();
        try
        {
            Edit(L.Format($"Paste {copies.Length + windows.Length} items"), () =>
            {
                foreach (var c in copies) editor.Vsm.Add(c);
                foreach (var w in windows) selectedWindowEvent = editor.AddWindow(w);
            });
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or InvalidDataException)
        {
            message = L.Get("Paste rejected: ") + ex.Message;
            return;
        }
        if (copies.Length > 0)
        {
            SetClipSelection(copies[0].Id);
            foreach (var c in copies) selectedClips.Add(c.Id);
        }
        message = L.Format($"Pasted {copies.Length + windows.Length} items.");
    }
}
