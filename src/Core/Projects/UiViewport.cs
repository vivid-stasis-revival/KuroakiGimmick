namespace KuroakiGimmick.Core;

/// <summary>绘制、命中测试与拖放共用的同一份投影。Retina 上 DPI 不能被应用两次。</summary>
public readonly record struct UiViewport(int Width, int Height, int PixelWidth, int PixelHeight,
    float InputScaleX, float InputScaleY, bool FitLimited)
{
    /// <summary>
    /// Width/Height 是逻辑界面单位，PixelWidth/PixelHeight 是物理像素；InputScale* 把物理指针坐标换算回逻辑坐标。
    /// 逻辑尺寸不低于 1180×860，缩放超出窗口可容纳范围时被 fit 夹住并置 FitLimited，让界面知道"请求值没有完全生效"。
    /// </summary>
    public static UiViewport Create(int windowWidth, int windowHeight, int pixelWidth, int pixelHeight,
        float displayScale, float pixelDensity, WorkspaceLayout options)
    {
        options.Normalize();
        windowWidth = Math.Max(1, windowWidth); windowHeight = Math.Max(1, windowHeight);
        pixelWidth = Math.Max(1, pixelWidth); pixelHeight = Math.Max(1, pixelHeight);
        // 平台没给出有效密度时由像素/逻辑尺寸反推，绝不能留 0 或 NaN 进入后面的除法。
        if (!float.IsFinite(pixelDensity) || pixelDensity <= 0) pixelDensity = pixelWidth / (float)windowWidth;
        if (!float.IsFinite(displayScale) || displayScale <= 0) displayScale = pixelDensity;
        // 跟随系统缩放时只取 displayScale 与 pixelDensity 的比值，避免把已经体现在像素尺寸里的 DPI 再乘一遍。
        double requested = options.UiScale * (options.FollowDisplayScale ? displayScale / pixelDensity : 1);
        double fit = Math.Min(windowWidth / 1180.0, windowHeight / 860.0);
        double applied = Math.Max(.01, Math.Min(requested, fit));
        int width = Math.Max(1180, (int)Math.Round(windowWidth / applied));
        int height = Math.Max(860, (int)Math.Round(windowHeight / applied));
        // 留 1e-5 容差，避免浮点误差让 FitLimited 在缩放恰好等于上限时抖动。
        return new(width, height, pixelWidth, pixelHeight, width / (float)windowWidth,
            height / (float)windowHeight, applied + .00001 < requested);
    }
}

