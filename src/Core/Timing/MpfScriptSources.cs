namespace KuroakiGimmick.Core;

/// <summary>播放和导出共用的 CGMK 脚本查找规则，独立于编辑器工作副本的文件名。</summary>
internal static class MpfScriptSources
{
    internal sealed record Result(string? Root, string Difficulty,
        IReadOnlyDictionary<string, string> Files, IReadOnlyList<string> Errors);

    internal static string? Root(ViewerProject project) => project.MpfSourceRoot ?? SongFiles.Root(project);
    internal static string Difficulty(ViewerProject project) => project.MpfSourceDifficulty
        ?? SongFiles.Difficulty(project.Chart ?? project.Gimmick ?? "ENCORE.vsm");

    // SaveCopy 将 Gimmick 替换为工作文件前须保存原歌曲身份。
    internal static void Preserve(ViewerProject project)
    {
        project.MpfSourceRoot ??= Root(project);
        project.MpfSourceDifficulty ??= Difficulty(project);
    }

    internal static Result Resolve(ViewerProject project) => FindFiles(Root(project), Difficulty(project));

    internal static Result FindFiles(string? root, string difficulty)
    {
        var catalog = KenomaGMK.Runtime.Mpf.MpfSourceCatalog.FindFiles(root, difficulty);
        return new(catalog.Root, catalog.Difficulty, catalog.Files, catalog.Errors);
    }
}
