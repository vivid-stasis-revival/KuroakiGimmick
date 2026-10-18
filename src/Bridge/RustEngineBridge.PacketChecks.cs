using System.IO.MemoryMappedFiles;

namespace KuroakiGimmick.Core;

internal static unsafe partial class RustEngineBridge
{
    /// <summary>Exercise a >2 GiB native address range without allocating a multi-gigabyte managed array.</summary>
    internal static void PacketSizeChecks()
    {
        static void Reject(Action action, string expected)
        {
            try { action(); }
            catch (InvalidDataException e) when (e.Message.Contains(expected, StringComparison.Ordinal)) { return; }
            throw new InvalidOperationException("Native packet validation did not reject: " + expected);
        }
        using (ChartLoadLimits.Override(false))
            Reject(() => ValidatePacketLength((nuint)MaxPacketBytes + 1), "512 MiB budget");
#if !KG_MOBILE && !KG_SWITCH
        if (!Environment.Is64BitProcess) return;
        long largeLength = (long)int.MaxValue + 4097;
        using var unlimited = ChartLoadLimits.Override(true);
        if (ValidatePacketLength((nuint)largeLength) != largeLength)
            throw new InvalidOperationException("Unlimited native packets still have an array-size cap.");
        string path = Path.Combine(Path.GetTempPath(), "kg-native-packet-" + Guid.NewGuid().ToString("N"));
        try
        {
            // A sparse file reserves address space, not 2 GiB of physical memory or chart objects.
            using (var file = File.Create(path))
            {
                file.SetLength(largeLength);
                using var writer = new BinaryWriter(file);
                writer.Write("KGS2"u8); writer.Write(new byte[3]); // version + flags
                for (int i = 0; i < 6; i++) writer.Write(0u); // empty object name and record lists
                file.Position = largeLength - 1; writer.Write((byte)0x5a);
            }
            using var mapped = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            using var view = mapped.CreateViewAccessor(0, largeLength, MemoryMappedFileAccess.Read);
            byte* pointer = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
            try
            {
                using var stream = new UnmanagedMemoryStream(pointer + view.PointerOffset, largeLength);
                stream.Position = largeLength - 1;
                if (stream.ReadByte() != 0x5a) throw new InvalidOperationException("Native stream cannot read beyond 2 GiB.");
                stream.Position = 0;
                // The real chart decoder must reach structural validation, not an array allocation/cap.
                Reject(() => DecodeChart(stream, "packet-size"), "Trailing bytes");
            }
            finally { view.SafeMemoryMappedViewHandle.ReleasePointer(); }
        }
        finally { File.Delete(path); }
        Console.WriteLine("PASS native packet stream: >2 GiB address/seek/decode, limited budget, structural validation.");
#endif
    }
}
