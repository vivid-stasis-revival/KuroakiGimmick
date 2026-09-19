namespace KuroakiGimmick.Core.Windows;

/// <summary>
/// 一个原生窗口在 ExtCustomGimmick 归一化 16:9 桌面画布中的内容矩形。X/Y/Width/Height 是 0..1 归一化值，
/// 而 Crop* 是 320×180 逻辑空间的像素区域；两套单位不要混用。
/// </summary>
public sealed record WindowPose(int Id, int Index, double X, double Y, double Width, double Height,
    bool Visible, int Source, string Title, bool Border, int Z = 0, int Proxy = -1,
    double CropX = 0, double CropY = 0, double CropW = 320, double CropH = 180, bool FlipX = false, bool FlipY = false, double Alpha = 1);

