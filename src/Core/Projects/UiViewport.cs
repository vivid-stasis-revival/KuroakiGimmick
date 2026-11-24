namespace KuroakiGimmick.Core;

/// <summary>绘制、命中测试与拖放共用的同一份投影。Retina 上 DPI 不能被应用两次。</summary>
/// <param name="Width">逻辑界面宽度。</param>
/// <param name="Height">逻辑界面高度。</param>
/// <param name="PixelWidth">目标物理像素宽度。</param>
/// <param name="PixelHeight">目标物理像素高度。</param>
/// <param name="InputScaleX">原生横坐标到逻辑坐标的倍率。</param>
/// <param name="InputScaleY">原生纵坐标到逻辑坐标的倍率。</param>
/// <param name="FitLimited">是否因窗口容量限制了所请求的缩放。</param>
public readonly record struct UiViewport(int Width, int Height, int PixelWidth, int PixelHeight,
    float InputScaleX, float InputScaleY, bool FitLimited)
{
    /// <summary>
    /// Width/Height 是逻辑界面单位，PixelWidth/PixelHeight 是物理像素；InputScale* 把物理指针坐标换算回逻辑坐标。
    /// 逻辑尺寸不低于 1180×860，缩放超出窗口可容纳范围时被 fit 夹住并置 FitLimited，让界面知道"请求值没有完全生效"。
    /// </summary>
    /// <param name="windowWidth">SDL 窗口坐标宽度。</param>
    /// <param name="windowHeight">SDL 窗口坐标高度。</param>
    /// <param name="pixelWidth">目标的物理像素宽度。</param>
    /// <param name="pixelHeight">目标的物理像素高度。</param>
    /// <param name="displayScale">用户显示缩放倍率。</param>
    /// <param name="pixelDensity">窗口坐标到物理像素的比例。</param>
    /// <param name="options">设备本地的工作区布局设置。</param>
    /// <returns>主窗口使用的逻辑视口与输入映射。</returns>
    public static UiViewport Create(int windowWidth, int windowHeight, int pixelWidth, int pixelHeight,
        float displayScale, float pixelDensity, WorkspaceLayout options)
        => CreateForMinimum(windowWidth, windowHeight, pixelWidth, pixelHeight, displayScale, pixelDensity,
            options, 1180, 860);

    /// <summary>独立停靠窗口使用面板最小尺寸，主工作区的 1180×860 下限仅用于主界面。</summary>
    /// <param name="windowWidth">SDL 窗口坐标宽度。</param>
    /// <param name="windowHeight">SDL 窗口坐标高度。</param>
    /// <param name="pixelWidth">目标的物理像素宽度。</param>
    /// <param name="pixelHeight">目标的物理像素高度。</param>
    /// <param name="displayScale">用户显示缩放倍率。</param>
    /// <param name="pixelDensity">窗口坐标到物理像素的比例。</param>
    /// <param name="options">设备本地的工作区布局设置。</param>
    /// <param name="minimumWidth">允许的最小宽度。</param>
    /// <param name="minimumHeight">允许的最小高度。</param>
    /// <returns>使用辅助窗口最小尺寸约束的视口。</returns>
    public static UiViewport CreateAuxiliary(int windowWidth, int windowHeight, int pixelWidth, int pixelHeight,
        float displayScale, float pixelDensity, WorkspaceLayout options, int minimumWidth = 240, int minimumHeight = 160)
        => CreateForMinimum(windowWidth, windowHeight, pixelWidth, pixelHeight, displayScale, pixelDensity,
            options, Math.Clamp(minimumWidth, 160, 2000), Math.Clamp(minimumHeight, 100, 1500));

    static UiViewport CreateForMinimum(int windowWidth, int windowHeight, int pixelWidth, int pixelHeight,
        float displayScale, float pixelDensity, WorkspaceLayout options, int minimumWidth, int minimumHeight)
    {
        options.Normalize();
        windowWidth = Math.Max(1, windowWidth);
        windowHeight = Math.Max(1, windowHeight);
        pixelWidth = Math.Max(1, pixelWidth);
        pixelHeight = Math.Max(1, pixelHeight);
        // 平台没给出有效密度时由像素/逻辑尺寸反推，绝不能留 0 或 NaN 进入后面的除法。
        if (!float.IsFinite(pixelDensity) || pixelDensity <= 0) pixelDensity = pixelWidth / (float)windowWidth;
        if (!float.IsFinite(displayScale) || displayScale <= 0) displayScale = pixelDensity;
        // 跟随系统缩放时只取 displayScale 与 pixelDensity 的比值，避免把已经体现在像素尺寸里的 DPI 再乘一遍。
        double requested = options.UiScale * (options.FollowDisplayScale ? displayScale / pixelDensity : 1);
        double fit = Math.Min(windowWidth / (double)minimumWidth, windowHeight / (double)minimumHeight);
        double applied = Math.Max(.01, Math.Min(requested, fit));
        int width = Math.Max(minimumWidth, (int)Math.Round(windowWidth / applied));
        int height = Math.Max(minimumHeight, (int)Math.Round(windowHeight / applied));
        // 留 1e-5 容差，避免浮点误差让 FitLimited 在缩放恰好等于上限时抖动。
        return new(width, height, pixelWidth, pixelHeight, width / (float)windowWidth,
            height / (float)windowHeight, applied + .00001 < requested);
    }
}

