using System.Text;

namespace KuroakiGimmick.Core;

/// <summary>
/// VSC1 二进制解码器：小端、字节对齐、单精度源数字。扩展 mod 在读到对象头时注册；截断和非法标记必须带字节位置报错。
/// </summary>
// VSC1：字节对齐的小端 GameMaker buffer；浮点字段一律 f32，读入后再升为 double。
public static class VsbReader
{
    public static Chart Load(string path, string? definitionPath = null, string profile = "auto")
    {
        if (new FileInfo(path).Length > 128 * 1024 * 1024)
        {
            throw new InvalidDataException("VSB exceeds the 128 MiB limit.");
        }
        using var stream = File.OpenRead(path);
        using var r = new BinaryReader(stream, Encoding.UTF8);
        if (!r.ReadBytes(5).SequenceEqual(new byte[]
        {
            86,
            83,
            67,
            1,
            0
        }))
        {
            throw new InvalidDataException("Expected VSC version 1 header (56 53 43 01 00).");
        }
        var c = new Chart
        {
            Title = Path.GetFileNameWithoutExtension(path)
        };
        try
        {
            while (true)
            {
                var flag = r.ReadByte();
                if (flag == 255)
                {
                    break;
                }
                // 0xC0/0xC1 包住音符段，0xE0 是 mod 段，0xFF 结束文件；
                // 未知标记一律带字节位置抛错，不跳过、不假装兼容。
                switch (flag)
                {
                    case 192:
                        while (true)
                        {
                            var f = r.ReadByte();
                            if (f == 193)
                            {
                                break;
                            }
                            if (f != 160)
                            {
                                throw Bad(r, f);
                            }
                            ReadNote(r, c);
                            if (c.Notes.Count > 2_000_000)
                            {
                                throw new InvalidDataException("Too many notes.");
                            }
                        }
                        break;
                    case 224:
                        ReadMods(r, c, Path.GetDirectoryName(path), definitionPath, profile);
                        break;
                    default:
                        throw Bad(r, flag);
                }
            }
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException($"Truncated VSB at byte {r.BaseStream.Position}.", ex);
        }
        // 音符按时间升序，后续查询才能二分；mod 事件保持读入顺序，由 Order 承载声明顺序。
        // 排序必须稳定：同一时刻的音符要保留文件里的先后，原版统计正是按读入顺序配对 jack/chain，
        // List.Sort 是不稳定的内省排序，同刻音符的次序会随实现变化。
        c.Notes.StableSortByTime();
        return c;
    }

    static void ReadNote(BinaryReader r, Chart c)
    {
        int type = 0, lane = 0;
        double time = 0;
        Dictionary<int, object>? extra = null;
        while (true)
        {
            var f = r.ReadByte();
            if (f == 161)
            {
                break;
            }
            switch (f)
            {
                case 162:
                    type = r.ReadByte();
                    break;
                case 163:
                    lane = r.ReadByte();
                    break;
                case 164:
                    time = Float(r);
                    break;
                // 0xA6..0xA7 是扩展字段表：每项先给类型标记再给 ID，值的宽度由类型标记决定。
                case 166:
                    while (true)
                    {
                        var t = r.ReadByte();
                        if (t == 167)
                        {
                            break;
                        }
                        var id = r.ReadByte();
                        (extra ??= new())[id] = t switch
                        {
                            176 => r.ReadByte(),
                            177 => r.ReadSByte(),
                            178 => r.ReadUInt32(),
                            179 => r.ReadInt32(),
                            181 => (double) BitConverter.UInt16BitsToHalf(r.ReadUInt16()),
                            182 => Float(r),
                            183 => r.ReadByte() != 0,
                            184 => CString(r),
                            _ => throw Bad(r, t)
                        };
                    }
                    break;
                default:
                    throw Bad(r, f);
            }
        }
        // 二进制里的时间是毫秒，统一在此除以 1000 转成秒；extra[1] 对 hold 是绝对结束时刻（毫秒），原样保留。
        double end = type == 2 && extra != null && extra.TryGetValue(1, out var e) ? Convert.ToDouble(e) / 1000 : time / 1000;
        if (type == 2 && end < time / 1000)
        {
            throw new InvalidDataException("Hold ends before its start.");
        }
        if ((type is 0 or 2 or 6 && lane > 3) || (type is 1 or 7 or 8 && lane > 2))
        {
            throw new InvalidDataException($"Invalid lane {lane} for note type {type}.");
        }
        c.Notes.Add(new Note(time / 1000, type, lane, end, extra ?? Note.EmptyExtra));
    }

