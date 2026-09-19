using System.Numerics;
using KuroakiGimmick.Core.Editing;

namespace KuroakiGimmick.Core;

/// <summary>16.2 图像对象编辑的真实 CPU / 文件系统测试。不构造 Host、窗口或图形设备。</summary>
public static class ImageObjectSelfTest
{
    /// <summary>
    /// 先做纯数学的画布映射检查，再在临时目录里对一份真实歌曲 fixture 做整套编辑/撤销/导出；
    /// 结尾强制校验源 VSM/VSP 一字未改。
    /// </summary>
    public static int Run()
    {
        int count = 0;
        void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException("IMAGE OBJECT TEST FAILED: " + name);
            count++; Console.WriteLine("PASS " + name);
        }
        void Reject(Action action, string name)
        {
            bool failed = false;
            try { action(); }
            catch (Exception e) when (e is InvalidOperationException or FormatException or IOException or ArgumentException) { failed = true; }
            Check(failed, name);
        }
        bool Near(double a, double b, double tolerance = 1e-5) => Math.Abs(a - b) <= tolerance;
        // 这个 pose 故意同时带负缩放（镜像）和 37° 旋转：命中测试必须走矩阵逆变换，
        // 用轴对齐包围盒近似的话，镜像加旋转的图像就会点不中或点到旁边。
        var pose = new ImageEditPose(170, 80, -1.25, .6, 37, .8);
        var matrix = pose.Matrix(64, 32);
        var inside = Vector2.Transform(new(.2f, -.3f), matrix);
        Check(ImageCanvasMap.Hit(matrix, inside), "rotated and mirrored image inverse hit test");
        Check(!ImageCanvasMap.Hit(matrix, new(-500, 800)), "outside transformed image is not selected");
        // 鼠标输入和绘制必须共用同一套投影：遍历极端缩放与面板宽度，世界→视图→世界的往返误差要小于 .003 像素。
        // 输入和绘制各算一套变换（哪怕只差一点）会表现成"拖动时图像跟不上光标"。
        foreach (float zoom in new[] { .05f, .5f, 1, 4, 8 })
        foreach (int width in new[] { 600, 1200, 2400 })
        {
            var map = ImageCanvasMap.Create(23, 89, width, width * 9 / 16f, new(160, 90), zoom);
            var p = new Vector2(-37, 220); var q = map.ToWorld(map.ToView(p));
            Check(Vector2.Distance(p, q) < .003f, "one projection for input and draw at zoom " + zoom + " / width " + width);
        }
        // 拖角等比缩放：把角点拖到"放大一倍"该在的位置，反解出来的缩放要正好是两倍，
        // 且旋转与镜像不受影响、轴心仍是 VSP 规定的图像中心（不是被拖的那个角）。
        var handle = new Vector2(.5f, .5f);
        var enlarged = pose with { ScaleX = pose.ScaleX * 2, ScaleY = pose.ScaleY * 2 };
        var resize = ImageCanvasMap.Resize(pose, 64, 32, Vector2.Transform(handle, enlarged.Matrix(64, 32)), handle, true);
        Check(Near(resize.ScaleX, enlarged.ScaleX) && Near(resize.ScaleY, enlarged.ScaleY), "uniform corner scaling retains rotation and mirroring");
        Check(Near(resize.X, pose.X) && Near(resize.Y, pose.Y), "scale gesture preserves the VSP center pivot");
        // 校验只针对本次编辑触及的通道：改位置时不去动旧数据里越界的 alpha=2，
        // 否则打开老谱面随便拖一下图，就会顺手把作者原本的数值改掉。
        Reject(() => (pose with { Alpha = 2 }).Validate(ImageChannels.Alpha), "opacity input range");
        (pose with { Alpha = 2 }).Validate(ImageChannels.Position);
        Check(true, "position edit does not normalize an untouched legacy opacity");

