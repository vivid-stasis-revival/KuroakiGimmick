using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Core.Windows;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    void SmokeCgmk1142()
    {
        var oldSession = Current; var oldEditor = editor;
        string root = Path.Combine(Path.GetTempPath(), "kg-cgmk-ui-" + Guid.NewGuid().ToString("N"));
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException("CGMK UI: " + label); }
        void Sync()
        {
            Current = new Session(editor!.Project.Copy(), editedVsm: editor.Vsm.Text, editedWindows: editor.CompiledWindows());
            renderedRevision = editor.Revision; layoutRevision = -1; Draw(1440, 940);
        }
        try
        {
            Directory.CreateDirectory(root);
            string vsm = Path.Combine(root, "ENCORE.vsm");
            File.WriteAllText(vsm, "!obj:obj_custom_gimmick\n0,0,linear,0,0,cover1,-1\n");
            File.WriteAllText(Path.Combine(root, "cgmk_config.json"), "{\"JACKET_MANAGE_MODE\":\"normal\"}");
            Current = new(new ViewerProject { Gimmick = vsm, Profile = "core", RoomPreset = "none", GameUiEnabled = false });
            editor = new(Current); SetClipSelection(null); selectedTextId = selectedImageId = null;
            textInspector = imageInspector = false; workflow = "";
            AddMod(0, "changeskin", -1); Sync();
            Check(Current.SkinChangeEnabled && Current.Timeline.Get("changeskin", .5) == 1,
                "quick add must enable and preview skin change");
            Check(selectedClip.HasValue, "quick add selects the skin event");
            editor.Undo(); Sync();
            Check(!Current.SkinChangeEnabled && !editor.Vsm.Clips.Any(c => c.Name == "changeskin"), "quick-add undo restores config and event");
            OpenAddGimmick(GimmickAuthoring.Entry("changeskin"), 0); addTo = "3"; CommitGimmick(); Sync();
            Check(workflow == "" && Current.SkinChangeEnabled && Current.Timeline.Get("changeskin", .5) == 3,
                "add dialog enables skin change");
            SetClipSelection(editor.Vsm.Clips.Single(c => c.Name == "changeskin").Id);
            // Exercise the actual inspector button with SDL mouse input. Seven
            // editable event fields precede the CGMK switch; scroll it into view.
            inspectorScroll = 7; motion.Snap("inspector-scroll", inspectorScroll); Draw(1440, 940);
            motion.Snap("inspector-scroll", inspectorScroll); Draw(1440, 940);
            float x = editorInspectorRect.X + editorInspectorRect.W / 2,
                y = editorInspectorRect.Y + 70 + (7 - inspectorScroll) * 31;
            Handle(new() { Type = 0x401, Button = 1, X = x, Y = y }); Draw(1440, 940);
            Handle(new() { Type = 0x402, Button = 1, X = x, Y = y }); Sync();
            Check(!WindowMotionConfig.Flag(editor.Windows.Root, "ENABLE_SKIN_CHANGE") && !Current.SkinChangeEnabled,
                "inspector click disables skin change");
            editor.Undo(); Sync();
            Check(Current.SkinChangeEnabled, "inspector toggle can be undone");

            using var font = new SceneFont(Canvas.Gpu);
            using var target = new Target(Canvas.Gpu, 320, 180);
            foreach (bool legacy in new[] { true, false })
            foreach (uint rgb in new uint[] { 0xff0000, 0x0000ff })
            {
                Current.Texts.Tracks.Clear();
                Current.Texts.Tracks.Add(new(legacy ? "" : "label", "synthetic", [new(0, "RGB")]));
                double Value(string name) => name is "textX_labelb" or "textY_labelb" ? 0 : name.Split('_')[0] switch
                {
                    "textX" => 160, "textY" => 90, "textcolhex" or "textcolrgb" => rgb,
                    var kind => CustomText.Default(kind, legacy)
                };
                Canvas.Begin(target, 320, 180, 320, 180, Color.Hex(0));
                font.Draw(Canvas, Current, .5, values: Value);
                byte[] pixels = Canvas.Read(target);
                int lit = 0, channel = rgb == 0xff0000 ? 0 : 2, other = 2 - channel;
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    if (pixels[i + channel] > 32) lit++;
                    Check(pixels[i + other] <= 2 && pixels[i + 1] <= 2, "subtitle RGB channels must not be swapped");
                }
                Check(lit > 10, "subtitle fixture rendered visible glyphs (legacy=" + legacy + ", RGB=" + rgb.ToString("X6") + ")");
            }
            Console.WriteLine("PASS CGMK 1.14.2 UI/GPU: quick add, add dialog, inspector toggle/undo and legacy/extended RGB subtitle pixels");
        }
        finally
        {
            Current = oldSession; editor = oldEditor; workflow = "";
            SetClipSelection(null); inspectorScroll = 0; layoutRevision = -1;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
