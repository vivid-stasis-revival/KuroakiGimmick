using Android.Content;
using Android.Database;
using Android.OS;
using Android.Provider;
using Root = Android.Provider.DocumentsContract.Root;
using Document = Android.Provider.DocumentsContract.Document;

namespace KuroakiGimmick.Mobile;

[ContentProvider(["io.github.kuroakigimmick.mobile.documents"], Exported = true, GrantUriPermissions = true,
    Permission = "android.permission.MANAGE_DOCUMENTS", Name = "io.github.kuroakigimmick.mobile.FilesProvider")]
[IntentFilter(["android.content.action.DOCUMENTS_PROVIDER"])]
public sealed class FilesProvider : DocumentsProvider
{
    static readonly string[] RootColumns = [Root.ColumnRootId, Root.ColumnDocumentId, Root.ColumnTitle, Root.ColumnFlags, Root.ColumnMimeTypes];
    static readonly string[] DocumentColumns = [Document.ColumnDocumentId, Document.ColumnDisplayName, Document.ColumnMimeType, Document.ColumnSize, Document.ColumnFlags];
    string BaseDirectory => Path.Combine(Context!.FilesDir!.AbsolutePath, "Documents");
    public override bool OnCreate() { Directory.CreateDirectory(BaseDirectory); return true; }
    public override ICursor QueryRoots(string[]? projection)
    {
        var cursor = new MatrixCursor(projection ?? RootColumns);
        var row = cursor.NewRow()!;
        row.Add(Root.ColumnRootId, "kuroaki"); row.Add(Root.ColumnDocumentId, "root:");
        row.Add(Root.ColumnTitle, "Kuroaki/Gimmick"); row.Add(Root.ColumnFlags, (int)DocumentRootFlags.LocalOnly);
        row.Add(Root.ColumnMimeTypes, "*/*");
        return cursor;
    }
    string Resolve(string? id)
    {
        if (id == null || !id.StartsWith("root:", StringComparison.Ordinal)) throw new Java.IO.FileNotFoundException();
        string path = Path.GetFullPath(Path.Combine(BaseDirectory, id[5..]));
        if (path != BaseDirectory && !path.StartsWith(BaseDirectory + "/", StringComparison.Ordinal)) throw new Java.IO.FileNotFoundException();
        return path;
    }
    void Add(MatrixCursor cursor, string path)
    {
        bool directory = Directory.Exists(path);
        if (!directory && !File.Exists(path)) throw new Java.IO.FileNotFoundException();
        var row = cursor.NewRow()!;
        string relative = Path.GetRelativePath(BaseDirectory, path);
        row.Add(Document.ColumnDocumentId, "root:" + (relative == "." ? "" : relative));
        row.Add(Document.ColumnDisplayName, path == BaseDirectory ? "Kuroaki/Gimmick" : Path.GetFileName(path));
        row.Add(Document.ColumnMimeType, directory ? Document.MimeTypeDir : "application/octet-stream");
        row.Add(Document.ColumnSize, directory ? 0L : new FileInfo(path).Length);
        row.Add(Document.ColumnFlags, 0);
    }
    public override ICursor QueryDocument(string? documentId, string[]? projection)
    {
        var cursor = new MatrixCursor(projection ?? DocumentColumns); Add(cursor, Resolve(documentId)); return cursor;
    }
    public override ICursor QueryChildDocuments(string? parentDocumentId, string[]? projection, string? sortOrder)
    {
        var cursor = new MatrixCursor(projection ?? DocumentColumns);
        foreach (string path in Directory.EnumerateFileSystemEntries(Resolve(parentDocumentId))) Add(cursor, path);
        return cursor;
    }
    public override ParcelFileDescriptor OpenDocument(string? documentId, string? mode, CancellationSignal? signal)
    {
        // Files UI may copy or share exports, while edits remain owned by K/G.
        if (mode != "r") throw new Java.IO.FileNotFoundException("Read-only provider.");
        return ParcelFileDescriptor.Open(new Java.IO.File(Resolve(documentId)), ParcelFileMode.ReadOnly)!;
    }
}