    static void ReadMods(BinaryReader r, Chart c, string? songRoot, string? definitionPath, string profile)
    {
        string[] extraMods = [];
        while (true)
        {
            var f = r.ReadByte();
            if (f == 225)
            {
                return;
            }
            switch (f)
            {
                case 228:
                    c.Proxies = r.ReadByte();
                    c.ProxyCountDeclared = true;
                    if (c.Proxies > 64)
                    {
                        throw new InvalidDataException("At most 64 proxies are supported.");
                    }
                    break;
                case 229:
                    // 先读到对象名才能解析扩展 mod 表：表里的位置就是二进制编号，
                    // 因此这张表不排序、不去重、不剔除空项，任何重排都会让后面的 mod ID 整体错位。
                    c.ObjectName = CString(r);
                    extraMods = GimmickCatalog.ReadExtraMods(c.ObjectName, songRoot, definitionPath, profile);
                    break;
                case 226:
                    while (true)
                    {
                        var m = r.ReadByte();
                        if (m == 227)
                        {
                            break;
                        }
                        if (m == 233)
                        {
                            // beat/duration 的单位是拍，秒的换算交给 BPM map；缓动是索引，
                            // 超出 BinaryEases 范围时保留 ease_N 让报告能指出是哪一个，而不是退成 linear。
                            var beat = Float(r);
                            var duration = Float(r);
                            var ease = r.ReadByte();
                            var from = Float(r);
                            var to = Float(r);
                            var id = r.ReadByte();
                            var proxy = r.ReadSByte();
                            // GameMaker 把非正的 duration 当作立即赋值。
                            // Order 用当前计数，保留同一拍事件的二进制先后顺序。
                            c.Mods.Add(new(beat, duration, ease < ModCatalog.BinaryEases.Length? ModCatalog.BinaryEases[ease] : $"ease_{ease}",
                                from, to, ModCatalog.Decode(id, c.ObjectName, extraMods), proxy, c.Mods.Count));
                        }
                        else if (m == 236)
                        {
                            // 逐帧函数的区间同样是拍。
                            c.PerFrame.Add(new(Float(r), Float(r), CString(r)));
                        }
                        else
                        {
                            throw Bad(r, m);
                        }
                        if (c.Mods.Count + c.PerFrame.Count > 1_000_000)
                        {
                            throw new InvalidDataException("Too many gimmick events.");
                        }
                    }
                    break;
                default:
                    throw Bad(r, f);
            }
        }
    }

    static InvalidDataException Bad(BinaryReader r, int value) => new($"Unexpected VSC1 tag 0x{value:X2} at byte {r.BaseStream.Position - 1}.");
    /// <summary>源数字是 f32，读入后才升为 double；NaN/无穷直接拒绝，不让它扩散进时间轴。</summary>
    static double Float(BinaryReader r)
    {
        var x = r.ReadSingle();
        if (!float.IsFinite(x))
        {
            throw new InvalidDataException("Non-finite number in VSB.");
        }
        return x;
    }

    /// <summary>读 0 结尾的 UTF-8 字符串；64 KiB 内没有终止符就判为截断，避免损坏文件把整个流吃光。</summary>
    static string CString(BinaryReader r)
    {
        using var b = new MemoryStream();
        for (int i = 0; i < 65536; i++)
        {
            var x = r.ReadByte();
            if (x == 0)
            {
                return Encoding.UTF8.GetString(b.ToArray());
            }
            b.WriteByte(x);
        }
        throw new InvalidDataException("Unterminated VSB string.");
    }
}

