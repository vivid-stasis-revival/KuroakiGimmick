using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    /// <summary>Exercise the real SDL event union and editor input path without a touch device.</summary>
    void SmokeTouchUi()
    {
        OpenEditor();
        Draw(1440, 940);
        var viewport = Viewport();
        Sdl.SDL_GetWindowSize(host.Window, out int windowW, out int windowH);
        uint window = Sdl.SDL_GetWindowID(host.Window);
        float firstX = editorTracksRect.X + 100;
        float secondX = firstX + 140;
        float y = editorTracksRect.Y + 50;
        double savedStart = beatStart, savedZoom = pixelsPerBeat;
        int savedScroll = trackScroll;
        bool savedFollow = editorFollow;
        trackScroll = 5;
        beatStart = 12;
        try
        {
            void Finger(uint type, ulong id, float x, float yy)
            {
                Handle(new Sdl.Event
                {
                    Type = type,
                    TouchID = 17,
                    FingerID = id,
                    FingerX = x / viewport.InputScaleX / windowW,
                    FingerY = yy / viewport.InputScaleY / windowH,
                    FingerWindowID = window
                });
            }

            double anchorBeat = BeatAt((firstX + secondX) * .5f);
            Finger(Sdl.FingerDown, 10, firstX, y);
            Finger(Sdl.FingerDown, 20, secondX, y);
            if (touchNavigation != TouchNavigation.Timeline || editDrag != null || held || click)
                throw new Exception("A second finger did not cancel single-finger editing.");

            // SDL may send a second synthetic mouse click for the same touch: ignore it.
            Handle(new Sdl.Event { Type = 0x401, MouseWhich = Sdl.TouchMouseId,
                WindowID = window, Button = 1, X = firstX, Y = y });
            if (click || held) throw new Exception("SDL touch-to-mouse event was not filtered.");

            Finger(Sdl.FingerMotion, 20, secondX + 80, y);
            if (pixelsPerBeat <= savedZoom) throw new Exception("Two-finger pinch did not zoom.");
            double mid = (firstX + secondX + 80) * .5;
            if (Math.Abs(BeatAt((float)mid) - anchorBeat) > .002)
                throw new Exception("Pinch lost the beat underneath its midpoint.");

            Finger(Sdl.FingerMotion, 10, firstX + 40, y + 35);
            Finger(Sdl.FingerMotion, 20, secondX + 120, y + 35);
            if (trackScroll != 4) throw new Exception("Two-finger vertical pan did not move the tracks.");
            mid = (firstX + 40 + secondX + 120) * .5;
            if (Math.Abs(BeatAt((float)mid) - anchorBeat) > .002)
                throw new Exception("Two-finger horizontal pan lost the beat anchor.");

            Finger(Sdl.FingerUp, 10, firstX + 40, y + 35);
            Finger(Sdl.FingerMotion, 20, secondX + 125, y + 35);
            if (click || held) throw new Exception("Lifting one finger restarted editing.");
            Finger(Sdl.FingerUp, 20, secondX + 125, y + 35);
            if (touchContacts.Count != 0 || touchNavigation != TouchNavigation.None)
                throw new Exception("Two-finger gesture left stale input state.");

            // A real mouse is NOT SDL_TOUCH_MOUSEID and must still follow existing paths.
            Handle(new Sdl.Event { Type = 0x400, MouseWhich = 0,
                WindowID = window, X = firstX / viewport.InputScaleX,
                Y = y / viewport.InputScaleY });
            if (Math.Abs(mouseX - firstX) > 1) throw new Exception("Mouse input was blocked by touch handling.");
        }
        finally
        {
            CancelTouchInput();
            beatStart = savedStart;
            pixelsPerBeat = savedZoom;
            trackScroll = savedScroll;
            editorFollow = savedFollow;
            click = held = false;
        }
    }
}
