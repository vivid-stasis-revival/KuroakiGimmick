using KuroakiGimmick.Native;
using KuroakiGimmick.Core.Editing;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    /// <summary>Exercise the real SDL event union and editor input path without a touch device.</summary>
    public void SmokeTouchUi()
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
            SmokeTouchTimelineCommit();
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
    // Render between DOWN and the second finger: the old code had already inserted
    // a clip at that point, so tests which only queued events missed the regression.
    void SmokeTouchTimelineCommit()
    {
        var savedEditor = editor;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        void Frame() { renderedRevision = editor!.Revision; Draw(); host.Gpu.Submit(); }
        void Finger(uint type, ulong id, float x, float y)
        {
            var v = Viewport();
            Sdl.SDL_GetWindowSize(host.Window, out int w, out int h);
            Handle(new() { Type = type, TouchID = 17, FingerID = id,
                FingerX = x / v.InputScaleX / w, FingerY = y / v.InputScaleY / h,
                FingerWindowID = Sdl.SDL_GetWindowID(host.Window) });
        }
        try
        {
            foreach (string ending in new[] { "primary-up", "secondary-up", "cancel", "focus", "move", "changed", "replaced", "commit" })
            {
                editor = new EditorDocument(Current);
                // Existing redo must survive a cancelled gesture as well.
                editor.Change("redo sentinel", () => editor.Vsm.Add(new(Guid.NewGuid(), 0, 1, "linear", "1", "2", "velocity", -1)));
                editor.Undo();
                layoutRevision = -1; trackScroll = 0; beatStart = 100000; pixelsPerBeat = 54;
                touchLastTapAt = -1; Frame();
                float x = editorTracksRect.X + 100, y = editorTracksRect.Y + 15;
                var owner = editor;
                string source = owner.Vsm.Text, redo = owner.RedoLabel;
                long revision = owner.Revision;
                Finger(Sdl.FingerDown, 140, x, y); Frame();
                Finger(Sdl.FingerUp, 140, x, y); Frame();
                touchLastTapAt = Environment.TickCount64; // deterministic on slow CI GPUs
                Finger(Sdl.FingerDown, 150, x, y); Frame();
                Check(touchTimelineAction != null, "Double tap did not reach the timeline insertion path: " + ending);
                Check(owner.Revision == revision && owner.Vsm.Text == source && owner.RedoLabel == redo,
                    "Touch double tap edited before release: " + ending);
                if (ending is "primary-up" or "secondary-up")
                {
                    Finger(Sdl.FingerDown, 160, x + 150, y);
                    ulong first = ending == "primary-up" ? 150UL : 160UL;
                    ulong last = first == 150 ? 160UL : 150UL;
                    Finger(Sdl.FingerUp, first, x, y);
                    Finger(Sdl.FingerMotion, last, x + 180, y); Frame();
                    Finger(Sdl.FingerUp, last, x + 180, y);
                }
                else
                {
                    if (ending == "cancel") Finger(Sdl.FingerCanceled, 150, x, y);
                    if (ending == "focus") Handle(new() { Type = 0x20F, WindowID = Sdl.SDL_GetWindowID(host.Window) });
                    if (ending == "move") Finger(Sdl.FingerMotion, 150, x + 50, y);
                    if (ending == "changed")
                    {
                        owner.Redo(); revision = owner.Revision; source = owner.Vsm.Text;
                    }
                    if (ending == "replaced") editor = new EditorDocument(Current);
                    Finger(Sdl.FingerUp, 150, x, y);
                }
                if (ending == "commit")
                {
                    Check(owner.Revision == revision + 1 && owner.Vsm.Text != source && owner.CanUndo && !owner.CanRedo,
                        "Single-finger double tap did not commit once on release.");
                    owner.Undo();
                    Check(owner.Vsm.Text == source, "Double-tap insertion did not undo in one step.");
                }
                else
                {
                    Check(owner.Revision == revision && owner.Vsm.Text == source,
                        "Cancelled/stale double tap edited the document: " + ending);
                    if (ending != "changed") Check(!owner.CanUndo && owner.CanRedo && owner.RedoLabel == redo,
                        "Cancelled double tap changed undo/redo history: " + ending);
                    if (ending == "replaced") Check(editor.Revision == 0, "Pending touch edited a replacement document.");
                }
                CancelTouchInput();
            }
            editor = new EditorDocument(Current); layoutRevision = -1; Frame();
            long mouseRevision = editor.Revision;
            var viewport = Viewport();
            float mx = editorTracksRect.X + 100, my = editorTracksRect.Y + 15;
            Handle(new() { Type = 0x401, MouseWhich = 4242, Button = 1, Clicks = 2,
                X = mx / viewport.InputScaleX, Y = my / viewport.InputScaleY }); Frame();
            Check(editor.Revision == mouseRevision + 1, "Physical mouse double-click no longer inserts on down.");
            Handle(new() { Type = 0x402, MouseWhich = 4242, Button = 1,
                X = mx / viewport.InputScaleX, Y = my / viewport.InputScaleY }); Frame();
            Check(editor.Revision == mouseRevision + 1, "Mouse release inserted a duplicate clip.");
            Console.WriteLine("Touch insertion: 8 release/cancel/history cases and mouse regression passed (synthetic SDL events).");
        }
        finally
        {
            CancelTouchInput(); editor = savedEditor; layoutRevision = renderedRevision = -1;
        }
    }

}
