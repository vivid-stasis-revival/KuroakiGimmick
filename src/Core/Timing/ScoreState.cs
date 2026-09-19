namespace KuroakiGimmick.Core;

/// <summary>
/// 自动演奏下的判定流与两个滚动分数。原版 handle_judgement 是计分、连击和命中特效的同一个入口，
/// 这里同样把它们合成一张判定表：谱面加载完就把每一次判定的时刻、轨道和特效种类一次算好，
/// 之后 HUD 与判定特效都只按时间二分这张表，不保留任何跨帧状态，拖时间轴、倒放和导出结果一致。
/// </summary>
public sealed class ScoreState
{
    /// <summary>判定要出哪几样特效。原版由 handle_judgement_normal 的 arg5/arg6/arg7 三个开关组合而成。</summary>
    public enum HitKind
    {
        /// <summary>常规键：指示框 + 两簇音符颗粒 + 两簇钻尘。长条的头和尾同样走这一档。</summary>
        Note,
        /// <summary>宽键：宽指示框 + 三簇音符颗粒 + 两簇朝外侧横飞的钻尘。</summary>
        Wide,
        /// <summary>地雷：只结算不出特效，原版 autoplay 分支把 arg5..arg7 全传 false。</summary>
        Mine,
        /// <summary>长条中途的节拍判定：只有两簇钻尘，没有指示框也没有音符颗粒。</summary>
        HoldTick
    }

    /// <summary>
    /// 一次判定。Time 单位秒；Lane 是 0..3 常规、4..6 宽键的内部轨道号。
    /// Lead 是特效生成时音符距判定线还有多少毫秒，也就是原版 NoteModsX 的第一个参数去掉视觉延迟后的部分：
    /// 音符自己被判定时它是 0，长条的节拍点和尾判用的却是长条头的坐标，于是变成负数。
    /// </summary>
    public readonly record struct Hit(double Time, int Lane, HitKind Kind, double Lead);

    /// <summary>
    /// 判定档位：0=A.CRITICAL 1=CRITICAL 2=GREAT 3=GOOD 4=FAILED。
    /// 预览没有按键输入，走的是原版 obj_note_rendering 的 autoplay 分支，该分支一律判 A.CRITICAL。
    /// 接入真实判定后改这里即可，命中特效的配色帧号与 HUD 上的判定文本都由它导出。
    /// </summary>
    public const int Tier = 0;
    /// <summary>地雷在自己的时刻之后 timings[0] 毫秒才结算：原版 miss_timing 取的是 -timings[0]，不是 0。</summary>
    const double MineWindow = 35;
    /// <summary>长条尾部 150 毫秒不再切分节拍点，照抄 LoadSong 的 holdendms - notems - 150。</summary>
    const double HoldTailGrace = 150;
    /// <summary>单条长条的节拍点上限。原版没有这个限制，这里只是挡住畸形谱面（极高 BPM + 超长 hold）撑爆内存。</summary>
    const int MaxHoldParts = 100_000;

    /// <summary>按时间升序的全部判定。原版 notecount 正好等于它的长度：长条记 2 + parts，其余每个音符记 1。</summary>
    public List<Hit> Hits { get; } = [];
    /// <summary>原版 global.notecount。</summary>
    public int NoteCount => Hits.Count;
    /// <summary>原版 global.notevalue = 1000000 / notecount；没有音符时取 0，避免除零。</summary>
    public double NoteValue { get; }
    /// <summary>原版 global.minusscore：从 1010000 里扣掉失分。预览全是 A.CRITICAL，扣分项恒为 0。</summary>
    public const double MaxScore = 1010000;
    /// <summary>原版 global.accuracy.value：[100,100,75,50,0] 的滑动平均，初值也是 100，因此全 A.CRITICAL 时恒为 100。</summary>
    public const double Accuracy = 100;

    /// <summary>两个滚动数字每秒（曲目时间）最多爬多少。</summary>
    readonly double accRate, exRate;
    /// <summary>每次判定给目标值加多少：准确分 1.01 × notevalue，EX 分固定 3。</summary>
    readonly double accStep, exStep;
    /// <summary>第 k 次判定发生的那一瞬间，两个滚动数字各自已经爬到的位置。</summary>
    readonly double[] accAtHit, exAtHit;

