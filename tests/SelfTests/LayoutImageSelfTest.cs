using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.Core;

/// <summary>
/// 真实 CPU/IO 回归：UI 缩放与工作区几何、图片导入、另存与导出。不创建 SDL 窗口，也不创建图形设备。
/// 贯穿全篇的硬约束是"绝不改用户的原始文件"，最后一条断言就是逐字节回查 .vsp 和 .vsm 没被动过。
/// </summary>
public static class LayoutImageSelfTest
{
    /// <summary>
    /// 前半段是纯计算的视口/布局几何，后半段在临时目录里建一份真实曲目并走完整的导入、保存、导出流程。
    /// 必须用真实文件系统：路径改写、符号链接、外部改动检测这些都无法用内存桩复现。返回累计通过的断言数。
    /// </summary>
    public static int Run()
    {
        int checks = 0;
        void Check(bool value, string label)
        {
            if (!value) throw new InvalidOperationException("LAYOUT/IMAGE TEST FAILED: " + label);
            checks++; Console.WriteLine("PASS " + label);
        }
        // 只认这几类"有意拒绝"的异常；NullReferenceException 之类会直接冒出去判成失败，
        // 免得把崩溃误当成正确的拒绝。
        void Reject(Action action, string label)
        {
            bool rejected = false;
            try { action(); }
            catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or FormatException)
            { rejected = true; }
            Check(rejected, label);
        }
        static bool Near(double a, double b) => Math.Abs(a - b) < .0001;
        var options = new WorkspaceLayout();
        // 两种 4K 情形：Windows 用 200% 显示缩放（像素尺寸已是 3840，密度 1），macOS Retina 则是
        // 逻辑 1920 配密度 2。两条路径必须都得到 1920×1080 的逻辑 UI —— 把密度乘两遍就会缩成一半。
        var windows = UiViewport.Create(3840, 2160, 3840, 2160, 2, 1, options);
        var retina = UiViewport.Create(1920, 1080, 3840, 2160, 2, 2, options);
        Check(windows.Width == 1920 && windows.Height == 1080, "Windows 200 percent display scale yields logical UI size");
        Check(retina.Width == 1920 && retina.Height == 1080, "Retina density is not applied twice");
        // SDL 报上来的指针坐标要用这个逆变换换回逻辑坐标，两种情形的系数分别是 .5 和 1；
        // 系数算错的表现是点击位置和控件对不上，而不是画面异常。
        Check(Near(windows.InputScaleX, .5) && Near(retina.InputScaleX, 1), "SDL input inverse transform matches logical coordinates");
        // 关掉跟随系统缩放后，UI 倍率完全由用户偏好决定，和显示器 DPI 无关：3840/1.5 = 2560。
        options.FollowDisplayScale = false; options.UiScale = 1.5;
        var manual = UiViewport.Create(3840, 2160, 3840, 2160, 2, 1, options);
        Check(manual.Width == 2560 && manual.Height == 1440 && !manual.FitLimited, "manual UI scale independent of monitor DPI");
        // 小窗口上放到 2.5 倍会装不下，此时必须回退（FitLimited）到至少铺满窗口，
        // 否则控件被放到可视区外就再也点不到、也调不回来了。
        options.UiScale = 2.5;
        var small = UiViewport.Create(1180, 860, 1180, 860, 1, 1, options);
        Check(small.Width >= 1180 && small.Height >= 860 && small.FitLimited, "small-window zoom keeps controls in the viewport");
        // 穷举 5 种宽 × 4 种高 × 编辑器/预览两种模式 × 3 个分割比例：预览区不小于 589.99（16:9 下的最小可用宽度），
        // 左右面板留出边距，分割线不贴到底部。这些边界值来自实际布局常量，不是拍脑袋的宽松下限。
        foreach (int width in new[] { 1180, 1440, 1920, 2560, 3840 })
        foreach (int height in new[] { 860, 1080, 1440, 2160 })
        foreach (bool editor in new[] { false, true })
        foreach (double fraction in new[] { .15, .52, .85 })
        {
            var prefs = new WorkspaceLayout { LeftWidth = 420, RightWidth = 480, EditorPreviewFraction = fraction, ViewerPreviewFraction = fraction };
            var layout = WorkspaceGeometry.Create(width, height, editor, prefs);
            Check(layout.PreviewWidth >= 589.99f && layout.PreviewX >= layout.LeftWidth + 40 &&
                layout.RightX + layout.RightWidth <= width - 24 && layout.SplitY < height - 200,
                $"layout bounds {width}x{height} editor={editor} fraction={fraction}");
            // 拖动分割线保存的是比例；从像素反算回比例必须精确还原，否则每次开关窗口分割线都会漂一点。
            Check(Near(WorkspaceGeometry.Fraction(layout.SplitY, height, editor), fraction), "splitter inverse preserves saved fraction");
        }
        // 手改坏的偏好文件（NaN、无穷、负数、过大值）必须收敛到有限的默认值，而不是把 NaN 带进布局计算：
        // 右边这些数字就是各字段的默认/钳位结果。
        options = new WorkspaceLayout { UiScale = double.NaN, LeftWidth = float.PositiveInfinity, RightWidth = -10,
            TrackLabelWidth = 9999, EditorPreviewFraction = double.NaN, ViewerPreviewFraction = -10 };
        options.Normalize();
        Check(Near(options.UiScale, 1) && Near(options.LeftWidth, 194) && Near(options.RightWidth, 282) &&
            Near(options.TrackLabelWidth, 500) && Near(options.EditorPreviewFraction, .52) && Near(options.ViewerPreviewFraction, .15),
            "malformed layout preferences normalize to finite bounds");
        // 故意取奇数尺寸和分数缩放（1.25 × 1.125），逼出取整路径：取整之后输入逆变换的比例
        // 必须仍然把物理尺寸精确映射到取整后的逻辑尺寸，不能用取整前的比例。
        var rounding = UiViewport.Create(3001, 1777, 3001, 1777, 1.25f, 1, new() { UiScale = 1.125 });
        Check(Near(3001 * rounding.InputScaleX, rounding.Width) && Near(1777 * rounding.InputScaleY, rounding.Height),
            "rounded logical extents use exact input ratios");
        // 预览框无论外框是什么形状，都要保持曲目的 16:9 —— 拉伸会让作者看到与游戏不同的构图。
        var fit = Canvas.PreviewRect(new Rect(0, 0, 800, 400), 1, 1, false);
        Check(Near(fit.W / fit.H, 16.0 / 9), "preview frame resize preserves 16:9 song aspect");

