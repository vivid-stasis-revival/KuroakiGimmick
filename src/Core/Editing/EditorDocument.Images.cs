namespace KuroakiGimmick.Core.Editing;

public sealed partial class EditorDocument
{
    /// <summary>
    /// 把一批图片声明与初始属性作为一次撤销操作加入。任一张导入失败，整批连同已写入的 VSP 文本一起回滚。
    /// beat 单位是拍；position 是 320×180 逻辑空间里的落点，省略时放在画面中心 (160, 90)。
    /// </summary>
    public IReadOnlyList<string> ImportImages(ImageImportBatch batch, double beat, bool switchObject, System.Numerics.Vector2? position = null)
    {
        if (!double.IsFinite(beat) || Math.Abs(beat) > 1e8) throw new FormatException("Invalid image insertion beat.");
        if (position is { } point && (!float.IsFinite(point.X + point.Y) || Math.Abs(point.X) > 1e7 || Math.Abs(point.Y) > 1e7))
            throw new FormatException("Invalid drop position.");
        if (batch.Images.Count == 0) throw new ArgumentException("No images to import.");
        var chart = new Chart(); VsmReader.ReplaceModsText(chart, Vsm.Text, "image-import.vsm");
        // 自定义图片只在 obj_custom_gimmick 下生效。改 obj 会影响整份谱面的表现，必须由调用方先拿到用户确认。
        if (chart.ObjectName != "obj_custom_gimmick" && !switchObject)
            throw new InvalidOperationException("Confirm switching to obj_custom_gimmick before importing images.");
        var names = new List<string>();
        // 事件里提到过的 ID 一律预留，哪怕 VSP 中已经没有对应声明——新导入的图片若占用同名 ID，会被那些遗留事件意外驱动。
        var reserved = Vsm.Clips.Select(c => CustomImages.TryMod(c.Name, out _, out var id) ? id : "")
            .Where(id => id.Length > 0).ToList();
        Change("Import " + batch.Images.Count + " image(s)", () =>
        {
            if (chart.ObjectName != "obj_custom_gimmick") Vsm.SetHeader("obj", "obj_custom_gimmick");
            foreach (var image in batch.Images)
            {
                string id = Images.UniqueId(image.OriginalPath, reserved.Concat(names));
                Images.Add(id, image); names.Add(id);
                // 初始缩放让图片最多占到 192×108（320×180 的六成），大图导进来不会一上来就糊满整个画面；不放大小图。
                double scale = Math.Min(1, Math.Min(192.0 / image.Width, 108.0 / image.Height));
                void Add(string property, double at, double value) => Vsm.Add(new(Guid.NewGuid(), at, 0, "linear",
                    VsmDocument.N(value), VsmDocument.N(value), property + "_" + id, -1));
                // 自定义图片默认就是可见的。必须先在更早一拍写一条 alpha 0，否则图片会从谱面开头一直显示到这里。
                Add("imgalp", Math.Min(0, beat - 1), 0);
                Add("imgx", beat, position?.X ?? 160); Add("imgy", beat, position?.Y ?? 90);
                Add("imgscalex", beat, scale); Add("imgscaley", beat, scale);
                Add("imgrot", beat, 0); Add("imgalp", beat, 1);
            }
        });
        return names;
    }
}
