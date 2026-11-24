namespace KuroakiGimmick.Core.Windows;

/// <summary>
/// 一个原生窗口在 ExtCustomGimmick 归一化 16:9 桌面画布中的内容矩形。X/Y/Width/Height 是 0..1 归一化值，
/// 而 Crop* 是 320×180 逻辑空间的像素区域；两套单位不要混用。
/// </summary>
/// <param name="Id">窗口标识。</param>
/// <param name="Index">窗口索引。</param>
/// <param name="X">桌面横坐标。</param>
/// <param name="Y">桌面纵坐标。</param>
/// <param name="Width">窗口宽度。</param>
/// <param name="Height">窗口高度。</param>
/// <param name="Visible">窗口是否可见。</param>
/// <param name="Source">画面来源编号。</param>
/// <param name="Title">窗口标题。</param>
/// <param name="Border">是否显示边框。</param>
/// <param name="Z">绘制层次。</param>
/// <param name="Proxy">目标 proxy 编号。</param>
/// <param name="CropX">裁剪左边界。</param>
/// <param name="CropY">裁剪上边界。</param>
/// <param name="CropW">裁剪宽度。</param>
/// <param name="CropH">裁剪高度。</param>
/// <param name="FlipX">是否水平翻转。</param>
/// <param name="FlipY">是否垂直翻转。</param>
/// <param name="Alpha">窗口不透明度。</param>
public sealed record WindowPose(int Id, int Index, double X, double Y, double Width, double Height,
    bool Visible, int Source, string Title, bool Border, int Z = 0, int Proxy = -1,
    double CropX = 0, double CropY = 0, double CropW = 320, double CropH = 180, bool FlipX = false, bool FlipY = false, double Alpha = 1);

