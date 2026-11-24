using KuroakiGimmick.Native;

namespace KuroakiGimmick.Graphics;

public sealed partial class WorkspaceWindowManager
{
    /// <summary>更新窗口标题并设置是否仅呈现预览。</summary>
    /// <param name="window">已登记的工作区窗口。</param>
    /// <param name="title">窗口标题。</param>
    /// <param name="previewOnly">是否仅显示预览。</param>
    public void ConfigurePresentation(Window window, string title, bool previewOnly)
    {
        CheckThread();
        if (window.Main) return;
        if (window.Title != title)
        {
            Sdl.SDL_SetWindowTitle(window.Handle, title);
            window.Title = title;
        }
        bool clean = previewOnly && !window.ShowPreviewControls;
        if (window.CleanPreview == clean) return;
        window.CleanPreview = clean;
        Sdl.SDL_SetWindowBordered(window.Handle, true);
        Sdl.SDL_SetWindowAspectRatio(window.Handle, clean ? 16f / 9 : 0, clean ? 16f / 9 : 0);
        if (clean)
        {
            Sdl.SDL_SetWindowMinimumSize(window.Handle, 320, 180);
            if (Sdl.SDL_GetWindowSize(window.Handle, out int width, out int height))
            {
                int fittedWidth = Math.Max(320, Math.Min(width, (int)Math.Round(height * 16.0 / 9)));
                Sdl.SDL_SetWindowSize(window.Handle, fittedWidth, (int)Math.Round(fittedWidth * 9.0 / 16));
            }
        }
    }
}