        string temporary = Path.Combine(Environment.CurrentDirectory, ".image-objects-test-" + Guid.NewGuid().ToString("N"));
        string song = Path.Combine(temporary, "song"), outputs = Path.Combine(temporary, "outputs");
        Directory.CreateDirectory(song); Directory.CreateDirectory(outputs);
        try
        {
            // fixture 在 2000 ms 处切到 240 BPM，用来验证动画时长的换算跨越变速点。
            // VSP/VSM 都用 CRLF 并带注释、未知 mpf 回调：编辑后这些部分必须原样还在。
            // card_front 与 card_front_2 只差后缀，用来防止按前缀匹配误伤另一张图。
            WriteBmp(Path.Combine(song, "card.bmp"), 32, 16);
            File.WriteAllText(Path.Combine(song, "ENCORE.vsc"), "0,3,0,b:120\n2000,3,0,b:240\n5000,0,0\n");
            const string sourceVsp = "// keep VSP formatting\r\n#Layer\r\nfront,1000\r\n#Image\r\nfront:\r\nstatic,card_front,card.bmp,0\r\nstatic,card_front_2,card.bmp,1,32,16,0\r\n";
            File.WriteAllText(Path.Combine(song, "ENCORE.vsp"), sourceVsp);
            string sourceVsm = "!obj:obj_custom_gimmick\r\n!proxies:1\r\nmods\r\n// untouched comment\r\n" +
                string.Join("\r\n", ImageEditPose.Properties(ImageChannels.All).Select(p => "0,0,linear," + VsmDocument.N(CustomImages.Default(p)) + "," + VsmDocument.N(CustomImages.Default(p)) + "," + p + "_card_front,-1")) +
                "\r\n0,0,linear,40,40,imgx_card_front_2,-1\r\nmpf\r\n0,20,unknown_preserved_callback\r\n";
            File.WriteAllText(Path.Combine(song, "ENCORE.vsm"), sourceVsm);
            var session = Session.Load(Path.Combine(song, "ENCORE.vsc"));
            var doc = new EditorDocument(session); var map = session.Timeline.Bpm;
            var item = session.Images.Items.Single(i => i.Id == "card_front");
            Check(session.Images.Items.Count == 2, "existing VSP instances loaded without an import journal");
            string unchanged = doc.Vsm.Text;
            // 一个完整 pose 由 6 条属性事件组成，界面上要聚合成一个可编辑对象；
            // 而 card_front_2 只有单独的 X（没有配对的 Y），必须标成不可直接编辑，不能擅自补一条 Y 凑成 XY 动画。
            // 浏览与分组是只读操作：看一眼不能把文档弄脏。
            var groups = ImageObjectModel.Groups(doc.Vsm, item.Id);
            Check(groups.Length == 1 && groups[0].Clips.Length == 6 && groups[0].Editable, "six initial events shown as one object pose");
            Check(ImageObjectModel.Groups(doc.Vsm, "card_front_2")[0].Editable == false, "unpaired legacy X is not silently promoted to an XY animation");
            Check(doc.Vsm.Text == unchanged && !doc.Dirty, "object browsing and grouping are read-only");
            Check(groups[0].Clips.All(c => !c.Name.EndsWith("_front_2", StringComparison.Ordinal)), "underscored instance IDs do not prefix-match another image");
            // 改初始位置要就地改已有的那两条事件（总数仍是 7），不能再追加一组重复的初始化；
            // 一次撤销必须把 X、Y 两条一起退回，且文本逐字节还原。
            var initial = new ImageValueSampler(doc.Vsm, map).Pose(item.Id, 0);
            doc.SetImagePose(item.Id, 0, initial with { X = 60, Y = 40 }, ImageChannels.Position, map);
            Check(doc.Vsm.Clips.Count() == 7, "pose edit updates existing source IDs without duplicate initialization");
            Check(Near(new ImageValueSampler(doc.Vsm, map).Get("imgx_card_front", 0), 60), "initial placement writes actual VSM");
            doc.Undo(); Check(doc.Vsm.Text == sourceVsm && !doc.Dirty, "one undo restores both position fields byte-for-byte");
            doc.Redo(); Check(Near(new ImageValueSampler(doc.Vsm, map).Get("imgy_card_front", 0), 40), "redo restores grouped pose");
            // 动画起点和初始摆放落在同一拍时，两者仍是各自独立可编辑的：
            // 改初始摆放要同时更新那条 duration=0 的事件和紧接其后动画的起始值，不能把它们合并成一条。
            var initialNow = new ImageValueSampler(doc.Vsm, map).Pose(item.Id, 0);
            Guid initialMotion = doc.AddImageAnimation(item.Id, 0, 1, initialNow, initialNow with { X = 90, Y = 50 }, ImageChannels.Position, "linear", map);
            doc.SetInitialImagePose(item.Id, initialNow with { X = 70, Y = 45 }, ImageChannels.Position, map);
            var starting = ImageObjectModel.Groups(doc.Vsm, item.Id).Single(g => g.Id == initialMotion);
            Check(starting.Clips.Any(c => c.Name == "imgx_card_front" && c.From == "70") &&
                doc.Vsm.Clips.Any(c => c.Name == "imgx_card_front" && c.Duration == 0 && c.To == "70"), "initial placement remains editable when a connected motion starts at the same beat");
            doc.Undo(); doc.Undo();
            var startPose = new ImageValueSampler(doc.Vsm, map).Pose(item.Id, 2);
            Guid first = doc.AddImageAnimation(item.Id, 2, 6, startPose, startPose with { X = 220, Y = 110 }, ImageChannels.Position, "linear", map);
            var move = ImageObjectModel.Groups(doc.Vsm, item.Id).Single(g => g.Id == first);
            Check(Near(move.Duration, 3) && Near(move.End(map), 6), "animation end is converted with start BPM across tempo change");
            Check(move.Clips.Length == 2 && move.Label == "MOVE", "XY written together and rediscovered as one movement");
            Guid second = doc.AddImageAnimation(item.Id, 6, 10, startPose with { X = 220, Y = 110 }, startPose with { X = 80, Y = 60 }, ImageChannels.Position, "outCubic", map);
            doc.SetImageEndpoint(move, true, startPose with { X = 250, Y = 95 }, ImageChannels.Position, map);
            var next = ImageObjectModel.Groups(doc.Vsm, item.Id).Single(g => g.Id == second);
            Check(next.Clips.Single(c => c.Name.StartsWith("imgx_", StringComparison.Ordinal)).From == "250" &&
                next.Clips.Single(c => c.Name.StartsWith("imgy_", StringComparison.Ordinal)).From == "95", "a joined path endpoint updates both coordinates of its unique neighbor");
            doc.Undo(); Check(ImageObjectModel.Groups(doc.Vsm, item.Id).Single(g => g.Id == second).Clips.All(c => c.From is "220" or "110"), "one undo restores the path joint on both segments");
            doc.Redo();
            string before = doc.Vsm.Text;
            Reject(() => doc.AddImageAnimation(item.Id, 3, 7, startPose, startPose, ImageChannels.Position, "linear", map), "overlapping generated movement rejected");
            Check(doc.Vsm.Text == before, "rejected edit rolls back all property writes");
            Reject(() => doc.SetImagePose(item.Id, 3, startPose, ImageChannels.Position, map), "no implicit keyframe inserted over an existing tween");
            Check(doc.Vsm.Text == before, "failed pose edit preserves document");
            var old = new ImageValueSampler(doc.Vsm, map).Pose(item.Id, 5);
            doc.OffsetImagePath(item.Id, 20, -10, map);
            var shifted = new ImageValueSampler(doc.Vsm, map).Pose(item.Id, 5);
            Check(Near(shifted.X - old.X, 20) && Near(shifted.Y - old.Y, -10), "offset whole route shifts numeric endpoints without changing timing");
            doc.Undo(); Check(doc.Vsm.Text == before, "whole-path offset is one undo action");
            var currentMove = ImageObjectModel.Groups(doc.Vsm, item.Id).Single(g => g.Id == first);
            Reject(() => doc.SetImageEndpoint(currentMove, true, startPose, ImageChannels.Scale, map), "a MOVE endpoint cannot silently accept an unrelated scale write");
            Guid copied = doc.DuplicateImageGroup(currentMove, 14, map);
            var copiedGroup = ImageObjectModel.Groups(doc.Vsm, item.Id).Single(g => g.Id == copied);
            Check(copiedGroup.Clips.Length == 2 && copiedGroup.Clips.All(c => c.Beat == 14), "duplicate preserves paired X/Y ownership");
            doc.TimeImageGroup(copiedGroup, 16, 20, "outCubic", map);
            copiedGroup = ImageObjectModel.Groups(doc.Vsm, item.Id).Single(g => g.Id == copied);
            Check(Near(copiedGroup.End(map), 20) && copiedGroup.Clips.All(c => c.Ease == "outCubic"), "retime and easing update both position channels");
            doc.DeleteImageGroup(copiedGroup);
            Check(doc.Vsm.Clips.All(c => copiedGroup.Clips.All(oldClip => oldClip.Id != c.Id)), "delete removes every grouped event");
            doc.Undo(); Check(ImageObjectModel.Groups(doc.Vsm, item.Id).Any(g => g.Id == copied), "undo restores deleted motion group");
            doc.Undo(); doc.Undo();
            Check(doc.Vsm.Text == before, "duplicate/retime/delete round trip preserves original document");
            doc.Change("dynamic fixture", () => doc.Vsm.Add(new(Guid.NewGuid(), 12, 2, "linear", "_", "0.3", "imgalp_card_front", -1)));
            Check(ImageObjectModel.Groups(doc.Vsm, item.Id).Any(g => !g.Editable && g.Clips[0].From == "_"), "dynamic source remains raw and keeps its sentinel");
            var rebuilt = new Session(doc.Project.Copy(), null, doc.Vsm.Text, doc.CompiledWindows(), session, doc.Images.Text, doc.Images.ResourceRoot);
            var sampler = new ImageValueSampler(doc.Vsm, map);
            foreach (double b in new[] { 0d, 2, 3.5, 4, 5, 6, 7, 10, 12, 13, 20 })
            foreach (string p in ImageEditPose.Properties(ImageChannels.All))
                Check(Near(sampler.Get(p + "_card_front", b), rebuilt.Timeline.Get(p + "_card_front", map.Time(b))), "authoring/production query agrees: " + p + " @ " + b);
            var advanced = new VsmDocument.Clip(Guid.NewGuid(), 0, 0, "linear", "5", "5", "imgxtime_card_front_2", -1);
            doc.Change("time-linked fixture", () => doc.Vsm.Add(advanced));
            Check(ImageObjectModel.DirectEditIssue(doc.Vsm, item, session.Images.Items).Length > 0, "loop-scoped layer time offsets detected before direct editing");
            doc.Undo();
            string replacement = Path.Combine(temporary, "replacement.bmp"); WriteBmp(replacement, 64, 48);
            using (var batch = ImageImportBatch.Prepare([replacement]))
            {
                string oldVsp = doc.Images.Text; string oldVsm = doc.Vsm.Text;
                doc.ReplaceImageResource(item, batch.Images[0]);
                Check(doc.Vsm.Text == oldVsm && doc.Images.Text.Contains("static,card_front_2,card.bmp,1,32,16,0", StringComparison.Ordinal), "replacing an instance preserves all animations and other references");
                doc.Undo(); Check(doc.Images.Text == oldVsp, "resource replacement undo restores original declaration"); doc.Redo();
                string saved = doc.SaveCopy(Path.Combine(outputs, "saved.sgv.json"));
                var reopen = Session.Load(saved);
                var replacedItem = reopen.Images.Items.Single(i => i.Id == item.Id);
                Check(Near(replacedItem.Width, 32) && Near(replacedItem.Height, 16), "replacement retains logical dimensions rather than new bitmap dimensions");
                Check(replacedItem.Asset.Path.Contains("saved.editor-assets", StringComparison.Ordinal) && File.Exists(replacedItem.Asset.Path), "replacement resource participates in persistent asset saving");
                string folder = Path.Combine(outputs, "exported");
                ChartExport.Write(ChartExport.Prepare(ChartExportInput.Capture(doc, rebuilt), ChartExportKind.ChartFolder, folder));
                var export = Session.Load(Path.Combine(folder, "Kuroaki.sgv.json"));
                Check(export.Images.Items.All(i => File.Exists(i.Asset.Path) && ChartExport.Inside(i.Asset.Path, folder)), "Chart Folder contains the replaced and original image resources");
                Check(export.Chart.Mods.Any(c => c.Name == "imgx_card_front" && c.To == 250), "export uses current edited image endpoints");
                Check(!File.ReadAllText(Path.Combine(folder, "ENCORE.vsm")).Contains("IMAGE CANVAS", StringComparison.Ordinal), "canvas controls do not become game instructions");
            }
            Check(File.ReadAllText(Path.Combine(song, "ENCORE.vsm")) == sourceVsm && File.ReadAllText(Path.Combine(song, "ENCORE.vsp")) == sourceVsp,
                "all operations leave original source files unchanged");
            Console.WriteLine($"PASS {count} image-object CPU/IO assertions.");
            return 0;
        }
        finally { try { Directory.Delete(temporary, true); } catch (IOException) { } }
    }
    static void WriteBmp(string path, int width, int height)
    {
        int stride = (width * 3 + 3) & ~3, size = stride * height;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write((ushort)0x4D42); writer.Write(54 + size); writer.Write(0); writer.Write(54);
        writer.Write(40); writer.Write(width); writer.Write(height); writer.Write((ushort)1); writer.Write((ushort)24);
        writer.Write(0); writer.Write(size); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
        byte[] row = new byte[stride];
        for (int x = 0; x < width; x++) { row[x * 3] = 220; row[x * 3 + 1] = 150; row[x * 3 + 2] = 70; }
        for (int y = 0; y < height; y++) writer.Write(row);
    }
}
