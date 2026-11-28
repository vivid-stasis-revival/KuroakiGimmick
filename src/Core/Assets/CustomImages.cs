// 解析 VSP 图片条目，将命名属性轨道转换为帧号、姿态和颜色。

using System.Globalization;
using System.Numerics;
using StbImageSharp;

namespace KuroakiGimmick.Core;

/// <summary>
/// VSP 图层/图像定义与姿态求值。图片资源和 mod 轨道分开加载；层优先级保留作者值，不强制所有图片进入同一轨道。
/// </summary>
// Custom Gimmicks v1.14.1：.vsp 图像声明 + 具名 .vsm 轨道。
public sealed class CustomImages
{
    /// <summary>图片文件及解码前取得的源尺寸。</summary>
    /// <param name="Path">图片文件路径。</param>
    /// <param name="Width">图像像素宽度。</param>
    /// <param name="Height">图像像素高度。</param>
    public record Asset(string Path, int Width, int Height);
    /// <summary>VSP 中关联图片资源的图层条目。</summary>
    /// <param name="Id">VSM 属性轨道引用的图片 ID。</param>
    /// <param name="Layer">所属 VSP 图层名。</param>
    /// <param name="LayerPriority">所在图层的优先级。</param>
    /// <param name="Priority">条目在层内的优先级。</param>
    /// <param name="Order">源声明顺序。</param>
    /// <param name="Asset">关联的图片资源。</param>
    /// <param name="Frames">源图包含的帧数。</param>
    /// <param name="Width">单帧绘制宽度。</param>
    /// <param name="Height">单帧绘制高度。</param>
    /// <param name="Lane">条目所属的轨道。</param>
    public record Item(string Id, string Layer, double LayerPriority, double Priority, int Order, Asset Asset, int Frames, double Width,
        double Height, int Lane);
    /// <summary>当前图片配置的来源路径。</summary>
    public string? Path { get; private set; }
    /// <summary>供按图层顺序绘制的图片条目。</summary>
    public List<Item> Items { get; } = [];
    /// <summary>以键索引的图片资源与尺寸。</summary>
    public Dictionary<string, Asset> Files { get; } = new(StringComparer.Ordinal);
    /// <summary>按公共图片轨道属性表拆分属性和 ID。</summary>
    public static bool TryMod(string name, out string kind, out string id) => KenomaGMK.Core.VspImages.TryMod(name, out kind, out id);
    /// <summary>返回原版图片轨道的初值或未设置时间项的哨兵。</summary>
    public static double Default(string kind) => KenomaGMK.Core.VspImages.Default(kind);
    /// <summary>从工程路径或编辑快照读取 VSP 图层、资源与声明。</summary>
    /// <param name="p">当前工程的路径与呈现配置。</param>
    /// <param name="chart">提供音符、演出事件及诊断的谱面。</param>
    /// <param name="editedText">编辑器的 VSP 文本覆盖。</param>
    /// <param name="resourceRoot">图片引用的可选解析根目录。</param>
    /// <returns>按层整理的图片条目与资源。</returns>
    public static CustomImages Load(ViewerProject p, Chart chart, string? editedText = null, string? resourceRoot = null)
    {
        var result = new CustomImages();
        var parsed = new KenomaGMK.Core.Chart();
        parsed.ImageNames.UnionWith(chart.ImageNames);
        var declarations = SongFiles.Resources(p).LoadImages(parsed, static stream =>
            ImageInfo.FromStream(stream) is { } info ? (info.Width, info.Height) : null,
            ChartLoadLimits.Unlimited, editedText, resourceRoot);
        if (declarations == null) return result;
        result.Path = declarations.Path;
        foreach (var (key, asset) in declarations.Files)
            result.Files.Add(key, new(asset.Path, asset.Width, asset.Height));
        foreach (var item in declarations.Items)
            result.Items.Add(new(item.Id, item.Layer, item.LayerPriority, item.Priority, item.Order,
                result.Files[item.Asset.Path], item.Frames, item.Width, item.Height, item.Lane));
        chart.ImageNames.UnionWith(parsed.ImageNames);
        foreach (var d in parsed.Diagnostics) chart.Diagnostics.Add(new(d.Source, d.Line, d.Message, d.Error));
        return result;
    }

    /// <summary>按歌曲时刻求得的图片渲染参数。</summary>
    /// <param name="Matrix">将图片映射到画布的变换矩阵。</param>
    /// <param name="Frame">当前帧号。</param>
    /// <param name="Rgb">RGB 调色值。</param>
    /// <param name="Alpha">不透明度。</param>
    public record Pose(Matrix3x2 Matrix, int Frame, uint Rgb, double Alpha);
    /// <summary>
    /// 按已排序的层顺序产出每张图的当前姿态（320×180 逻辑空间）。alpha ≤ 0 或矩阵出现非有限分量的图直接跳过，不绘制。
    /// 变换次序固定为缩放 → 斜切 → 旋转 → 平移，与原版绘制顺序一致，不能为了合并矩阵改写。
    /// </summary>
    /// <param name="timeline">用于采样轨道和回调的时间轴。</param>
    /// <param name="time">采样时刻，单位为秒。</param>
    /// <param name="valueOverrides">替代轨道采样值的可选映射。</param>
    /// <returns>按绘制顺序排列的可见图片及其姿态。</returns>
    public IEnumerable<(Item Image, Pose Pose)> At(Timeline timeline, double time, IReadOnlyDictionary<string, double>? valueOverrides = null)
    {
        var tracks = new ImageTracks(timeline,time,valueOverrides);
        foreach (var item in Items)
        {
            if (KenomaGMK.Graphics.Scene.CustomImagePose.TryEvaluate(item.Id,item.Width,item.Height,item.Frames,item.Lane,tracks,out var pose))
                yield return (item,new(pose.Matrix,pose.Frame,pose.Rgb,pose.Alpha));
        }
    }

    readonly record struct ImageTracks(Timeline Timeline,double Time,IReadOnlyDictionary<string,double>? Overrides)
        : KenomaGMK.Graphics.Scene.ICustomImageTracks
    {
        public double Get(string name) => Overrides != null && Overrides.TryGetValue(name,out double value)
            ? value : Timeline.Get(name,Time);
        public double NoteX(int lane,double distance) => NoteMotion.X(Timeline,Time,lane,distance);
        public double NoteY(int lane,double distance) => NoteMotion.Y(Timeline,Time,lane,distance);
    }
}
