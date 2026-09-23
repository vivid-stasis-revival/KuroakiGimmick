namespace KuroakiGimmick.Core;

/// <summary>
/// 谱面的六项统计，对应原版 GetSongStats：CHIP / TECH / STREAM / CHORD / BURST / GIMMICK。
/// 公式与常数照抄 6.2.0.1 的 parseSongDataFromBinary + GetSongStats，改动其中任何一个数都会改变显示结果。
/// 原版按二进制里的读入顺序遍历音符，这里遍历的是已按时间排序的 Notes：同一时刻多个音符之间的先后
/// 可能与源文件不同，因而 jack / chain 的配对在同刻音符上可能有细微差异，其余统计与顺序无关。
/// </summary>
public sealed record SongStats(double Chip, double Tech, double Stream, double Chord, double Burst, double Gimmick, double Total)
{
    /// <summary>原版的取整：正数 floor(v+.5)，负数 ceil(v-.5)。不要换成 Math.Round（银行家舍入）。</summary>
    public static int Round(double v) => v >= 0 ? (int)Math.Floor(v + .5) : (int)Math.Ceiling(v - .5);

    public int RoundedChip => Round(Chip);
    public int RoundedTech => Round(Tech);
    public int RoundedStream => Round(Stream);
    public int RoundedChord => Round(Chord);
    public int RoundedBurst => Round(Burst);
    public int RoundedGimmick => Round(Gimmick);

    /// <summary>逐音符统计量，对应原版的 SongData 结构体。密度单位是"每秒桶"，时间一律用毫秒。</summary>
    public sealed record Counts(int Bumpers, int HoldNotes, int HoldPieces, int NormalNotes, int NoteCount,
        int MultiNoteCount, int MultiHoldNoteCount, double PeakDensity, double AverageDensity, double PeakBeatDensity,
        double JackStat, double ChainStat, int JackCount, int ChainCount);

    /// <summary>
    /// 遍历音符得到 SongData。<paramref name="lengthSeconds"/> 只决定密度桶的数量。
    /// ms_per_note 初值 1 并由途中的变速音符改写，这是原版行为：第一条变速点之前的长条会按毫秒计件。
    /// </summary>
    public static Counts Measure(IEnumerable<Note> notes, double lengthSeconds)
    {
        int bumpers = 0, holdNotes = 0, holdPieces = 0, normalNotes = 0, noteCount = 0, multiNoteCount = 0, multiHoldNoteCount = 0;
        int bucketCount = Math.Max(1, (int)(lengthSeconds * 1000.0 / 1000.0));
        var buckets = new double[bucketCount];
        var beatBuckets = new double[bucketCount];
        var timings = new HashSet<double>();
        var holds = new List<(double Start, double End)>();
        // lane 与时间的配对，按遍历顺序保存；jack / chain 只看相邻两项。
        var flat = new List<(int Lane, double Time)>();
        double msPerNote = 1;

        void Collide(double t)
        {
            if (!timings.Add(t))
            {
                multiNoteCount++;
            }
            if (holds.Count == 0)
            {
                return;
            }
            var kept = new List<(double Start, double End)>(holds.Count);
            foreach (var (start, end) in holds)
            {
                if (end >= t)
                {
                    kept.Add((start, end));
                }
                if (t >= start && t < end)
                {
                    multiHoldNoteCount++;
                }
            }
            holds.Clear();
            holds.AddRange(kept);
        }

        void Density(double t, double weight)
        {
            int index = (int)Math.Floor(t / 1000.0);
            if (index < 0)
            {
                return;
            }
            // 一个音符把权重摊进往后四个秒桶，这是原版的密度定义，不是笔误。
            for (int i = index; i < index + 4; i++)
            {
                if (i >= 0 && i < buckets.Length)
                {
                    buckets[i] += weight;
                }
            }
            if (index < beatBuckets.Length)
            {
                beatBuckets[index] += weight;
            }
        }

        foreach (var n in notes)
        {
            double t = n.Time * 1000;
            switch (n.Type)
            {
                case 0 or 6:
                    noteCount++;
                    normalNotes++;
                    flat.Add((n.Lane, t));
                    bool wasMulti = timings.Contains(t);
                    Collide(t);
                    Density(t, 1.0 - (wasMulti ? .2 : 0));
                    break;
                case 1 or 7 or 8:
                    noteCount++;
                    bumpers++;
                    flat.Add((n.Lane + 4, t));
                    Collide(t);
                    Density(t, .75);
                    break;
                case 2:
                    noteCount += 2;
                    holdNotes++;
                    Collide(t);
                    Density(t, .85);
                    flat.Add((n.Lane, t));
                    double end = n.End * 1000;
                    holds.Add((t, end));
                    int pieces = (int)Math.Floor(Math.Max(0, end - t - 150.0) / msPerNote);
                    noteCount += pieces;
                    holdPieces += pieces;
                    break;
                case 3:
                    if (n.Extra.TryGetValue(1, out object? raw) && Convert.ToDouble(raw) is var bpm && double.IsFinite(bpm) && bpm != 0)
                    {
                        msPerNote = 60000.0 / bpm;
                    }
                    break;
            }
        }

        double jackStat = 0, chainStat = 0;
        int jackCount = 0, chainCount = 0;
        for (int i = 0; i + 1 < flat.Count; i++)
        {
            var (lane, time) = flat[i];
            var (nextLane, nextTime) = flat[i + 1];
            if (lane >= 4)
            {
                continue;
            }
            if (lane == nextLane)
            {
                jackCount++;
                jackStat += Math.Max(0, 1000.0 - (nextTime - time));
            }
            // 左侧两条轨接左撞块、右侧两条接右撞块才算 chain；lane 4 与 6 分别是两个撞块。
            if ((lane is 0 or 1 && nextLane == 4) || (lane is 2 or 3 && nextLane == 6))
            {
                chainCount++;
                chainStat += Math.Max(0, 1000.0 - (nextTime - time));
            }
        }
        return new(bumpers, holdNotes, holdPieces, normalNotes, noteCount, multiNoteCount, multiHoldNoteCount,
            buckets.Length > 0 ? buckets.Max() : 0, buckets.Length > 0 ? buckets.Average() : 0,
            beatBuckets.Length > 0 ? beatBuckets.Max() : 0, jackStat, chainStat, jackCount, chainCount);
    }

