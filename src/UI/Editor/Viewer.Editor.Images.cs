using System.Runtime.InteropServices;
using System.Numerics;
using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

/// <summary>
/// 图片导入：拖放接收、后台解码、确认弹窗与资源生命周期。
/// 解码一律扔到 Task.Run，输入事件分支里不做文件读取，否则拖一批大图会把窗口线程卡住。
/// </summary>
public sealed partial class Viewer
{
    readonly List<string> droppedImages = [];
    readonly List<ImageImportBatch> retainedImageBatches = [];
    readonly List<Task<Session>> retiredImageReaders = [];
    bool imageDropActive, imageImportInput;
    long lastImageDrop;
    Task<ImageImportBatch>? imageLoad;
    EditorDocument? imageOwner;
    double imageInsertBeat;
    ImageImportBatch? pendingImages;
    string imageImportError = "";
    Vector2? droppedImagePosition, imageInsertPosition;
    string? imageReplacementId;
    float imageImportAlpha;
    /// <summary>导入弹窗是否可见。带上淡出残留（alpha 未归零）一起算，淡出途中仍然吃输入，避免动画期间点到下层。</summary>
    bool ImageImportVisible => pendingImages != null || imageImportAlpha > .001f;

    /// <summary>
    /// 处理拖放事件（0x1000 文件 / 0x1002 开始 / 0x1003 结束 / 0x1004 位置）。
    /// 弹窗可见时只放行退出类事件，其余全部吞掉。忙碌、有对话框或有手势进行中则整批拒收并给出提示，
    /// 不做“先收着待会儿再处理”——那会让文件在不确定的状态下被导入。
    /// </summary>
    bool HandleImageDrop(Sdl.Event e)
    {
        if (e.WindowID != 0 && e.WindowID != Sdl.SDL_GetWindowID(host.Window)) return false;
        if (ImageImportVisible)
        {
            if (e.Type is 0x400 or 0x401 or 0x402)
            {
                mouseX = e.X; mouseY = e.Y;
                if (e.Type == 0x401 && e.Button == 1 && pendingImages != null) { held = true; click = true; }
                if (e.Type == 0x402 && e.Button == 1) held = false;
            }
            if (e.Type == 0x300 && e.Repeat == 0 && e.Scan == 41) CancelImageImport();
            return e.Type is not (0x100 or Sdl.WindowCloseRequested);
        }
        if (!editorMode || e.Type is < 0x1000 or > 0x1004) return false;
        if (e.Type == 0x1002) { droppedImages.Clear(); droppedImagePosition = null; imageDropActive = !Busy && !UiOverlayVisible; lastImageDrop = Environment.TickCount64; return true; }
        if (e.Type == 0x1004) { imageDropActive = !Busy && !UiOverlayVisible; lastImageDrop = Environment.TickCount64; return true; }
        if (e.Type == 0x1000)
        {
            // event.data 的所有权在 SDL3 手里：必须在取下一个事件之前把这段 UTF-8 字符串复制出来。
            string? path = Marshal.PtrToStringUTF8(e.DropData);
            if (path == null) return true;
            if (Busy || UiOverlayVisible || dialogOpen || editDrag != null || ImageGestureActive || resizingLayout != 0)
            { imageDropActive = false; droppedImages.Clear(); message = L.Get("Close the dialog or finish the active operation before dropping images."); return true; }
            if (!ImageImportBatch.Accepts(path))
            {
                if (droppedImages.Count > 0) { droppedImages.Clear(); imageDropActive = false; message = L.Get("Drop images separately from chart/project files."); return true; }
                imageDropActive = false;
                // 其它格式仍走原来的资源/谱面打开流程。
                if (Path.GetExtension(path).ToLowerInvariant() is ".gif" or ".webp" or ".svg" or ".heic")
                { message = L.Get("Use a static PNG, JPEG, BMP or TGA image. Animated/vector formats are not imported."); return true; }
                LoadPaths([path]); return true;
            }
            if (droppedImages.Count == 0)
            {
                transport.SetPlaying(false); droppedImagePosition = null;
                if (previewImage.W > 0 && previewImage.H > 0 && previewImage.Contains(e.DropX, e.DropY))
                    droppedImagePosition = imageCanvas && imageCanvasMap.Scale > 0 ? imageCanvasMap.ToWorld(new(e.DropX, e.DropY)) :
                        new Vector2((e.DropX - previewImage.X) / previewImage.W * 320, (e.DropY - previewImage.Y) / previewImage.H * 180);
            }
            droppedImages.Add(path); lastImageDrop = Environment.TickCount64; return true;
        }
        if (e.Type == 0x1003)
        {
            imageDropActive = false; FlushImageDrops(); return true;
        }
        return true;
    }
    /// <summary>把攒下的一批拖放路径交给导入流程，并清空暂存。落点坐标一并交出去后立即清掉，下一批重新采集。</summary>
    void FlushImageDrops()
    {
        if (droppedImages.Count == 0) return;
        var paths = droppedImages.ToArray(); droppedImages.Clear();
        var position = droppedImagePosition; droppedImagePosition = null; BeginImageImportAt(paths, position);
    }
    void ChooseImageImport()
    {
        if (!EnsureAuthoring()) return;
        Dialog(false, SongFiles.Root(Current.Project), BeginImageImport);
    }
    void BeginImageImport(string[] paths) => BeginImageImportAt(paths, null);
    /// <summary>
    /// 启动一次导入。插入拍和落点位置在这一刻就固定下来，解码完成后播放头早已走远也不影响落位。
    /// imageOwner 记下发起时的文档，完成回调里用引用相等比对，中途换谱面就整批丢弃。
    /// replacement 非空表示替换某个已有 id 的资源，而不是新建 gimmick。
    /// </summary>
    void BeginImageImportAt(string[] paths, Vector2? position, string? replacement = null)
    {
        if (Busy || ImageGestureActive || !EnsureAuthoring() || editor == null) return;
        if (paths.Length == 0) return;
        transport.SetPlaying(false); imageOwner = editor; imageInsertBeat = InsertionBeat;
        imageInsertPosition = position; imageReplacementId = replacement;
        held = click = editorScrub = false; imageImportError = "";
        imageLoad = Task.Run(() => ImageImportBatch.Prepare(paths));
        message = L.Get("Importing image(s)...");
    }
    /// <summary>
    /// 每帧在窗口线程上收割后台解码结果。已经是 obj_custom_gimmick（或本来就是替换资源）就直接提交，
    /// 否则停在弹窗等用户确认切换 !obj。异常在这里一次性兜住并释放 batch，不让半成品留在 pendingImages 里。
    /// </summary>
    void UpdateImageImport()
    {
        // 被丢弃的 session 里可能还有 CPU 端读取线程在跑，要让它暂存的路径保持有效，不能提前删。
        for (int i = retiredImageReaders.Count - 1; i >= 0; i--)
            if (retiredImageReaders[i].IsCompleted)
            { _ = retiredImageReaders[i].Exception; retiredImageReaders.RemoveAt(i); }
        // 有些后端/测试只发单个文件事件、不发 complete 事件，所以静默 150ms 后就自行收口。
        if (droppedImages.Count > 0 && Environment.TickCount64 - lastImageDrop > 150)
        { imageDropActive = false; FlushImageDrops(); }
        if (imageDropActive && Environment.TickCount64 - lastImageDrop > 1500) imageDropActive = false;
        if (imageLoad is not { IsCompleted: true }) return;
        var task = imageLoad; imageLoad = null;
        try
        {
            var batch = task.GetAwaiter().GetResult();
            if (!ReferenceEquals(editor, imageOwner)) { batch.Dispose(); return; }
            pendingImages = batch;
            var chart = new Chart(); VsmReader.ReplaceModsText(chart, editor!.Vsm.Text, "image-import.vsm");
            if (imageReplacementId != null || chart.ObjectName == "obj_custom_gimmick") CommitImages(false);
            else { motion.Snap("image-import", 0); message = L.Get("Confirm the Custom Gimmick object before importing images."); }
        }
        catch (Exception ex) { pendingImages?.Dispose(); pendingImages = null; message = L.Get("Image import failed: ") + ex.Message; }
    }
    /// <summary>
    /// 真正写入文档。替换模式要求恰好一张图，且保留原 id、层、帧数与已有动画。
    /// 提交后 batch 转入 retainedImageBatches 而不是 Dispose：撤销回来还要用到它暂存的文件。
    /// 图片与带时间的属性是一次提交，撤销一步还原两者。
    /// </summary>
    void CommitImages(bool switchObject)
    {
        if (pendingImages == null || editor == null || !ReferenceEquals(editor, imageOwner)) return;
        try
        {
            IReadOnlyList<string> names;
            if (imageReplacementId is { } replacement)
            {
                var item = Current.Images.Items.FirstOrDefault(i => i.Id == replacement) ?? throw new InvalidOperationException(L.Get("Image no longer exists."));
                if (pendingImages.Images.Count != 1) throw new InvalidOperationException(L.Get("Choose exactly one replacement image."));
                editor.ReplaceImageResource(item, pendingImages.Images[0]); names = new[] { replacement };
            }
            else names = editor.ImportImages(pendingImages, imageInsertBeat, switchObject, imageInsertPosition);
            retainedImageBatches.Add(pendingImages); pendingImages = null;
            string name = "imgalp_" + names[0];
            selectedClip = editor.Vsm.Clips.LastOrDefault(c => c.Name == name)?.Id; selectedWindowEvent = -1; inspectorScroll = 0;
            SelectImageObject(names[0]);
            FocusImageTrack(names[0], imageInsertBeat); imageDropActive = false;
            held = click = false;
            message = imageReplacementId != null ? L.Get("Replaced image resource; placement and animations preserved.") : L.Format($"Added {names.Count} image(s). Drag in IMAGE CANVAS; edit INITIAL or add a motion. Chart Folder includes resources.");
        }
        catch (Exception ex) { imageImportError = ex.Message; message = L.Get("Image import failed: ") + ex.Message; }
    }
    /// <summary>选文件替换当前 image 的资源。id 先取出来存进闭包，对话框返回时选中项可能已经变了。</summary>
    void ChooseImageReplacement()
    {
        if (Busy || editor == null || selectedImageId == null) return;
        string id = selectedImageId;
        Dialog(false, SongFiles.Root(Current.Project), paths => BeginImageImportAt(paths, null, id));
    }
    /// <summary>取消导入并释放尚未提交的 batch。同时清掉 held/click，免得关闭弹窗那一下穿透到下面的工作区。</summary>
    void CancelImageImport()
    {
        pendingImages?.Dispose(); pendingImages = null; held = click = false;
    }
    /// <summary>拖放悬停时的提示框，顺带显示会落在哪一拍。仅提示，不参与任何命中判定。</summary>
    void DrawImageDropOverlay()
    {
        if (!editorMode || !imageDropActive || Busy || UiOverlayVisible) return;
        Canvas.Border(previewFrame, Color.Hex(0x85DCFF), 3);
        var r = new Rect(previewFrame.X + 18, previewFrame.Y + 18, Math.Min(470, previewFrame.W - 36), 70);
        Canvas.Fill(r, panel); Canvas.Border(r, soft);
        Text(L.Get("DROP IMAGE TO ADD GIMMICK"), r.X + 14, r.Y + 12, 16, white, max: r.W - 28);
        Text(L.Format($"Insert at beat {InsertionBeat:0.###} / PNG, JPEG, BMP, TGA"), r.X + 14, r.Y + 40, 12, muted, max: r.W - 28);
    }
    /// <summary>
    /// 导入确认弹窗。按钮包在 try/finally 里置 imageImportInput，保证即使 CommitImages 抛出也会复位，
    /// 否则这个标记会一直留着、让弹窗外的控件误以为仍在弹窗里响应输入。
    /// </summary>
    void DrawImageImport(int w, int h)
    {
        if (!ImageImportVisible) return;
        using var fade = Canvas.Opacity(imageImportAlpha);
        Canvas.Fill(new(0, 0, w, h), Color.Hex(0, .88f));
        var r = new Rect(w / 2f - 355, h / 2f - 180, 710, 360);
        Canvas.Fill(r, panel); Canvas.Border(r, soft);
        Text(imageReplacementId != null ? L.Get("REPLACE IMAGE RESOURCE") : L.Get("IMPORT IMAGE GIMMICK"), r.X + 24, r.Y + 25, 22, white);
        Text(L.Format($"{pendingImages?.Images.Count ?? 0} image(s) / beat {imageInsertBeat:0.######}"), r.X + 24, r.Y + 72, 15, white);
        Text(imageReplacementId != null ? L.Get("One source image replaces only the selected instance.") : L.Get("Images require obj_custom_gimmick."), r.X + 24, r.Y + 117, 16, white);
        Text(imageReplacementId != null ? L.Get("ID, layer, frame count and animations are retained.") : L.Get("Switching the current !obj may change existing object-specific effects."), r.X + 24, r.Y + 149, 13, muted, max: r.W - 48);
        Text(L.Get("This imports images and timed properties together; Undo restores both."), r.X + 24, r.Y + 176, 13, muted, max: r.W - 48);
        Text(L.Get("Drop on the canvas to place an image; file selection uses the scene center."), r.X + 24, r.Y + 203, 13, muted, max: r.W - 48);
        if (imageImportError.Length > 0) Text(imageImportError, r.X + 24, r.Y + 236, 12, soft, max: r.W - 48);
        imageImportInput = true;
        try
        {
            if (Button(imageReplacementId != null ? L.Get("REPLACE") : L.Get("USE CUSTOM OBJ + IMPORT"), new(r.X + 24, r.Y + 286, 398, 42), primary: true, enabled: pendingImages != null)) CommitImages(true);
            if (Button(L.Get("CANCEL"), new(r.X + 440, r.Y + 286, 246, 42), enabled: pendingImages != null)) CancelImageImport();
        }
        finally { imageImportInput = false; }
    }
    /// <summary>
    /// 退出前的收尾，顺序要紧：先等所有后台任务（导出准备、导出写入、预览重建、退役读取器）结束，
    /// 再 Dispose 各个 batch。反过来做会在暂存目录已被删除时还有读取线程持着里面的路径。
    /// 每个 await 都单独 try/catch 吞掉异常——此时已在关闭流程上，报错不如保证全部释放。
    /// </summary>
    void ReleaseImageImports()
    {
        // 暂存目录被删除时，不允许还有后台读取线程持有其中的路径。
        try { chartExportPrepare?.GetAwaiter().GetResult(); } catch (Exception) { }
        try { chartExportWrite?.GetAwaiter().GetResult(); } catch (Exception) { }
        try { editorBuild?.GetAwaiter().GetResult(); } catch (Exception) { }
        foreach (var task in retiredImageReaders)
            try { task.GetAwaiter().GetResult(); } catch (Exception) { }
        retiredImageReaders.Clear();
        try { if (imageLoad != null) imageLoad.GetAwaiter().GetResult().Dispose(); } catch (Exception) { }
        pendingImages?.Dispose();
        foreach (var batch in retainedImageBatches) batch.Dispose();
        retainedImageBatches.Clear();
    }
}
