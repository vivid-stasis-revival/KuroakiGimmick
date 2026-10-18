using System.Runtime.InteropServices;
using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    void SmokeMpfSourceEditor()
    {
        string root = Path.Combine(Path.GetTempPath(), "kg-mpf-source-ui-" + Guid.NewGuid().ToString("N"));
        var oldSession = Current; var oldEditor = editor;
        long oldResource = editorResourceRevision, oldRenderedResource = renderedResourceRevision;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException("MPF source UI: " + message); }
        void Frame() { renderedRevision = editor!.Revision; Draw(1440, 940); }
        void Mouse(uint type, float x, float y) => Handle(new() { Type = type, Button = 1, X = x, Y = y });
        void Type(string value)
        {
            nint ptr = Marshal.StringToCoTaskMemUTF8(value);
            try { Handle(new() { Type = 0x303, TextData = ptr }); }
            finally { Marshal.FreeCoTaskMem(ptr); }
        }
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "cmpf"));
            string vsm = Path.Combine(root, "ENCORE.vsm"), mpf = Path.Combine(root, "cmpf", "GLOBAL.wave.vsmpf");
            const string script = "::Main::\nset_mod_val(\"cover1\",0.2);\n";
            File.WriteAllText(vsm, "!obj:obj_custom_gimmick\nmpf\n0,4,wave\n0,4,newscript\n");
            File.WriteAllText(mpf, script);
            var project = new ViewerProject { Gimmick = vsm, Profile = "core", GameUiEnabled = false };
            Current = new(project); editor = new(Current); SetClipSelection(null);
            editorResourceRevision = renderedResourceRevision = 0;
            OpenSourceEditor(); Frame();
            // Actual MPF tab click: source input must let toolbar clicks through.
            Mouse(0x401, 24 + 250, 24 + 65); Frame(); Mouse(0x402, 24 + 250, 24 + 65); Frame();
            Check(sourceMpfTab && sourceMpfFiles.Count == 2 && ActiveMpfSource?.Path == mpf, "MPF tab did not discover GLOBAL and missing referenced scripts");
            var draft = ActiveMpfSource!;
            draft.Buffer.SelectAll(); Type(script.Replace("0.2", "0.8")); Frame();
            Check(File.ReadAllText(mpf) == script && draft.Buffer.Dirty, "typing wrote to disk before save");
            SelectMpfSource(1); Frame(); Check(!ActiveMpfSource!.Exists, "missing script did not offer a new draft");
            // Actual Ctrl+S creates the new file without overwriting any external source.
            Handle(new() { Type = 0x300, Scan = 22, Modifiers = 0x0040 }); Frame();
            Check(File.Exists(ActiveMpfSource.Path) && !ActiveMpfSource.Buffer.Dirty, "Ctrl+S did not save new MPF");
            SelectMpfSource(0); Frame(); Check(ReferenceEquals(ActiveMpfSource, draft) && draft.Buffer.Text.Contains("0.8"), "switching scripts discarded the draft");
            sourceVsm!.Select(0); sourceVsm.Insert("// separate VSM draft\n");
            sourceMpfTab = false; ApplySourceEditor();
            Check(workflow == "source" && draft.Buffer.Dirty && editor.Vsm.Text.StartsWith("// separate VSM draft"),
                "applying VSM discarded pending MPF drafts");
            sourceMpfTab = true;
            sourceVsm.Select(0); sourceVsm.Insert("// still pending\n");
            string vsmBefore = editor.Vsm.Text;
            SaveMpfSource(Path.Combine(root, "history")); Frame();
            Check(workflowError == "" && File.ReadAllText(mpf).Contains("0.8") && !draft.Buffer.Dirty && editor.Vsm.Text == vsmBefore,
                "save failed or unexpectedly applied the VSM draft");
            if (Environment.GetEnvironmentVariable("KG_MPF_SOURCE_SCREENSHOT") is { } screenshot) Canvas.SavePpm(UiTarget, screenshot);
            // A preview compiled before the disk edit must not replace a newer resource revision.
            var stale = new Session(project);
            editorBuild = Task.FromResult(stale); compilingDocument = editor; compilingRevision = editor.Revision;
            compilingResourceRevision = editorResourceRevision - 1;
            UpdateEditor();
            Check(!ReferenceEquals(Current, stale) && editorBuild != null, "stale preview was installed or replacement rebuild not queued");
            editorBuild!.GetAwaiter().GetResult(); UpdateEditor();
            Check(Math.Abs(Current.Timeline.Get("cover1", 1) - .8) < 1e-9 && renderedResourceRevision == editorResourceRevision,
                "rebuilt preview did not load saved MPF");
            draft.Buffer.SelectAll(); Type("::Main::\nset_mod_val(;");
            SaveMpfSource(Path.Combine(root, "history"));
            Check(workflow == "source" && workflowError.Length > 0 && File.ReadAllText(mpf).Contains("0.8"), "syntax error overwrote the file or closed the editor");
            draft.Buffer.Undo();
            File.WriteAllText(mpf, script + "// outside editor\n");
            SaveMpfSource(Path.Combine(root, "history"));
            Check(workflowError.Contains("changed outside", StringComparison.Ordinal) && File.ReadAllText(mpf).EndsWith("// outside editor\n"), "external modification was overwritten");
            CloseSourceEditor(); Check(editor.Vsm.Text == vsmBefore, "closing applied unrelated VSM drafts");
            OpenSourceEditor(); OpenMpfSources(); SelectMpfSource(sourceMpfFiles.FindIndex(file => file.Path == mpf)); Frame();
            Check(ActiveMpfSource!.Buffer.Text.EndsWith("// outside editor\n"), "reopen did not reload the external file");
            CloseSourceEditor();
            Console.WriteLine("PASS MPF source UI: tab, script switching, input, new-file Ctrl+S, explicit save, draft preservation, syntax/conflict errors and stale-build-safe preview reload");
        }
        finally
        {
            if (editorBuild != null) { editorBuild.GetAwaiter().GetResult(); editorBuild = null; }
            CloseSourceEditor(); sourceMpfTab = false; sourceMpfFiles.Clear(); sourceMpfDrafts.Clear(); sourceOwner = null;
            Current = oldSession; editor = oldEditor; editorResourceRevision = oldResource; renderedResourceRevision = oldRenderedResource;
            layoutRevision = renderedRevision = -1;
            Directory.Delete(root, true);
        }
    }
}
