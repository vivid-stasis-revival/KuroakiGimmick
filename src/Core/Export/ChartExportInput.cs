using System.Text.Json;
using System.Text.Json.Nodes;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Core.Windows;

namespace KuroakiGimmick.Core;

/// <summary>已编辑文档的一份冻结快照。导出不会保存、也不会把编辑会话标记为已保存。</summary>
public sealed record ChartExportInput(ViewerProject Project, string? Images, string VsmText, byte[] VsmBytes,
    string ConfigText, string[] ProtectedPaths, string[] Notices, string? ImageText = null, string? ImageRoot = null,
    IReadOnlyDictionary<string, string>? TextSources = null, IReadOnlyDictionary<string, string>? ImageOriginalNames = null,
    VscDialect VscDialect = VscDialect.Legacy, IReadOnlyList<string>? Dependencies = null)
{
    /// <summary>从编辑器文档取快照。此后再改动文档不影响已生成的导出计划。</summary>
    public static ChartExportInput Capture(EditorDocument document, Session session)
    {
        var project = document.Project.Copy();
        // 只冻结当前会话实际解析的本地资源，不把目录当成导出依赖。
        string? songRoot = SongFiles.Root(project);
        var dependencies = new List<string>();
        void Local(string? path)
        {
            if (path != null && songRoot != null && File.Exists(path) && ChartExport.Inside(path, songRoot))
                dependencies.Add(Path.GetFullPath(path));
        }
        Local(session.NativeGimmick.Manifest); Local(session.NativeGimmick.ResourceManifest);
        foreach (var sprite in session.NativeGimmick.Sprites.Values)
            foreach (string frame in sprite.Frames) Local(frame);
        foreach (string path in session.NativeGimmick.TextureFiles.Values) Local(path);
        foreach (string path in session.NativeGimmick.Resources.Values) Local(path);
        Local(session.Fx.Path); Local(session.Fx.DynamicDefinitions);
        foreach (var layer in session.Fx.Layers) Local(layer.TexturePath);
        Local(session.GameUi.Manifest);
        if (project.GameUi != null && Directory.Exists(project.GameUi)) project.GameUi = session.GameUi.Manifest;
        if (session.GameUi.Data is { } ui)
        {
            foreach (string frame in ui.Sprites.Values.SelectMany(s => s.Frames)) Local(session.GameUi.File(frame));
            foreach (var font in ui.Fonts.Values) Local(session.GameUi.File(font.File));
        }
        Local(session.Jackets.DefaultPath);
        foreach (string path in session.Jackets.Files.Values) Local(path);
        project.EditorMarkers = document.Markers.ToList();
        var windows = document.CompiledWindows().AsInlineGameConfig();
        // 单独挂载的独立窗口文件并不等于整份 cgmk 配置。
        // 保留歌曲实际使用的配置，只覆盖其中被编辑过的窗口字段。
        string? root = SongFiles.Root(project);
        string? original = root == null ? null : SongFiles.Existing(root,
            Stem(project) + "_cgmk_config.json", "cgmk_config.json");
        JsonObject config = document.Windows.Standalone && original != null
            ? JsonNode.Parse(File.ReadAllText(original)) as JsonObject ?? throw new InvalidDataException("Expected a cgmk object.")
            : new JsonObject();
        foreach (var pair in windows.Root) config[pair.Key] = pair.Value?.DeepClone();
        // 事件已经内嵌，旧的 FILE 外链必须移除，否则游戏会去读那份过期文件。
        if (config[WindowMotionConfig.EventsKey] is JsonArray) config.Remove("ECG_WINDOW_MOVEMENT_FILE");
        string normalizedVsm = document.Vsm.NormalizedText;
        byte[] normalizedVsmBytes = document.Vsm.NormalizedBytes();
        var check = new Chart(); VsmReader.ReplaceModsText(check, normalizedVsm, "export.vsm");
        // 结构化编辑在提交时已校验过。导入的不透明行必须能逐字导出：
        // 报出它们的解析提示，而不是悄悄删掉。可解析事件则统一 canonicalize，成品里不保留浮点运算尾巴。
        if (normalizedVsmBytes.LongLength > 64L * 1024 * 1024)
            throw new InvalidDataException("VSM exceeds 64 MiB.");
        return new(project, document.Images.HasContent ? document.Images.SourcePath : null, normalizedVsm, normalizedVsmBytes,
            config.ToJsonString(ViewerProject.Json), document.ImportedPaths.Concat(new[] { project.Chart, project.Gimmick, project.WindowMotion }
                .Where(p => p != null).Select(p => Path.GetFullPath(p!))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            session.Chart.Diagnostics.Concat(check.Diagnostics).Select(d => d.Message).Distinct().ToArray(),
            document.Images.HasContent ? document.Images.Text : null, document.Images.ResourceRoot,
            document.TextSources.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal),
            document.Images.OriginalResourceNames(), session.Chart.VscDialect,
            dependencies.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }
    /// <summary>导出文件名的词干：取谱面/VSM 的难度名并去掉 ".editor" 后缀，避免把编辑副本的命名带进成品。</summary>
    public static string Stem(ViewerProject p)
    {
        string name = SongFiles.Difficulty(p.Chart ?? p.Gimmick ?? "ENCORE.vsm");
        if (name.EndsWith(".editor", StringComparison.OrdinalIgnoreCase)) name = name[..^7];
        return name;
    }
}