    /// <summary>难度对总分指数的加成。BACKSTAGE 与 ENCORE 同值，未知难度按 0。</summary>
    public static double DifficultyMultiplier(string difficulty) => SongInfo.Normalize(difficulty) switch
    {
        "OPENING" => 0,
        "MIDDLE" => 1,
        "FINALE" => 2,
        "ENCORE" or "BACKSTAGE" => 2.5,
        _ => 0
    };

    /// <summary>
    /// 由统计量算出六项。plaudite / n7 在原版里长度减半、平均密度加倍，是写死在源码里的特例。
    /// <paramref name="gimmickWeight"/> 为 0 时 GIMMICK 项为 0，界面上也不显示这一行。
    /// </summary>
    public static SongStats Compute(Counts d, double lengthSeconds, string difficulty, string? chartId, double gimmickWeight)
    {
        double length = lengthSeconds, averageDensity = d.AverageDensity;
        if (chartId?.ToLowerInvariant() is "plaudite" or "n7")
        {
            length /= 2;
            averageDensity *= 2;
        }
        if (!(length > 0))
        {
            throw new InvalidDataException("Song length must be greater than zero to compute stats.");
        }
        double note = d.NormalNotes, hold = d.HoldNotes, bumper = d.Bumpers, combo = d.NoteCount, multi = d.MultiNoteCount;
        double notesNoPiece = combo - d.HoldPieces;
        double basePower = (((notesNoPiece + averageDensity + (multi * .75)) / (length / 3.0)) * 4.0) / 1.25;
        double chip = Math.Pow(Math.Max((((note / 1.5) - (multi / 2.5)) / (length / 20.0)) + basePower - 20.0, 0), 1.025);
        double stream = Math.Pow(Math.Max(0, (averageDensity * 5.0) + (basePower / 2.0) - (Math.Clamp(multi, 0, 300) / 10.0) - 40.0), 1.025);
        double t1 = ((bumper / 2.0) + (hold / 2.0)) / (length / 20.0);
        double t2 = (((d.JackStat / 2300.0) + (d.ChainStat / 2300.0)) - Math.Max(0, (length - 180.0) / 2.5)) / 1.5;
        double tech = Math.Pow(Math.Max(0, t1 + basePower + t2 - 20.0), 1.025);
        double burst = (d.PeakDensity * 4.0) - 30.0;
        double chord = Math.Pow(Math.Max(0, (multi / 1.5 / (length / 20.0)) + basePower), 1.025);
        double gimmick = gimmickWeight > 0 ? Math.Pow(gimmickWeight, .77) : 0;
        double total = chip > 0
            ? Math.Pow((chip * .8) + (tech * .8) + stream + (chord * .5) + (burst * .5), 1.0 + (DifficultyMultiplier(difficulty) / 100.0))
            : 0;
        return new(chip, tech, stream, chord, burst, gimmick, total);
    }
}
