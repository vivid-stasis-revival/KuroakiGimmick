namespace KuroakiGimmick.Core;

/// <summary>Shared CGMK script lookup for playback and export, independent of editor working filenames.</summary>
internal static class MpfScriptSources
{
    internal sealed record Result(string? Root, string Difficulty,
        IReadOnlyDictionary<string, string> Files, IReadOnlyList<string> Errors);

    internal static string? Root(ViewerProject project) => project.MpfSourceRoot ?? SongFiles.Root(project);
    internal static string Difficulty(ViewerProject project) => project.MpfSourceDifficulty
        ?? SongFiles.Difficulty(project.Chart ?? project.Gimmick ?? "ENCORE.vsm");

    // Freeze the original song identity before SaveCopy replaces Gimmick with an editor working file.
    internal static void Preserve(ViewerProject project)
    {
        project.MpfSourceRoot ??= Root(project);
        project.MpfSourceDifficulty ??= Difficulty(project);
    }

    internal static Result Resolve(ViewerProject project) => FindFiles(Root(project), Difficulty(project));

    internal static Result FindFiles(string? root, string difficulty)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var errors = new List<string>();
        if (root == null) return new(root, difficulty, files, errors);
        string dir = Path.Combine(root, "cmpf");
        try
        {
            // Enumerate directly: Directory.Exists would hide access-denied errors.
            string[] candidates;
            try
            {
                candidates = Directory.EnumerateFiles(dir)
                    .Where(path => path.EndsWith(".vsmpf", StringComparison.OrdinalIgnoreCase))
                    .Take(257).ToArray();
            }
            catch (DirectoryNotFoundException) when (!File.Exists(dir)) { return new(root, difficulty, files, errors); }
            if (candidates.Length > 256)
                throw new InvalidDataException("cmpf contains too many MPF files (256 maximum).");
            Array.Sort(candidates, StringComparer.Ordinal);
            foreach (int pass in new[] { 0, 1 })
            foreach (string path in candidates)
            {
                string stem = Path.GetFileName(path)[..^6];
                int dot = stem.IndexOf('.');
                if (dot < 1 || dot == stem.Length - 1) continue;
                string selector = stem[..dot], name = stem[(dot + 1)..];
                if (pass == 0 && selector.Equals("GLOBAL", StringComparison.OrdinalIgnoreCase)) files[name] = path;
                if (pass == 1 && selector.Equals(difficulty, StringComparison.OrdinalIgnoreCase)) files[name] = path;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            files.Clear();
            errors.Add("Cannot discover MPF scripts in " + dir + ": " + ex.Message);
        }
        return new(root, difficulty, files, errors);
    }
}
