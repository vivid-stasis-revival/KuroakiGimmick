using KuroakiGimmick.Core;

string root = Path.Combine(Path.GetTempPath(), "kg-backup-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Paths.LogDirectory = Path.Combine(root, "AppData");
void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
void Put(string path, string contents) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, contents); }
string Project(string folder)
{
    string path = Path.Combine(root, folder, "FINALE.sgv.json");
    Put(path, "original project");
    Put(Path.Combine(ProjectBackups.WorkingFolder(path), "FINALE.editor.vsm"), "working VSM");
    return path;
}
string Old(string path, string kind) => Path.Combine(Path.GetDirectoryName(path)!, ".kuroaki", kind, Path.GetFileName(path));
void ExpectFailure(Action action)
{
    try { action(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
    throw new Exception("Expected a safe I/O failure");
}
try
{
    string first = Project("Song A"), second = Project("Song B");
    Check(ProjectBackups.StorageRoot(first) != ProjectBackups.StorageRoot(second), "Same-name projects share backup storage");
    Check(ProjectBackups.StorageRoot(first) == ProjectBackups.StorageRoot(Path.Combine(Path.GetDirectoryName(first)!, ".", Path.GetFileName(first))), "Path normalization changed history identity");
    if (OperatingSystem.IsWindows()) Check(ProjectBackups.StorageRoot(first) == ProjectBackups.StorageRoot(first.ToUpperInvariant()), "Windows case changed history identity");
    ProjectBackups.Create(first); ProjectBackups.Create(second);
    string saved = Directory.GetDirectories(ProjectBackups.Folder(first)).Single();
    Check(File.ReadAllText(Path.Combine(saved, Path.GetFileName(first))) == "original project", "Project backup missing");
    Check(File.ReadAllText(Path.Combine(saved, ".kuroaki", "projects", Path.GetFileName(first), "FINALE.editor.vsm")) == "working VSM", "Working files absent from recovery snapshot");
    Check(!Directory.Exists(Old(first, "backup")), "New backup appeared inside the song");
    string pending = Path.Combine(Path.GetDirectoryName(first)!, ".kuroaki", "tmp", "pending", "0.old"); Put(pending, "recover me");

    string oldBackup = Path.Combine(Old(first, "backup"), "20200101T0000000-old");
    Put(Path.Combine(oldBackup, Path.GetFileName(first)), "old project");
    Put(Path.Combine(oldBackup, "nested", "assets.bin"), "old assets");
    Directory.CreateDirectory(Path.Combine(oldBackup, "empty"));
    string oldLegacy = Path.Combine(Old(first, "legacy"), "archive", "FINALE.editor.vsm"); Put(oldLegacy, "archived VSM");
    string otherProject = Path.Combine(Path.GetDirectoryName(Old(first, "backup"))!, "OTHER.sgv.json", "keep", "other"); Put(otherProject, "other project history");
    ProjectBackups.MigrateHistory(first);
    Check(!Directory.Exists(Old(first, "backup")) && !Directory.Exists(Old(first, "legacy")), "Old history was not removed after verified migration");
    Check(File.ReadAllText(Path.Combine(ProjectBackups.Folder(first), "20200101T0000000-old", "nested", "assets.bin")) == "old assets", "Nested snapshot data lost");
    Check(Directory.Exists(Path.Combine(ProjectBackups.Folder(first), "20200101T0000000-old", "empty")), "Empty snapshot directory lost");
    Check(File.ReadAllText(Path.Combine(ProjectBackups.StorageRoot(first), "legacy", Path.GetFileName(first), "archive", "FINALE.editor.vsm")) == "archived VSM", "Old legacy archive lost");
    Check(File.Exists(otherProject) && File.Exists(pending) && File.Exists(Path.Combine(ProjectBackups.WorkingFolder(first), "FINALE.editor.vsm")), "Migration touched other projects, working files or recovery data");
    int count = Directory.GetDirectories(ProjectBackups.Folder(first)).Length;
    ProjectBackups.MigrateHistory(first);
    Check(Directory.GetDirectories(ProjectBackups.Folder(first)).Length == count, "Repeated migration duplicated snapshots");

    // Retry after a completed copy: reuse equal destination; preserve both when names collide.
    Put(Path.Combine(Old(first, "backup"), "same", Path.GetFileName(first)), "same bytes");
    Put(Path.Combine(ProjectBackups.Folder(first), "same", Path.GetFileName(first)), "same bytes");
    ProjectBackups.MigrateHistory(first);
    Check(Directory.GetDirectories(ProjectBackups.Folder(first), "same*").Length == 1, "Retry duplicated an already verified snapshot");
    Put(Path.Combine(Old(first, "backup"), "same", Path.GetFileName(first)), "different bytes");
    ProjectBackups.MigrateHistory(first);
    Check(Directory.GetDirectories(ProjectBackups.Folder(first), "same*").Length == 2, "Name collision overwrote history");
    Check(File.ReadAllText(Path.Combine(ProjectBackups.Folder(first), "same", Path.GetFileName(first))) == "same bytes", "Collision damaged existing snapshot");

    string failing = Project("Failure");
    string retained = Path.Combine(Old(failing, "backup"), "snapshot", Path.GetFileName(failing)); Put(retained, "must survive");
    string appData = Paths.LogDirectory;
    Paths.LogDirectory = Path.Combine(root, "blocked"); File.WriteAllText(Paths.LogDirectory, "not a directory");
    ExpectFailure(() => ProjectBackups.MigrateHistory(failing));
    ExpectFailure(() => ProjectBackups.Write(failing, new Dictionary<string, byte[]> { [failing] = "replacement"u8.ToArray() }));
    Check(File.ReadAllText(retained) == "must survive" && File.ReadAllText(failing) == "original project", "Failed migration/save deleted or changed original data");
    Paths.LogDirectory = appData; ProjectBackups.MigrateHistory(failing);
    Check(!File.Exists(retained), "Migration could not resume after failure");

    if (!OperatingSystem.IsWindows())
    {
        string linkedProject = Project("Links");
        string outside = Path.Combine(root, "outside.bin"); Put(outside, "do not touch");
        string linked = Path.Combine(Old(linkedProject, "backup"), "snapshot", "link.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(linked)!); File.CreateSymbolicLink(linked, outside);
        ExpectFailure(() => ProjectBackups.MigrateHistory(linkedProject));
        Check(File.Exists(linked) && File.ReadAllText(outside) == "do not touch", "Migration followed/deleted a linked file");
    }

    string legacy = Path.Combine(Path.GetDirectoryName(second)!, "FINALE.editor.vsm"); Put(legacy, "loose legacy VSM");
    Put(legacy + ".bak", "legacy bak"); Put(second + ".bak", "project bak");
    ProjectBackups.ArchiveLegacy(second);
    string archiveRoot = Path.Combine(ProjectBackups.StorageRoot(second), "legacy", Path.GetFileName(second));
    string archive = Directory.GetDirectories(archiveRoot).Single();
    Check(File.ReadAllText(Path.Combine(archive, "FINALE.editor.vsm")) == "loose legacy VSM" && File.Exists(Path.Combine(archive, "FINALE.editor.vsm.bak"))
        && File.Exists(Path.Combine(archive, "FINALE.sgv.json.bak")), "Loose legacy files were not archived outside the song");
    Check(!File.Exists(legacy) && !File.Exists(legacy + ".bak") && !File.Exists(second + ".bak"), "Archived files still pollute the song directory");

    ProjectBackups.Write(second, new Dictionary<string, byte[]> { [second] = "updated project"u8.ToArray() });
    Check(File.ReadAllText(second) == "updated project", "Save no longer works");
    string badTarget = Path.Combine(Path.GetDirectoryName(second)!, "directory-target"); Directory.CreateDirectory(badTarget);
    ExpectFailure(() => ProjectBackups.Write(second, new Dictionary<string, byte[]> { [second] = "bad update"u8.ToArray(), [badTarget] = [1] }));
    Check(File.ReadAllText(second) == "updated project", "Failed transaction did not roll back committed files");
    for (int i = 0; i < 23; i++) ProjectBackups.Create(second);
    ProjectBackups.Prune(second);
    Check(Directory.GetDirectories(ProjectBackups.Folder(second)).Length == 20, "Retention does not keep 20 external backups");
    Check(Directory.Exists(archive), "Pruning backups deleted legacy archives");
    Check(!Directory.Exists(Old(second, "backup")) && !Directory.Exists(Old(second, "legacy")), "Saving recreated local history");
    Console.WriteLine("PASS project history: external storage, path isolation, verified migration/retry/collisions, failure retention, link rejection, portable working/recovery files, legacy archives, save rollback and retention");
}
finally { Directory.Delete(root, true); }

namespace KuroakiGimmick.Core
{
    // The real production backup sources are compiled above; isolate their platform data root for tests.
    internal static class Paths { public static string LogDirectory { get; set; } = ""; }
}
