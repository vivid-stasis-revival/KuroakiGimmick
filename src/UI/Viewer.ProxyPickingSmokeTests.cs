using System.Numerics;
using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    void SmokeProxyPicking()
    {
        void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
        string Mod(string name, double value, int p = 0) => $"0,0,linear,{value},{value},{name},{p}\n";
        string root = Path.Combine(Path.GetTempPath(), "kg-proxy-picking-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string vsmPath = Path.Combine(root, "scene.vsm"), vspPath = Path.Combine(root, "scene.vsp");
            string textPath = Path.Combine(root, "scene_text_label.txt");
            using (var texture = new Target(Canvas.Gpu, 8, 8))
            {
                Canvas.Begin(texture, 8, 8, 8, 8, Color.Hex(0x334455));
                Canvas.SavePng(texture, Path.Combine(root, "background.png"));
            }
            File.WriteAllText(textPath, "0,Pick text\n");
            preferences.UiAnimations = false;
            preferences.Workspace = new WorkspaceLayout { FollowDisplayScale = false, UiScale = 1 };
            ResetDockWorkspaces();
            void Frame() => Draw(1440, 940);
            void Scene(string mods, bool image = false, bool text = false)
            {
                CancelSceneDirectGesture(); ResetImageObjects(); ResetTextObjects();
                File.WriteAllText(vsmPath, "!obj:obj_custom_gimmick\n!proxies:3\n" + mods +
                    (text ? Mod("textX_label", 160, -1) + Mod("textY_label", 120, -1) + Mod("textscale_label", 1, -1) +
                        Mod("textalignh_label", 1, -1) + Mod("textalignv_label", 1, -1) + Mod("textsep_label", -1, -1) : ""));
                File.WriteAllText(vspPath, "#Layer\nfront,1000\n#Image\nfront:\n" +
                    (image ? "static,background,background.png,0,320,180,0\n" : ""));
                var project = new ViewerProject { Gimmick = vsmPath, Images = vspPath, Bpm = 120, Profile = "core",
                    RoomPreset = "none", GameUiEnabled = false, TextFiles = text ? new() { ["label"] = textPath } : new() };
                Current = new Session(project); editor = new EditorDocument(Current);
                renderedRevision = editor.Revision; layoutRevision = -1;
                editorMode = true; workflow = ""; settings = help = false;
                imageCanvas = textCanvas = desktopPreview = false; imageInspector = textInspector = false;
                SetClipSelection(null); newProxy = 2; prGridSelection = null; prGridMode = 1;
                prGridSolo = prGridShowCrop = prGridHudExpanded = false;
                prGridShowIds = false; prGridHudOffsetX = float.NaN;
                transport.SetPlaying(false); transport.SetDuration(10); SetTime(0);
                mouseX = mouseY = 0; Frame();
                Check(proxyPickFrame != null, "PR grid did not publish a picking frame");
            }
            Vector2 Pixel(float x, float y) => new(previewImage.X + x * previewImage.W / 320, previewImage.Y + y * previewImage.H / 180);
            Sdl.Event Down(Vector2 point, byte clicks = 2) => new() { Type = 0x401, Button = 1, Clicks = clicks, X = point.X, Y = point.Y };
            void Up(Vector2 point) => Handle(new() { Type = 0x402, Button = 1, X = point.X, Y = point.Y });

            Scene(Mod("pra", 1) + Mod("pra", 1, 1));
            string before = editor!.Vsm.Text; long revision = editor.Revision;
            var point = Pixel(170, 125);
            Handle(Down(point, 1)); Up(point); Check(newProxy == 2, "Single click unexpectedly selected a proxy");
            Handle(Down(point)); Up(point);
            Check(newProxy == 0 && sceneDirectDrag == null, "Double click did not choose the frontmost overlapping proxy");
            Check(editor.Vsm.Text == before && editor.Revision == revision, "Proxy selection created an edit/undo entry");
            prGridMode = 2; newProxy = 2; Frame(); Handle(Down(point)); Up(point);
            Check(newProxy == 2, "Proxy body remained selectable with grid OFF");
            prGridMode = 0; newProxy = -1; hoveredEditTrack = null; Frame(); Handle(Down(point)); Up(point);
            Check(newProxy == -1, "Inactive AUTO grid unexpectedly selected a proxy");

            Scene(Mod("pra", 0) + Mod("pra", 1, 1));
            point = Pixel(170, 125); Handle(Down(point)); Up(point);
            Check(newProxy == 1, "Hidden front proxy blocked a visible proxy");
            Scene(Mod("pra", 1) + Mod("shcl", .6) + Mod("pra", 1, 1));
            point = Pixel(160, 125); Handle(Down(point)); Up(point);
            Check(newProxy == 1, "Masked-out area blocked the proxy behind it");
            Scene(Mod("pra", 1) + Mod("shcl", .45) + Mod("shfl", .1));
            point = Pixel(160, 125); Handle(Down(point)); Up(point);
            Check(newProxy == 0, "Visible fade band was not selectable");
            Scene(Mod("pra", 1) + Mod("shcl", 1));
            point = Pixel(160, 125); Handle(Down(point)); Up(point);
            Check(newProxy == 2, "Fully covered proxy accepted a body hit");
            Scene(Mod("pra", 1) + Mod("prcl", 320));
            point = Pixel(30, 125); Handle(Down(point)); Up(point);
            Check(newProxy == 2, "A wide source crop made background selectable as a track");
            Scene(Mod("pra", 1) + Mod("przm", 0));
            point = Pixel(160, 82); Handle(Down(point)); Up(point);
            Check(newProxy == 2, "Collapsed geometry accepted a body hit");

            foreach (string pose in new[] { Mod("prrz", 30) + Mod("prsx", .2) + Mod("przm", .8),
                Mod("prtrX", .002) + Mod("prtrY", .003), Mod("prcl", 208) + Mod("prcr", 113) + Mod("przx", -1) })
            {
                Scene(Mod("pra", 1) + pose);
                var g = ProxyGeometry.Sample(Current.Timeline, 0, 0, true);
                Check(g.TryProject(new(170, 125), out var logical), "Picking test point failed to project");
                point = Pixel(logical.X, logical.Y); Handle(Down(point)); Up(point);
                Check(newProxy == 0, "Transformed proxy double-click used an axis-aligned box");
            }

            Scene(Mod("pra", 1) + "0,4,linear,0,80,prx,0\n");
            point = Pixel(125, 125); SetTime(2); Handle(Down(point)); Up(point);
            Check(newProxy == 0 && transport.Position == 2, "Picking did not use the displayed frame or moved playback");
            var oldFrame = proxyPickFrame;
            Current = new Session(Current.Project.Copy()); newProxy = 2;
            Check(!HandleProxySceneGesture(Down(point), false) && newProxy == 2, "Stale frame from another session was picked");
            proxyPickFrame = oldFrame;

            Scene(Mod("pra", 1) + Mod("pra", 1, 1));
            point = Pixel(170, 125);
            foreach (string mode in new[] { "image", "text", "desktop", "settings" })
            {
                imageCanvas = mode == "image"; textCanvas = mode == "text";
                desktopPreview = mode == "desktop"; settings = mode == "settings";
                Check(!HandleProxySceneGesture(Down(point), true), "Proxy picking leaked into " + mode);
            }
            imageCanvas = textCanvas = desktopPreview = settings = false;
            Check(!HandleProxySceneGesture(Down(new(previewImage.X - 3, point.Y)), false), "Letterbox area was picked");
            Check(!HandleProxySceneGesture(Down(new(prGridHudRect.X + 15, prGridHudRect.Y + 15)), false), "Proxy picked through its HUD");
            var foreign = Down(point); foreign.WindowID = uint.MaxValue;
            Check(!HandleProxySceneGesture(foreign, true), "Another window's coordinates picked a proxy");
            SelectProxyFromGuide(1); prGridSolo = true; Frame();
            Check(PickProxyInScene(point, proxyPickFrame!.Value) == 1, "Solo preview picked a different proxy");

            Scene(Mod("pra", 1), image: true);
            point = Pixel(170, 125); before = editor!.Vsm.Text; revision = editor.Revision;
            Check(SceneDirectPick(point)?.Kind == SceneDirectKind.Image, "Image overlap fixture missing");
            Handle(Down(point));
            Check(imageInspector && selectedImageId == "background" && newProxy == 2 && sceneDirectDrag != null,
                "Ordinary double-click stole image selection/drag");
            Up(point);
            Check(HandleProxySceneGesture(Down(point, 1), true) && sceneDirectDrag == null,
                "First Shift press started dragging the selected background");
            Up(point);
            Check(HandleProxySceneGesture(Down(point), true), "Shift double-click was not reserved for proxies"); Up(point);
            Check(newProxy == 0 && !imageInspector && sceneDirectDrag == null, "Shift double-click did not select through the image");
            Check(editor.Vsm.Text == before && editor.Revision == revision, "Image/proxy selection conflict edited the chart");
            newProxy = 2;
            Check(HandleProxySceneGesture(Down(Pixel(20, 125)), true) && newProxy == 2 && !imageInspector,
                "A forced proxy miss unexpectedly selected the background");

            Scene(Mod("pra", 1), text: true);
            var hit = sceneDirectHits.Single(h => h.Kind == SceneDirectKind.Text);
            point = (hit.Corners[0] + hit.Corners[2]) / 2; before = editor!.Vsm.Text;
            Handle(Down(point));
            Check(textInspector && selectedTextId == "label" && newProxy == 2 && sceneDirectDrag != null,
                "Ordinary double-click stole text selection/drag"); Up(point);
            Check(HandleProxySceneGesture(Down(point), true), "Shift did not bypass overlapping text"); Up(point);
            Check(newProxy == 0 && !textInspector && editor.Vsm.Text == before, "Text/proxy selection edited the chart");
            Console.WriteLine("PASS proxy body picking: double-click gating, frontmost order, masks/fades, transformed/reversed geometry, rendered time, stale sessions, solo, image/text priority and Shift override without edits");
        }
        finally
        {
            CancelSceneDirectGesture();
            // Keep no loaded editor referring to a temporary fixture after cleanup.
            editorMode = false; ResetEditorForLoad(); Current = Session.Empty();
            Directory.Delete(root, true);
        }
    }
}
