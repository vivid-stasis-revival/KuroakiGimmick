using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KuroakiGimmick.Core.Editing;

public sealed partial class EditorDocument
{
    /// <summary>文本轨 ID 到其源文件全文的映射。值是逐字保留的原文，不是解析结果——改一条 cue 也只重写那一行。</summary>
    readonly Dictionary<string, string> texts = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, string> TextSources => texts;
    /// <summary>把全部文本源序列化成一个可比较的串。按 key 排序是为了让快照只随内容变化，不随字典插入顺序变化。</summary>
    string TextSnapshot() => AppJson.Serialize(texts.OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Value));
    void RestoreTexts(string value)
    {
        texts.Clear();
        foreach (var pair in AppJson.Deserialize<Dictionary<string, string>>(value)!) texts.Add(pair.Key, pair.Value);
    }
    /// <summary>用编辑中的文本源（而非磁盘上的文件）装载一份 CustomText，保证预览与时间轴看到的都是未保存的当前内容。</summary>
    public CustomText EditableTexts() => CustomText.Load(Project, new Chart(), texts, true);
    static void TextBeat(double beat)
    {
        if (!double.IsFinite(beat) || Math.Abs(beat) > 1e8) throw new FormatException("Text beat must be finite and within +/-100000000.");
    }
    /// <summary>
    /// 新建一个文本对象：分配未被占用的 textN 名字，写入首条 cue 与一整套初始属性，必要时顺带切到 obj_custom_gimmick 并打开 ENABLE_TEXT。
    /// 判重时连 name + "b" 一并排除，因为带 b 后缀的 ID 在源格式里指向同一对象的第二行。
    /// </summary>
    public string AddText(string content, double beat, bool enableCustom)
    {
        TextBeat(beat);
        var chart = new Chart(); VsmReader.ReplaceModsText(chart, Vsm.Text, "text.vsm");
        // 切对象和开 ENABLE_TEXT 都会影响整份谱面的表现，必须由调用方先拿到用户确认。
        if ((chart.ObjectName != "obj_custom_gimmick" || Windows.Root["ENABLE_TEXT"]?.ToString() == "false") && !enableCustom)
            throw new InvalidOperationException("Confirm Custom object and ENABLE_TEXT before adding text.");
        // 空字符串 ID 代表旧版无名文本。游戏里旧版文件优先级更高，此时新建具名文本只会被它盖掉，所以直接拒绝。
        if (texts.ContainsKey("")) throw new InvalidOperationException("This chart uses legacy text. Edit its existing text object; legacy files take precedence over named text in the game.");
        var reserved = texts.Keys.Concat(Vsm.Clips.Select(c => CustomText.TryMod(c.Name, out _, out var id) ? id : "")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string name = "text1"; for (int i = 2; reserved.Contains(name) || reserved.Contains(name + "b"); i++) name = "text" + i;
        Change("Add text " + name, () =>
        {
            if (chart.ObjectName != "obj_custom_gimmick") Vsm.SetHeader("obj", "obj_custom_gimmick");
            Windows.Root["ENABLE_TEXT"] = true;
            texts.Add(name, ""); WriteTextCue(name, beat, content);
            foreach (var pair in new Dictionary<string, double> { ["textX"] = 160, ["textY"] = 90, ["textscale"] = 1,
                ["textrot"] = 0, ["textalp"] = 1, ["textalignh"] = 1, ["textalignv"] = 1, ["textsep"] = -1 })
                Vsm.Add(new(Guid.NewGuid(), beat, 0, "linear", VsmDocument.N(pair.Value), VsmDocument.N(pair.Value), TextValueSampler.Name(pair.Key, name), -1));
        });
        return name;
    }
    /// <summary>
    /// 在文本源里就地增删改一行 cue：无关行、注释与原有换行符逐字保留，行尾也沿用文件里第一种出现的换行。
    /// 同一拍出现多条 cue 时拒绝处理，不会悄悄合并——那两行各自有意义，该由用户决定留哪条。content 传 null 表示删除该行。
    /// 换行与制表符按源格式编码成 {n} / {t}；文本源上限 4 MiB。
    /// </summary>
    void WriteTextCue(string id, double beat, string? content)
    {
        TextBeat(beat);
        if (!texts.TryGetValue(id, out string? raw)) throw new InvalidOperationException("Text object no longer exists.");
        var lines = Regex.Matches(raw, @"([^\r\n]*)(\r\n|\r|\n|$)").Cast<Match>().Where(m => m.Length > 0).ToArray();
        var matches = lines.Where(m =>
        {
            string body = m.Groups[1].Value.TrimStart('\uFEFF'); int comma = body.IndexOf(',');
            return comma >= 0 && double.TryParse(body[..comma], NumberStyles.Float, CultureInfo.InvariantCulture, out double at) && Math.Abs(at - beat) < 1e-9;
        }).ToArray();
        if (matches.Length > 1) throw new InvalidOperationException("Duplicate text cue times: resolve the source rows before editing this cue.");
        string ending = lines.Select(m => m.Groups[2].Value).FirstOrDefault(s => s.Length > 0) ?? "\n";
        string encoded = content == null ? "" : VsmDocument.N(beat) + "," + content.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "{n}").Replace("\t", "{t}");
        string next;
        if (matches.FirstOrDefault() is { } match)
            next = raw[..match.Index] + (content == null ? "" : encoded + match.Groups[2].Value) + raw[(match.Index + match.Length)..];
        else if (content == null) return;
        else next = raw + (raw.Length > 0 && !raw.EndsWith('\n') && !raw.EndsWith('\r') ? ending : "") + encoded + ending;
        if (Encoding.UTF8.GetByteCount(next) > 4 * 1024 * 1024) throw new InvalidDataException("Text file exceeds 4 MiB.");
        texts[id] = next;
    }
    public void SetTextCue(string id, double beat, string content) => Change("Edit text cue", () => WriteTextCue(id, beat, content));
    public void DeleteTextCue(string id, double beat) => Change("Delete text cue", () => WriteTextCue(id, beat, null));
    /// <summary>移动一条 cue = 先删原位再写新位，合成一次撤销操作。目标拍已有 cue 则拒绝，避免两条内容被并成一条。</summary>
    public void MoveTextCue(string id, double from, double to)
    {
        TextBeat(to);
        if (Math.Abs(from - to) < 1e-9) return;
        var track = EditableTexts().Tracks.Single(t => t.Id == id);
        if (track.Cues.Any(c => Math.Abs(c.Beat - to) < 1e-9)) throw new InvalidOperationException("A text cue already exists at the target beat.");
        string content = track.Cues.Single(c => Math.Abs(c.Beat - from) < 1e-9).Text;
        Change("Move text cue", () => { WriteTextCue(id, from, null); WriteTextCue(id, to, content); });
    }
    /// <summary>文本对象出场的那一拍：取其属性事件与 cue 里最早的一个，都没有则为 0。</summary>
    public double InitialTextBeat(string id) => Vsm.Clips.Where(c => CustomText.TryMod(c.Name, out _, out var target) && target == id)
        .Select(c => c.Beat).Concat(EditableTexts().Tracks.Where(t => t.Id == id).SelectMany(t => t.Cues).Select(c => c.Beat)).DefaultIfEmpty(0).Min();

    /// <summary>
    /// 写入或改写文本属性：duration 为 0 时打关键帧，大于 0 时生成一段补间。duration 单位是拍，按起点 BPM 折算。
    /// starts 省略时起值取采样器在该拍的实际取值，这样补间从画面当前状态接着走，不会跳一下。
    /// 该通道上凡是出现 repeat、"_"、573613 哨兵、proxy 事件或未解析的源行，一律拒绝，改由原始事件编辑。
    /// </summary>
    public void SetTextProperties(string id, double beat, IReadOnlyDictionary<string, double> values, BpmMap map, double duration = 0, string ease = "linear",
        IReadOnlyDictionary<string, double>? starts = null)
    {
        TextBeat(beat);
        if (!texts.ContainsKey(id)) throw new InvalidOperationException("Text object no longer exists.");
        if (!double.IsFinite(duration) || duration < 0 || duration > 1e8 || !Easings.IsKnown(ease)) throw new FormatException("Use a valid duration and easing.");
        var sampler = new TextValueSampler(Vsm, map);
        Change(duration > 0 ? "Animate text" : "Place text", () =>
        {
            foreach (var (kind, value) in values)
            {
                if (!CustomText.TryMod(kind, out _, out var tail) || tail.Length > 0 || !double.IsFinite(value) || Math.Abs(value) > 1e7 && kind is not ("textcolrgb" or "textcolhex"))
                    throw new FormatException("Invalid text property/value.");
                // 各属性的取值域：alpha 是 0..1，scale 绝对值 .001..1000，两个对齐值是 0/1/2 的整数，颜色是 0..0xFFFFFF 的打包 RGB。
                if (kind == "textalp" && (value < 0 || value > 1) || kind == "textscale" && (Math.Abs(value) < .001 || Math.Abs(value) > 1000) ||
                    kind is "textalignh" or "textalignv" && (value < 0 || value > 2 || value != Math.Floor(value)) ||
                    kind is "textcolrgb" or "textcolhex" && (value < 0 || value > 16777215)) throw new FormatException("Text value is outside its supported range.");
                string name = TextValueSampler.Name(kind, id);
                if (starts?.TryGetValue(kind, out double startValue) == true && (!double.IsFinite(startValue) ||
                    kind == "textalp" && (startValue < 0 || startValue > 1))) throw new FormatException("Invalid animation start value.");
                // textX_foo 与 textX_foob 在源格式里会互相混淆（b 后缀指同一对象的第二行），同时存在 foo 与 foob 时无法确定该写哪一个。
                if (id.Length > 0 && kind is "textX" or "textY" && texts.Keys.Any(other => other != id && (id == other + "b" || other == id + "b")))
                    throw new InvalidOperationException("Ambiguous trailing-b text IDs: use the raw event editor.");
                var clips = Vsm.Clips.Where(c => c.Name == name).ToArray();
                if (clips.Any(c => c.RepeatEnd != null || c.From == "_" || c.To == "_" || c.From == "573613" || c.To == "573613" || c.Proxy != -1) ||
                    Vsm.Lines.Any(l => l.Event == null && l.Text.Contains("," + name + ",", StringComparison.Ordinal)))
                    throw new InvalidOperationException("Dynamic/repeated or unparsed text channel: edit its raw events.");
                double end = map.Beat(map.Time(beat) + duration * 60 / map.BpmAtBeat(beat));
                // 两类重叠都要挡：既有动画的区间盖住了新写入点，或新区间内部还夹着别的事件。比较统一带 1e-9 容差，首尾相接不算重叠。
                if (clips.Any(c => c.Duration > 0 && map.Time(c.Beat) + c.Duration * 60 / map.BpmAtBeat(c.Beat) > map.Time(beat) + 1e-9 && c.Beat <= end + 1e-9) ||
                    duration > 0 && clips.Any(c => c.Beat > beat + 1e-9 && c.Beat < end - 1e-9))
                    throw new InvalidOperationException("This would overlap an existing text animation. Edit the original event or choose another time.");
                var existing = clips.Where(c => Math.Abs(c.Beat - beat) < 1e-9).ToArray();
                if (existing.Length > 1) throw new InvalidOperationException("Multiple writes at this beat: use the raw event editor.");
                // 该拍已有事件就沿用它的 Id 就地改写，保持事件身份与行序；只有确实要新增时才发新 Guid。
                var next = new VsmDocument.Clip(existing.FirstOrDefault()?.Id ?? Guid.NewGuid(), beat, duration, Easings.Normalize(ease),
                    VsmDocument.N(duration > 0 ? starts?.GetValueOrDefault(kind, sampler.Get(name, beat)) ?? sampler.Get(name, beat) : value), VsmDocument.N(value), name, -1);
                if (existing.Length == 1) Vsm.Replace(next); else Vsm.Add(next);
            }
        });
    }

    /// <summary>
    /// 只改某段文本动画的起值或终值，时间与缓动一概不动。
    /// 目标通道必须恰好有一条与所选动画同拍、同时长、同缓动的简单事件；命中不唯一就说明这几条在源里各有来历，不替用户挑。
    /// </summary>
    public void SetTextAnimationEndpoint(string id, Guid animation, bool end, IReadOnlyDictionary<string, double> values)
    {
        var selected = Vsm.Find(animation) ?? throw new InvalidOperationException("Animation no longer exists.");
        if (selected.Duration <= 0) throw new InvalidOperationException("Choose a text animation first.");
        Change(end ? "Text animation end" : "Text animation start", () =>
        {
            foreach (var (kind, value) in values)
            {
                string name = TextValueSampler.Name(kind, id);
                var channel = Vsm.Clips.Where(c => c.Name == name).ToArray();
                if (channel.Count(c => c.Beat == selected.Beat) > 1 || channel.Any(c => c.RepeatEnd != null || c.From is "_" or "573613" || c.To is "_" or "573613"))
                    throw new InvalidOperationException("Conflicting or dynamic text channel: use its raw events.");
                var candidates = channel.Where(c => c.Beat == selected.Beat && c.Duration == selected.Duration && c.Ease == selected.Ease).ToArray();
                if (candidates.Length != 1 || candidates[0].RepeatEnd != null || candidates[0].Proxy != -1 ||
                    candidates[0].From is "_" or "573613" || candidates[0].To is "_" or "573613")
                    throw new InvalidOperationException("This property is not a simple channel of the selected animation. Use KEY HERE or raw events.");
                if (!double.IsFinite(value) || kind == "textalp" && (value < 0 || value > 1) ||
                    kind == "textscale" && (Math.Abs(value) < .001 || Math.Abs(value) > 1000) || Math.Abs(value) > 16777215)
                    throw new FormatException("Invalid text animation endpoint.");
                var clip = candidates[0];
                Vsm.Replace(end ? clip with { To = VsmDocument.N(value) } : clip with { From = VsmDocument.N(value) });
            }
        });
    }
}
