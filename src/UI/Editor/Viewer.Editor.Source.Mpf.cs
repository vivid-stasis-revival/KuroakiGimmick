using KuroakiGimmick.Core;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    bool sourceMpfTab;
    int sourceMpfIndex;
    string sourceMpfStatus = "";
    readonly List<(string Name, string Path)> sourceMpfFiles = [];
    readonly Dictionary<string, MpfSourceFile> sourceMpfDrafts = new(StringComparer.Ordinal);
    MpfSourceFile? ActiveMpfSource => sourceMpfIndex >= 0 && sourceMpfIndex < sourceMpfFiles.Count
        ? sourceMpfDrafts.GetValueOrDefault(sourceMpfFiles[sourceMpfIndex].Path) : null;

    void OpenMpfSources()
    {
        if (sourceOwner == null) return;
        string? previous = ActiveMpfSource?.Path;
        sourceMpfTab = true; sourceSelecting = false; sourceTop = 0; sourceLeft = 0;
        sourceMpfFiles.Clear(); workflowError = sourceMpfStatus = "";
        var sources = MpfScriptSources.Resolve(sourceOwner.Project);
        foreach (var (name, path) in sources.Files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            sourceMpfFiles.Add((name, path));
        if (sources.Errors.Count > 0) workflowError = string.Join("; ", sources.Errors);
        // Missing referenced scripts get a draft at the expected difficulty filename. No file
        // is created until the author explicitly saves it.
        if (sources.Root != null && sourceVsm != null && sources.Errors.Count == 0)
        {
            var chart = new Chart(); VsmReader.ReplaceModsText(chart, sourceVsm.Text, "source.vsm");
            foreach (string name in chart.PerFrame.Select(frame => frame.Function).Distinct(StringComparer.Ordinal))
            {
                if (sources.Files.ContainsKey(name) || NativeFunctionTimeline.Supports(Current.NativeGimmick.Data, name)
                    || Current.NativeGimmick.Data?.PerFrameBindings.ContainsKey(name) == true) continue;
                static bool FilePart(string value) => value.Length is > 0 and <= 128 && value is not ("." or "..")
                    && !value.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c));
                if (!FilePart(name) || !FilePart(sources.Difficulty)) continue;
                sourceMpfFiles.Add((name, System.IO.Path.Combine(sources.Root, "cmpf", sources.Difficulty + "." + name + ".vsmpf")));
            }
        }
        sourceMpfIndex = Math.Max(0, sourceMpfFiles.FindIndex(file => file.Path == previous));
        if (sourceMpfFiles.Count > 0) SelectMpfSource(sourceMpfIndex);
        else sourceMpfStatus = L.Get("No MPF scripts. Add a named mpf range in VSM, then reopen this tab to create its script.");
    }

    void SelectMpfSource(int index)
    {
        if (sourceMpfFiles.Count == 0) return;
        sourceMpfIndex = (index + sourceMpfFiles.Count) % sourceMpfFiles.Count;
        sourceTop = 0; sourceLeft = 0; sourceSelecting = false; workflowError = "";
        string path = sourceMpfFiles[sourceMpfIndex].Path;
        try
        {
            if (!sourceMpfDrafts.ContainsKey(path)) sourceMpfDrafts.Add(path, new(path));
            sourceMpfStatus = path;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or FormatException)
        { workflowError = ex.Message; }
    }

    void SaveMpfSource(string? historyRoot = null)
    {
        if (ActiveMpfSource is not { } file || editor == null || sourceOwner != editor) return;
        if (editor.Revision != sourceRevision)
        { workflowError = L.Get("Document changed. Reopen the source editor before applying."); return; }
        try
        {
            if (file.Save(historyRoot)) editorResourceRevision++;
            workflowError = "";
            sourceMpfStatus = L.Get("MPF saved. Preview will rebuild; VSM/VSP drafts are unchanged.");
            message = sourceMpfStatus;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or FormatException)
        { workflowError = ex.Message; }
    }

    void DrawMpfSourcePicker(Rect box)
    {
        float y = box.Y + 92;
        if (WorkflowButton("<", new(box.X + 20, y, 30, 28), enabled: sourceMpfFiles.Count > 1)) SelectMpfSource(sourceMpfIndex - 1);
        string label = sourceMpfFiles.Count == 0 ? L.Get("NO MPF SCRIPT")
            : $"{sourceMpfIndex + 1}/{sourceMpfFiles.Count}  " + System.IO.Path.GetFileName(sourceMpfFiles[sourceMpfIndex].Path)
                + (ActiveMpfSource?.Buffer.Dirty == true ? " *" : "") + (ActiveMpfSource?.Exists == false ? "  " + L.Get("NEW") : "");
        Text(label, box.X + 62, y + 7, 13, white, true, box.W - 120);
        if (WorkflowButton(">", new(box.X + box.W - 50, y, 30, 28), enabled: sourceMpfFiles.Count > 1)) SelectMpfSource(sourceMpfIndex + 1);
    }
}
