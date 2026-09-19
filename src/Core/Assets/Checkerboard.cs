namespace KuroakiGimmick.Core;

/// <summary>
/// 可复用棋盘格的刷新时间表。固定种子保证拖动可重复；仅在 set 被消费时刷新，不按显示器帧率重新随机。
/// </summary>
// o_angelstar_checker：32×18 格，创建时为第 2 帧，之后只在 _set 控制被消费时
// 各自独立地做一次 irandom(2)。固定种子保证拖动和视频导出可重复；
// 它无法复现游戏里未知的 RNG。
public sealed class Checkerboard
{
    public string[] Frames { get; private set; } = [];
    public List<double> Updates { get; } = [];
    public bool Enabled { get; private set; }
    public int? FixedMode { get; private set; }
    public bool Ready => Enabled && Frames.Length == 3;
    public static Checkerboard Load(ViewerProject p, Chart chart, Timeline timeline)
    {
        var result = new Checkerboard();
        result.Enabled = chart.ObjectName == "obj_custom_gimmick" && SongFiles.Config(p, "ENABLE_ANGELSTAR_CHECKER")
            || timeline.Native?.Data?.Checkerboard == true;
        if (timeline.Native?.Data?.Checkerboard == true)
        {
            result.FixedMode = timeline.Native.Data.CheckerMode;
        }
        if (!result.Enabled)
        {
            return result;
        }
        string root = p.GimmickAssets ?? Paths.SharedAssetDirectory("GimmickExtras");
        var frames = Enumerable.Range(0, 3).Select(i => Path.Combine(root, $"sp_angelstar_checker_{i}.png")).ToArray();
        if (frames.All(File.Exists))
        {
            try
            {
                foreach (string file in frames)
                {
                    if (new FileInfo(file).Length > 1024 * 1024)
                    {
                        throw new InvalidDataException("Checker tile exceeds 1 MiB.");
                    }
                    using var stream = File.OpenRead(file);
                    var image = StbImageSharp.ImageInfo.FromStream(stream);
                    if (image == null || image.Value.Width != 10 || image.Value.Height != 10)
                    {
                        throw new InvalidDataException("Original checker tiles must be 10 x 10 pixels.");
                    }
                }
                result.Frames = frames;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                chart.Diagnostics.Add(new("checker", 0, ex.Message, true));
            }
        }
        if (result.Frames.Length == 0 && chart.Mods.Any(e => e.Name.StartsWith("angelstar_checker_")))
        {
            chart.Diagnostics.Add(new("checker", 0,
                "Checker logic is available, but original sp_angelstar_checker_0/1/2.png tiles are missing. Run Dump_Gimmick_Extras_Mac.sh into the program folder's Assets/GimmickExtras, then press R. No substitute checker artwork is drawn."));
        }
        if (timeline.Tracks.TryGetValue(("angelstar_checker_set", -1), out var segments))
        {
            var times = new SortedSet<double>();
            for (int i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                double end = Math.Min(3600, s.Time + s.Duration);
                if (s.Time > 3600 || end <= 0)
                {
                    continue;
                }
                if (i + 1 < segments.Count)
                {
                    end = Math.Min(end, segments[i + 1].Time - 1e-9);
                }
                if (s.Duration <= 0)
                {
                    if (s.To > 0 && s.Time > 0 && s.Time <= 3600)
                    {
                        times.Add(s.Time);
                    }
                    if (times.Count > 4096)
                    {
                        break;
                    }
                    continue;
                }
                // tween 会在每个源 Step 重写一次被消费的控制值，因此按 60 Hz 逻辑 tick 逐帧采样。
                for (int frame = (int) Math.Max(1, Math.Ceiling(s.Time * 60)); frame <= Math.Floor(end * 60); frame++)
                {
                    if (s.Value(frame / 60.0) > 0)
                    {
                        times.Add(frame / 60.0);
                    }
                    if (times.Count > 4096)
                    {
                        break;
                    }
                }
                if (end == s.Time + s.Duration && s.To > 0 && end > 0)
                {
                    times.Add(end);
                }
                if (times.Count > 4096)
                {
                    break;
                }
            }
            if (times.Count > 4096)
            {
                result.Enabled = false;
                chart.Diagnostics.Add(new("checker", 0, "Checker exceeds 4096 refreshes/session; reduce the continuous _set tween duration.", true));
            }
            else
            {
                result.Updates.AddRange(times);
            }
        }
        return result;
    }

    /// <summary>二分统计 time 之前已发生的刷新次数；0 代表还没刷新过，对应创建时的固定第 2 帧。</summary>
    public int Generation(double time)
    {
        int lo = 0, hi = Updates.Count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (Updates[mid] <= time)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }
        return lo;
    }

    public static int CellFrame(int generation, int cell) => generation == 0 ? 2 : Math.Min(2,
        (int)(Timeline.Hash(unchecked((uint) generation * 9743 + (uint) cell * 137 + 9127)) * 3));
}