        // 不要用 macOS 的 /tmp 别名：导出的资源会主动拒绝带符号链接的父目录，所以临时目录建在当前目录下。
        string temporary = Path.Combine(Environment.CurrentDirectory, ".kuroaki-layout-image-test-" + Guid.NewGuid().ToString("N"));
        string song = Path.Combine(temporary, "song"), exports = Path.Combine(temporary, "exports");
        Directory.CreateDirectory(song); Directory.CreateDirectory(exports);
        ImageImportBatch? batch = null;
        try
        {
            var preferences = new ViewerSettings { Workspace = new() { UiScale = 1.5, LeftWidth = 260, TrackLabelWidth = 310 } };
            string settings = Path.Combine(temporary, "settings.json");
            preferences.Save(new ViewerProject(), settings);
            var loaded = ViewerSettings.Load(settings);
            Check(Near(loaded.Workspace.UiScale, 1.5) && Near(loaded.Workspace.LeftWidth, 260) && Near(loaded.Workspace.TrackLabelWidth, 310),
                "layout and scale survive preference save/reload");
            // 工作区尺寸属于本机设置，不能随谱面工程走：一旦序列化进 project，别人打开这份谱面
            // 就会被改掉自己的 UI 缩放。这里按名字扫一遍序列化结果来卡住这条边界。
            Check(!JsonSerializer.Serialize(new ViewerProject(), ViewerProject.Json).Contains("UiScale", StringComparison.OrdinalIgnoreCase),
                "device UI preferences do not leak into a song project");
            string chartPath = Path.Combine(song, "ENCORE.vsc"), vsm = Path.Combine(song, "ENCORE.vsm"), vsp = Path.Combine(song, "ENCORE.vsp");
            // 最小可用谱面：开头 b:120 把 BPM 定成 120（0.5 秒一拍），后面一个单点音符加一个长按。
            File.WriteAllText(chartPath, "0,3,0,b:120\n1000,0,0\n5000,2,1,5500\n");
            // 这两份原始文本故意用 CRLF 并各带一行注释：文件末尾会逐字节比对，
            // 只要编辑器顺手把换行统一成 LF 或者把注释吃掉，比对就会失败。
            const string sourceVsm = "!obj:obj_custom_gimmick\r\n!proxies:1\r\nmods\r\n0,0,linear,1,1,velocity,-1\r\n// leave this text alone\r\n";
            File.WriteAllText(vsm, sourceVsm);
            string existingImage = Path.Combine(song, "existing.bmp");
            WriteBmp(existingImage, 16, 8);
            const string sourceVsp = "// original header\r\n#Layer\r\nbase,0\r\n#Image\r\nbase:\r\nstatic,existing,existing.bmp,0,16,8,0\r\n";
            File.WriteAllText(vsp, sourceVsp);
            // 两个文件名只差一个逗号：VSP 是 CSV，逗号必须被替换掉，替换后就会和第二个文件撞名。
            // 所以这一对同时压住"CSV 安全"和"撞名时仍要分配出不同 ID"两件事。
            string first = Path.Combine(temporary, "image one, sample.bmp"), second = Path.Combine(temporary, "image one_ sample.bmp");
            // 640x360 正好是 16:9，横竖两个方向的适配倍率相同，才能验证缩放是等比而不是各轴独立拉伸。
            WriteBmp(first, 640, 360); WriteBmp(second, 320, 240);
            byte[] sourcePixels = File.ReadAllBytes(first);
            batch = ImageImportBatch.Prepare(new[] { first, second });
            Check(batch.Images.Count == 2 && batch.Images[0].Width == 640 && batch.Images[0].Height == 360, "dropped images actually decode with their measured dimensions");
            // 拖进来的图会复制到私有暂存目录，用户桌面上的原文件一个字节都不能动。
            Check(batch.Images.All(i => !i.StagedPath.Contains(',')) && File.ReadAllBytes(first).SequenceEqual(sourcePixels),
                "private CSV-safe staging leaves external original bytes unchanged");
            var project = new ViewerProject { Chart = chartPath, Gimmick = vsm, Profile = "core", GameUiEnabled = false };
            var session = new Session(project);
            var doc = new EditorDocument(session);
            // 只是打开谱面不算修改：文本要和磁盘上的原样一致，Dirty 也必须还是 false，
            // 否则用户什么都没做就会被提示保存，进而覆盖掉原始 VSP。
            Check(doc.Images.Text == sourceVsp && !doc.Dirty, "existing VSP is loaded without rewriting the source");
            var names = doc.ImportImages(batch, 8, false);
            // ID 要能直接用作游戏里的标识符：首字符不能是数字，只能是字母数字下划线；
            // 两个来源文件名归一化后会撞在一起，所以还得互不相同。
            Check(names.Count == 2 && names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2 &&
                names.All(n => Regex.IsMatch(n, "^[A-Za-z_][A-Za-z0-9_]*$")), "import allocates unique game-safe instance identifiers");
            // 15 = 原 VSM 里已有的 1 条 velocity 片段 + 2 个实例 × 每个 7 条（提前置 0 的 imgalp、
            // imgx、imgy、imgscalex、imgscaley、imgrot、显示用的 imgalp）。
            Check(doc.Images.Ids().Count == 3 && doc.Vsm.Clips.Count() == 15 && doc.Dirty,
                "image batch adds two VSP instances and seven timed properties per instance");
            // 导入只改内存中的文档；磁盘上的两份原文件在显式保存之前必须原封不动。
            Check(File.ReadAllText(vsp) == sourceVsp && File.ReadAllText(vsm) == sourceVsm, "import does not write either original source");
            var frozen = ChartExportInput.Capture(doc, session);
            // 导出取的是当前未保存的文本快照，不是磁盘内容；取错了就会导出成导入之前的旧状态。
            Check(frozen.ImageText == doc.Images.Text && frozen.ImageRoot == song, "export captures unsaved VSP text, not stale disk data");
            Session Preview(EditorDocument d) => new(d.Project.Copy(), null, d.Vsm.Text, d.CompiledWindows(), session, d.Images.Text, d.Images.ResourceRoot);
            var preview = Preview(doc);
            // 预览走的是正式的运行时读取路径，不是编辑器自己的简化视图：暂存目录里的资源必须能被它真正打开。
            Check(preview.Images.Items.Count == 3 && preview.Images.Items.All(i => File.Exists(i.Asset.Path)), "production reader sees unsaved imported resources");
            string id = names[0];
            // 自定义图片默认可见，所以导入时会先补一条 alpha=0 的事件。两条事件都是零时长常量片段，
            // 合起来是个阶跃：插入拍之前必须是全透明，到插入拍当帧才变成不透明。
            double before = preview.Timeline.Bpm.Time(7.999), at = preview.Timeline.Bpm.Time(8);
            Check(Near(preview.Timeline.Get("imgalp_" + id, before), 0) && Near(preview.Timeline.Get("imgalp_" + id, at), 1),
                "image opacity switches at the exact insertion beat");
            // 160/90 是 320x180 曲目坐标系的正中心，和 UI 缩放无关；.3 来自 min(1, 192/640, 108/360)，
            // 即等比缩进 192x108 的安全框内，且绝不放大小图。横竖两轴必须是同一个倍率。
            Check(Near(preview.Timeline.Get("imgx_" + id, at), 160) && Near(preview.Timeline.Get("imgy_" + id, at), 90) &&
                Near(preview.Timeline.Get("imgscalex_" + id, at), .3) && Near(preview.Timeline.Get("imgscaley_" + id, at), .3),
                "image is centered and aspect-fit in song coordinates, independent of UI scale");
            // 一次导入同时动了 VSP 声明和 VSM 事件，撤销必须把两边一起退回去——分两步撤销会留下
            // 指向不存在图片的事件。
            doc.Undo();
            Check(doc.Images.Text == sourceVsp && doc.Vsm.Text == sourceVsm && !doc.Dirty, "one undo restores VSP and VSM together");
            // 重做要复用原来那批 ID，不能重新分配，否则 VSM 里的事件名就对不上了。
            doc.Redo(); Check(doc.Images.Ids().SetEquals(names.Concat(new[] { "existing" })), "redo keeps image identities");
            string folder = Path.Combine(exports, "chart");
            var plan = ChartExport.Prepare(frozen, ChartExportKind.ChartFolder, folder);
            // 恰好 2 个 resources/ 条目：源目录内的 existing.bmp 按原相对路径随目录一起复制，
            // 只有暂存目录（在源目录之外）里的那两张导入图才会被改名搬进 resources/。
            Check(plan.Files.Any(f => f.Name == "ENCORE.vsp") && plan.Files.Count(f => f.Name.StartsWith("resources/", StringComparison.Ordinal)) == 2,
                "Chart Folder plans both unsaved images and generated VSP");
            ChartExport.Write(plan);
            var exported = Session.Load(Path.Combine(folder, "Kuroaki.sgv.json"));
            // 导出结果必须自包含：三张图都要落在新文件夹内部，不能还有路径指回源目录。
            Check(exported.Images.Items.Count == 3 && exported.Images.Items.All(i => File.Exists(i.Asset.Path) && ChartExport.Inside(i.Asset.Path, folder)),
                "exported project resolves original and imported images inside new folder");
            // 会话暂存目录在退出时会被删掉，所以重写后的 VSP 里绝不能残留指向它的路径。
            Check(!File.ReadAllText(Path.Combine(folder, "ENCORE.vsp")).Contains(batch.DirectoryPath), "exported VSP does not point back into session cache");
            var single = ChartExport.Prepare(frozen, ChartExportKind.Vsm, Path.Combine(exports, "single.vsm"));
            // VSM-only 就该只出一个文件，但必须明确警告图片和 VSP 没被带上——静默少带资源比直接失败更难排查。
            Check(single.Files.Count == 1 && single.Warnings.Any(n => n.Contains("NOT included", StringComparison.Ordinal)),
                "VSM-only export remains VSM-only and clearly identifies omitted assets");

            string saved = doc.SaveCopy(Path.Combine(exports, "saved.sgv.json"));
            var reopened = Session.Load(saved);
            // 另存会顺带写出配套的 .editor.vsp，并把图片路径改成相对 VSP 的形式，这样整份工程可以整体搬走。
            Check(!doc.Dirty && reopened.Project.ImagePathsRelativeToVsp && reopened.Images.Items.Count == 3 &&
                File.Exists(Path.Combine(exports, "saved.editor.vsp")), "Save creates a persistent companion VSP and portable resource paths");
            // 导入的图要从临时暂存迁到工程自己的 .editor-assets 目录；留在暂存里的话关掉程序就没了。
            Check(reopened.Images.Items.Where(i => i.Id != "existing").All(i => i.Asset.Path.Contains("saved.editor-assets")),
                "saved imported images reside in the project companion asset directory");
            doc.SaveCopy(saved); Check(File.Exists(saved), "repeated save accepts its own unchanged companion assets");
            // 把配套资源在外部改掉再存：保存必须察觉并拒绝，不能拿一份来路不明的字节覆盖工程。
            string stable = reopened.Images.Items.First(i => i.Id == id).Asset.Path;
            byte[] good = File.ReadAllBytes(stable); File.AppendAllText(stable, "external edit");
            Reject(() => doc.SaveCopy(saved), "save detects outside changes to generated image companions");
            File.WriteAllBytes(stable, good);

            // 第二份曲目没有 VSP，而且用的是 obj_base_gimmick：导入图片要求切换到 obj_custom_gimmick，
            // 这是会影响整首曲目表现的改动，必须由用户点头（第三个参数）才能做。
            string noVspSong = Path.Combine(temporary, "empty-images"); Directory.CreateDirectory(noVspSong);
            string blankChart = Path.Combine(noVspSong, "FINALE.vsc"), blankVsm = Path.Combine(noVspSong, "FINALE.vsm");
            File.Copy(chartPath, blankChart); File.WriteAllText(blankVsm, "!obj:obj_base_gimmick\n!proxies:1\n");
            var blankSession = new Session(new ViewerProject { Chart = blankChart, Gimmick = blankVsm, Profile = "core", GameUiEnabled = false });
            var blankDoc = new EditorDocument(blankSession);
            Reject(() => blankDoc.ImportImages(batch, 3, false), "base object is not changed without confirmation");
            // 被拒绝的操作不能留下半成品：文档要还是干净的，也不能已经建出 VSP。
            Check(!blankDoc.Dirty && !blankDoc.Images.HasContent, "cancelled object change leaves document clean");
            blankDoc.ImportImages(batch, 3, true);
            Check(blankDoc.Images.HasContent && blankDoc.Vsm.Text.Contains("!obj:obj_custom_gimmick"), "explicit confirmation adds custom object and VSP together");
            var newInput = ChartExportInput.Capture(blankDoc, blankSession);
            // 这份 VSP 从头到尾只存在于内存里，磁盘上从来没有过；导出必须能凭文本把它写出来。
            string newly = Path.Combine(exports, "new-vsp"); ChartExport.Write(ChartExport.Prepare(newInput, ChartExportKind.ChartFolder, newly));
            Check(Session.Load(Path.Combine(newly, "Kuroaki.sgv.json")).Images.Items.Count == 2, "Chart Folder exports an authored VSP that never existed on disk");
            blankDoc.Undo();
            Check(!blankDoc.Images.HasContent && blankDoc.Vsm.Text.Contains("obj_base_gimmick"), "undo removes new VSP and restores original object");
            // 渲染器那边仍持有一个指向虚拟 VSP 的会话。撤销之后导出快照必须两个字段都为 null，
            // 否则会导出一份空的 VSP，或者去读一个根本不存在的路径。
            var stalePreview = new Session(blankSession.Project.Copy(), null, blankDoc.Vsm.Text, blankDoc.CompiledWindows(), blankSession, "", noVspSong);
            var emptyInput = ChartExportInput.Capture(blankDoc, stalePreview);
            Check(emptyInput.ImageText == null && emptyInput.Images == null, "undo does not export the renderer's virtual VSP path");
            ChartExport.Prepare(emptyInput, ChartExportKind.ChartFolder, Path.Combine(exports, "empty-export"));
            // 下面几条都是"宁可拒绝也不要猜"：遇到不认识的 VSP 段落就不能擅自重排用户的文件。
            var invalid = new VspDocument("#Layer\nlayer,0\n#Image\n#Unknown\n", song, "invalid.vsp");
            Reject(() => invalid.Add("new_image", batch.Images[0]), "unsupported VSP section structure is not silently rewritten");
            Reject(() => blankDoc.ImportImages(batch, double.NaN, true), "non-finite insertion beat rejected");
            string corrupt = Path.Combine(temporary, "corrupt.png"); File.WriteAllText(corrupt, "not an image");
            // 解码失败要在动文档之前就挡掉，不能先插好事件再失败留下半截状态。
            Reject(() => ImageImportBatch.Prepare(new[] { corrupt }), "malformed image rejected before editing");
            // 33 比上限 32 多一个，而且这些路径全都不存在：数量检查必须排在访问文件之前。
            Reject(() => ImageImportBatch.Prepare(Enumerable.Range(0, 33).Select(i => Path.Combine(temporary, i + ".png"))), "batch count limit is enforced before file access");
            // 白名单只收静态格式（png/jpg/jpeg/bmp/tga）且不分大小写；gif 是动图，没有对应的播放语义。
            Check(!ImageImportBatch.Accepts("animated.gif") && ImageImportBatch.Accepts("PHOTO.PNG"), "format filter is explicit and case-insensitive");
            // 删掉暂存目录和用户的原始文件，模拟"导入完就把素材挪走/清缓存"：已保存和已导出的工程
            // 都必须自带资源副本，此时仍能完整打开。
            batch.Dispose(); batch = null;
            File.Delete(first); File.Delete(second);
            Check(Session.Load(saved).Images.Items.Count == 3 && Session.Load(Path.Combine(folder, "Kuroaki.sgv.json")).Images.Items.Count == 3,
                "saved and exported imports reopen after original drops and staging are removed");
            // 全流程跑完后的收尾断言：用户最初的两份源文件仍然和写进去时逐字节相同。
            Check(File.ReadAllText(vsp) == sourceVsp && File.ReadAllText(vsm) == sourceVsm, "original song files remain unchanged after all operations");
            Console.WriteLine($"PASS {checks} layout/image CPU and IO checks.");
            return 0;
        }
        finally
        {
            batch?.Dispose();
            // 临时目录删不掉不算测试失败（Windows 上可能还被别的进程占着）；这里只做尽力清理。
            try { Directory.Delete(temporary, true); } catch (IOException) { }
        }
    }
    /// <summary>
    /// 现写一张 24 位 BMP 当素材，避免让测试依赖仓库里的二进制文件。
    /// 行数据必须按 4 字节对齐（stride）且自下而上存放，否则解码器读出的尺寸/内容会不对。
    /// </summary>
    static void WriteBmp(string path, int width, int height)
    {
        int stride = (width * 3 + 3) & ~3, size = stride * height;
        using var w = new BinaryWriter(File.Create(path));
        w.Write((ushort)0x4D42); w.Write(54 + size); w.Write(0); w.Write(54);
        w.Write(40); w.Write(width); w.Write(height); w.Write((ushort)1); w.Write((ushort)24);
        w.Write(0); w.Write(size); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        byte[] row = new byte[stride];
        for (int x = 0; x < width; x++) { row[x * 3] = 96; row[x * 3 + 1] = 164; row[x * 3 + 2] = 240; }
        for (int y = 0; y < height; y++) w.Write(row);
    }
}