    public ScoreState(Chart chart, BpmMap bpm)
    {
        foreach (var n in chart.Notes)
        {
            switch (n.Type)
            {
                case 0:
                    Hits.Add(new(n.Time, n.Lane, HitKind.Note, 0));
                    break;
                // 原版 LoadSong 把 type 1 与 type 8 一起塞进 4 + lane 的宽键轨道，计分上没有区别。
                case 1 or 8:
                    Hits.Add(new(n.Time, 4 + n.Lane, HitKind.Wide, 0));
                    break;
                case 6:
                    Hits.Add(new(n.Time + MineWindow / 1000, n.Lane, HitKind.Mine, 0));
                    break;
                case 7:
                    Hits.Add(new(n.Time + MineWindow / 1000, 4 + n.Lane, HitKind.Mine, 0));
                    break;
                case 2:
                    // msdelay 取 LoadSong 读到这条长条时的 BPM，也就是该时刻生效的变速段；
                    // 工程若指定了固定 BPM，这里跟着固定值走，与画面上的滚动保持同一套时间。
                    double perBeat = 60000 / bpm.BpmAtBeat(bpm.Beat(n.Time));
                    int parts = (int) Math.Min(MaxHoldParts, Math.Floor(Math.Max(0, (n.End - n.Time) * 1000 - HoldTailGrace) / perBeat));
                    Hits.Add(new(n.Time, n.Lane, HitKind.Note, 0));
                    for (int p = 1; p <= parts; p++)
                    {
                        Hits.Add(new(n.Time + p * perBeat / 1000, n.Lane, HitKind.HoldTick, -p * perBeat));
                    }
                    Hits.Add(new(n.End, n.Lane, HitKind.Note, (n.Time - n.End) * 1000));
                    break;
            }
        }
        Hits.Sort((a, b) => a.Time.CompareTo(b.Time));
        NoteValue = Hits.Count == 0 ? 0 : 1000000.0 / Hits.Count;
        // 原版每帧做 sdisp += timediff * 60 * (359800 / notecount)，而 timediff 是曲目时间差而不是真实帧时间，
        // 所以这两个追赶积分器其实是曲目位置的函数：同一份谱面在 t 处的值唯一确定，可以预先算好再闭式求值。
        accRate = Hits.Count == 0 ? 0 : 60 * 359800.0 / Hits.Count;
        exRate = 60;
        accStep = 1.01 * NoteValue;
        exStep = 3;
        accAtHit = new double[Hits.Count];
        exAtHit = new double[Hits.Count];
        for (int k = 1; k < Hits.Count; k++)
        {
            // 第 k 次判定发生的瞬间显示值还没来得及动，因此上限是前 k 次判定累计的目标值。
            double gap = Hits[k].Time - Hits[k - 1].Time;
            accAtHit[k] = Math.Min(k * accStep, accAtHit[k - 1] + accRate * gap);
            exAtHit[k] = Math.Min(k * exStep, exAtHit[k - 1] + exRate * gap);
        }
    }

    /// <summary>t 时刻已经结算的判定次数。原版全 A.CRITICAL，所以它同时就是 global.currentcombo。</summary>
    public int Judged(double time) => SortedSearch.UpperBound(Hits, time, h => h.Time);
    /// <summary>原版 global.currentcombo。A.CRITICAL 每次 +1；断连和 GOOD 的分支在自动演奏下走不到。</summary>
    public int Combo(double time) => Judged(time);
    /// <summary>原版 global.currentscore：不经滚动的即时准确分。</summary>
    public double CurrentScore(double time) => Judged(time) * accStep;
    /// <summary>原版 global.gamescore：EX 分的即时值，A.CRITICAL 每个 3 分。</summary>
    public int GameScore(double time) => 3 * Judged(time);
    /// <summary>原版 sdisp_accscore：右上角那个会追上来的准确分。</summary>
    public double AccScore(double time) => Rolling(time, accAtHit, accStep, accRate);
    /// <summary>原版 sdisp_exscore：EX 分的滚动显示，固定每秒 60，密集段会明显落后于 gamescore。</summary>
    public double ExScore(double time) => Rolling(time, exAtHit, exStep, exRate);

    /// <summary>最近一次判定的时刻，用于判定显示的补间与淡出；t 之前还没有判定时返回 null。</summary>
    public double? LastHit(double time)
    {
        int k = Judged(time);
        return k == 0 ? null : Hits[k - 1].Time;
    }

    /// <summary>min(目标值, 上一次判定时的位置 + 速率 × 已过时间)，即原版逐帧 min(sdisp + step, target) 的闭式。</summary>
    double Rolling(double time, double[] atHit, double step, double rate)
    {
        int k = Judged(time);
        return k == 0 ? 0 : Math.Min(k * step, atHit[k - 1] + rate * (time - Hits[k - 1].Time));
    }
}
