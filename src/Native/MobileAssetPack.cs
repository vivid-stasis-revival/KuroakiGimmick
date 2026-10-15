#if KG_MOBILE
using System.IO.Compression;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Native;

internal static class MobileAssetPack
{
    internal static bool Ready(string directory)
    {
        if (!File.Exists(Path.Combine(directory, "Brand", "kuroaki.png"))) return false;
        try
        {
            _ = NoteSkinProfile.Load(directory);
            string shaders = Path.Combine(directory, "MobileShaders");
            if (!Directory.Exists(shaders)) return false;
            uint format = OperatingSystem.IsIOS() ? (uint)Sdl.GpuShaderFormat.Msl : (uint)Sdl.GpuShaderFormat.SpirV;
            return Directory.EnumerateFiles(shaders, "*.kgms").Any(file =>
            {
                using var reader = new BinaryReader(File.OpenRead(file));
                return reader.BaseStream.Length >= 12 && reader.ReadInt32() == 0x48534D4B && reader.ReadInt32() == 1 && reader.ReadUInt32() == format;
            });
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.Text.Json.JsonException) { return false; }
    }

    // Resource packs are supplied by the user. Staging never replaces a working pack.
    internal static string Install(string zip, string root)
    {
        string staging = Path.Combine(root, "AssetImport-" + Guid.NewGuid().ToString("N"));
        string destination = Path.Combine(root, "Assets");
        Directory.CreateDirectory(staging);
        try
        {
            using var archive = ZipFile.OpenRead(zip);
            if (archive.Entries.Count > 20000 || archive.Entries.Sum(e => e.Length) > 2L * 1024 * 1024 * 1024)
                throw new InvalidDataException("Resource ZIP exceeds the import limit.");
            foreach (var e in archive.Entries)
                if (e.FullName.Contains('\\') || e.FullName.Split('/').Contains("..") || e.FullName.StartsWith('/') ||
                    (e.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                    throw new InvalidDataException("Unsafe resource ZIP path.");
            archive.ExtractToDirectory(staging);
            string assets = Directory.Exists(Path.Combine(staging, "Assets")) ? Path.Combine(staging, "Assets") : staging;
            if (!Ready(assets)) throw new InvalidDataException("ZIP must contain Assets/Brand and a complete note skin.");
            if (!Directory.Exists(Path.Combine(assets, "MobileShaders")))
                throw new InvalidDataException("Use a resource pack prepared for Android/iOS with MobileShaders.");
            // Preserve an earlier incomplete/user-created directory instead of deleting it.
            string backup = destination + "-previous-" + Guid.NewGuid().ToString("N");
            bool hadPrevious = Directory.Exists(destination);
            if (hadPrevious) Directory.Move(destination, backup);
            try { Directory.Move(assets, destination); }
            catch { if (hadPrevious) Directory.Move(backup, destination); throw; }
            return destination;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
}
#endif
