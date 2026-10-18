using KuroakiGimmick.Native;

namespace KuroakiGimmick.Graphics;

public sealed partial class WorkspaceWindowManager
{
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
