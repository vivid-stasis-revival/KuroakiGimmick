namespace KuroakiGimmick.Graphics;

/// <summary>仅供 UI 覆盖层使用的呈现透明度；场景渲染与导出不进入这些作用域，因此顶点数据与非 UI 路径完全一致。</summary>
public sealed partial class Canvas
{
    // 顶点 alpha 只在显式的 UI 作用域内被乘一次。Begin 会把它重置为 1；
    // 场景渲染与导出从不进入这种作用域，产出的顶点因此保持不变。
    float presentationOpacity = 1;
    /// <summary>与外层作用域相乘嵌套；Dispose 时恢复进入前的值。</summary>
    public IDisposable Opacity(float opacity) => new OpacityScope(this, opacity);
    /// <summary>忽略外层淡入淡出，把呈现透明度重置为绝对的 1，退出作用域后恢复。</summary>
    public IDisposable Unfaded() => new OpacityScope(this, 1, absolute: true);
    /// <summary>非有限的 opacity 一律当作 1；Dispose 幂等，重复调用不会再次回退。</summary>
    sealed class OpacityScope : IDisposable
    {
        readonly Canvas canvas;
        readonly float previous;
        bool disposed;
        public OpacityScope(Canvas canvas, float opacity, bool absolute = false)
        {
            this.canvas = canvas;
            previous = canvas.presentationOpacity;
            canvas.presentationOpacity = (absolute ? 1 : previous) * (float.IsFinite(opacity) ? Math.Clamp(opacity, 0, 1) : 1);
        }
        public void Dispose()
        {
            if (disposed) return;
            canvas.presentationOpacity = previous;
            disposed = true;
        }
    }
}
