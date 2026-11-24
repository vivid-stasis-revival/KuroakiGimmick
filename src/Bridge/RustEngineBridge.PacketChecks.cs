using System.IO.MemoryMappedFiles;

namespace KuroakiGimmick.Core;

internal static unsafe partial class RustEngineBridge
{
    /// <summary>验证超过 2 GiB 的原生地址区间，不分配同等规模的托管数组。</summary>
    internal static void PacketSizeChecks()
    {
        static void Reject(Action action, string expected)
        {
            try
            {
                action();
            }
            catch (InvalidDataException e) when (e.Message.Contains(expected, StringComparison.Ordinal))
            {
                return;
            }
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
            // 用稀疏文件预留地址空间，避免分配 2 GiB 实体内存或谱面对象。
            using (var file = File.Create(path))
            {
                file.SetLength(largeLength);
                using var writer = new BinaryWriter(file);
                writer.Write("KGS2"u8);
                writer.Write(new byte[3]); // version + flags
                for (int i = 0; i < 6; i++) writer.Write(0u); // 对象名及各记录列表均为空。
                file.Position = largeLength - 1;
                writer.Write((byte)0x5a);
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
                // 真实解码器应到达结构校验阶段，不能先因托管数组分配或长度上限中断。
                Reject(() => DecodeChart(stream, "packet-size"), "Trailing bytes");
            }
            finally
            {
                view.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }
        finally
        {
            File.Delete(path);
        }
        Console.WriteLine("PASS native packet stream: >2 GiB address/seek/decode, limited budget, structural validation.");
#endif
    }
}
