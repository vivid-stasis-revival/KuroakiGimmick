namespace KuroakiGimmick.Core.Editing;

// 图片对象的直接操作层：把画布上的拖拽换算成对 VSM 事件的增删改。
// 这里只处理“简单到能安全生成”的事件（见 ImageObjectModel.Simple），凡是含糊、重叠或带动态取值的一律抛错，让用户去改原始事件，绝不猜测意图。
public sealed partial class EditorDocument
{
    /// <summary>换图不动任何事件：ID、图层、帧数、逻辑宽高全部保留，引用它的动画照旧生效。</summary>
    public void ReplaceImageResource(CustomImages.Item item, ImageImportBatch.Image image)
    {
        RequireImage(item.Id);
        Change("Replace image resource: " + item.Id, () => Images.ReplaceSource(item, image));
    }
    /// <summary>每次改动前复核 VSP 里还有这条声明——撤销或换图之后 UI 上的选中项可能已经不存在了。</summary>
    void RequireImage(string id)
    {
        if (!Images.Ids().Contains(id)) throw new InvalidOperationException("Image declaration no longer exists: " + id);
    }
    /// <summary>拍值必须有限且在 ±1e8 内，越界会在后面的 BPM 换算里放大成 NaN 并写进源文件。</summary>
    static void CheckBeat(double beat)
    {
        if (!double.IsFinite(beat) || Math.Abs(beat) > 1e8) throw new FormatException("Invalid image beat.");
    }
    /// <summary>
    /// 把 [begin, end] 这段拍区间换算成 VSM 的 duration。
    /// VSM 的 duration 是“按起点 BPM 折算的拍数”，所以先用 BPM map 求出实际秒差，再乘起点处的 BPM/60——中途变速的段落照样能算准。
    /// </summary>
    static double ImageDuration(BpmMap map, double begin, double end)
    {
        CheckBeat(begin); CheckBeat(end);
        if (end <= begin) throw new FormatException("Animation end must be after its start.");
        return (map.Time(end) - map.Time(begin)) * map.BpmAtBeat(begin) / 60;
    }
    /// <summary>
    /// 检查同一属性轨上 [beat, end] 是否与既有事件相撞；ignored 用来排除本次正在改写的那几条。
    /// 单点写入（beat == end）与区间写入判定条件不同，比较一律带 1e-9 容差，避免浮点误差把首尾相接误判成重叠。
    /// </summary>
    void CheckImageSpan(string name, double beat, double end, BpmMap map, HashSet<Guid>? ignored = null)
    {
        foreach (var c in Vsm.Clips.Where(c => c.Name == name && c.Proxy == -1 && ignored?.Contains(c.Id) != true))
        {
            // last 是这条事件真正结束的拍：duration 先按起点 BPM 折回秒，再转回拍，中途变速也不会算错。
            double last = map.Beat(map.Time(c.LastBeat) + Math.Max(0, c.Duration) * 60 / map.BpmAtBeat(c.LastBeat));
            bool collision = beat == end
                ? (c.RepeatEnd != null && c.Beat <= beat && last >= beat || c.Duration > 0 && c.Beat <= beat && last > beat)
                : c.Beat < end - 1e-9 && (c.Duration > 0 || c.RepeatEnd != null ? last > beat + 1e-9 : c.Beat > beat + 1e-9);
            if (collision) throw new InvalidOperationException("Overlapping " + name + ". Select its existing animation or edit the raw events.");
        }
        // 原样保留下来的未解析行如果也提到这个属性，就不能让生成的写入悄悄盖过它——我们读不懂那行，就无从判断谁该赢。
        if (Vsm.Lines.Any(l => l.Event == null && !l.Text.TrimStart().StartsWith("//", StringComparison.Ordinal) &&
            l.Text.Split(',').Length == 7 && l.Text.Split(',')[5].Trim() == name))
            throw new InvalidOperationException("Unparsed event for " + name + "; edit its source first.");
    }
    /// <summary>
    /// 设定图片的初始姿态，写在该图片最早一条姿态事件所在的拍上（<see cref="ImageObjectModel.InitialBeat"/>）。
    /// 同一拍上最多允许一条关键帧加一条出发动画，再多就说明意图不明确，交给原始事件编辑器。
    /// </summary>
    public void SetInitialImagePose(string id, ImageEditPose pose, ImageChannels channels, BpmMap map)
    {
        RequireImage(id); pose.Validate(channels);
        double beat = ImageObjectModel.InitialBeat(Vsm, id); CheckBeat(beat);
        Change("Set initial image pose: " + id, () =>
        {
            foreach (string p in ImageEditPose.Properties(channels))
            {
                string name = p + "_" + id;
                var at = Vsm.Clips.Where(c => c.Name == name && c.Proxy == -1 && Math.Abs(c.Beat - beat) < 1e-9).ToArray();
                var keys = at.Where(c => c.Duration == 0).ToArray();
                var outgoing = at.Where(c => c.Duration > 0).ToArray();
                if (keys.Length > 1 || outgoing.Length > 1 || at.Any(c => !ImageObjectModel.Simple(c)))
                    throw new InvalidOperationException("Ambiguous initial pose: " + name + ". Use its raw event.");
                CheckImageSpan(name, beat, beat, map, at.Select(c => c.Id).ToHashSet());
                var key = keys.FirstOrDefault(); var next = outgoing.FirstOrDefault();
                // 关键帧的终值与出发动画的起值本来就对不上，说明作者是故意要一个跳变，这里不替他决定该改哪一边。
                if (key != null && next != null && VsmDocument.Number(key.To) != VsmDocument.Number(next.From))
                    throw new InvalidOperationException("Initial pose and animation intentionally differ: " + name + ". Edit START instead.");
                string value = VsmDocument.N(pose.Get(p));
                if (key != null) Vsm.Replace(key with { From = value, To = value });
                else if (next == null) Vsm.Add(new(Guid.NewGuid(), beat, 0, "linear", value, value, name, -1));
                // 从初始拍出发的动画沿用的就是这个已经相连的姿态。两边一起改，才不会被同拍的补间盖回旧位置。
                if (next != null) Vsm.Replace(next with { From = value });
            }
        });
    }
    /// <summary>在任意一拍打一个姿态关键帧（duration 为 0 的事件）。该拍上已有动态事件或多条事件就拒绝，改由原始事件编辑。</summary>
    public void SetImagePose(string id, double beat, ImageEditPose pose, ImageChannels channels, BpmMap map)
    {
        RequireImage(id); CheckBeat(beat); pose.Validate(channels);
        Change("Set image pose: " + id, () =>
        {
            foreach (string p in ImageEditPose.Properties(channels))
            {
                string name = p + "_" + id;
                CheckImageSpan(name, beat, beat, map);
                var at = Vsm.Clips.Where(c => c.Name == name && c.Proxy == -1 && Math.Abs(c.Beat - beat) < 1e-9).ToArray();
                if (at.Length > 1 || at.Length == 1 && (!ImageObjectModel.Simple(at[0]) || at[0].Duration > 0))
                    throw new InvalidOperationException("Ambiguous/dynamic pose for " + name + "; select its raw event.");
                string n = VsmDocument.N(pose.Get(p));
                if (at.Length == 1) Vsm.Replace(at[0] with { From = n, To = n });
                else Vsm.Add(new(Guid.NewGuid(), beat, 0, "linear", n, n, name, -1));
            }
        });
    }
    /// <summary>
    /// 新建一段图片动画：选中的每个通道各生成一条事件，共用同一 start/duration/ease，从而在分组时被识别成同一组。
    /// start/end 单位是拍，duration 按起点 BPM 折算；返回第一条事件的 Id 供 UI 选中。
    /// </summary>
    public Guid AddImageAnimation(string id, double start, double end, ImageEditPose from, ImageEditPose to,
        ImageChannels channels, string ease, BpmMap map)
    {
        RequireImage(id); from.Validate(channels); to.Validate(channels);
        if (channels == ImageChannels.None) throw new FormatException("Choose at least one property.");
        if (!Easings.IsKnown(ease)) throw new FormatException("Unknown easing.");
        double duration = ImageDuration(map, start, end);
        Guid first = Guid.Empty;
        Change("Add image animation: " + id, () =>
        {
            foreach (string p in ImageEditPose.Properties(channels))
            {
                string name = p + "_" + id; CheckImageSpan(name, start, end, map);
                var c = new VsmDocument.Clip(Guid.NewGuid(), start, duration, ease, VsmDocument.N(from.Get(p)), VsmDocument.N(to.Get(p)), name, -1);
                if (first == Guid.Empty) first = c.Id;
                Vsm.Add(c);
            }
        });
        return first;
    }
    /// <summary>
    /// 按 Id 把 UI 里那份分组快照重新绑回文档当前的事件。分组只是视图，事件身份才是真的。
    /// 事件被删、变得不再“简单”，或几条的时间/缓动已经不一致，都要求用户重新选一次，绝不在错位的状态上继续改。
    /// </summary>
    ImageMotionGroup CurrentImageGroup(ImageMotionGroup group)
    {
        RequireImage(group.ImageId);
        var clips = group.Clips.Select(c => Vsm.Find(c.Id) ?? throw new InvalidOperationException("Animation was removed.")).ToArray();
        if (!group.Editable || clips.Any(c => !ImageObjectModel.Simple(c)))
            throw new InvalidOperationException("This animation needs the raw event editor.");
        if (clips.Any(c => c.Beat != clips[0].Beat || c.Duration != clips[0].Duration || c.Ease != clips[0].Ease || c.Proxy != clips[0].Proxy))
            throw new InvalidOperationException("Animation timing changed. Select its current group again.");
        return group with { Clips = clips };
    }
    /// <summary>
    /// 改一段动画的起点或终点姿态。linkNeighbors 为真时顺带把接在同一处的相邻事件改成同值，保持轨道连续。
    /// </summary>
    public void SetImageEndpoint(ImageMotionGroup group, bool end, ImageEditPose pose, ImageChannels channels, BpmMap map, bool linkNeighbors = true)
    {
        pose.Validate(channels); group = CurrentImageGroup(group);
        if (channels == ImageChannels.None || (group.Channels & channels) != channels)
            throw new InvalidOperationException("This animation does not contain all requested properties.");
        var ids = group.Clips.Select(c => c.Id).ToHashSet();
        Change("Edit image " + (end ? "end" : "start") + ": " + group.ImageId, () =>
        {
            foreach (var c in group.Clips)
            {
                CustomImages.TryMod(c.Name, out var p, out _);
                if ((ImageEditPose.Channel(p) & channels) == 0) continue;
                CheckImageSpan(c.Name, c.Beat, group.End(map), map, ids);
                string value = VsmDocument.N(pose.Get(p)), before = end ? c.To : c.From;
                Vsm.Replace(c.Duration <= 0 ? c with { From = value, To = value } : end ? c with { To = value } : c with { From = value });
                if (!linkNeighbors || c.Duration <= 0) continue;
                // 只有唯一一条、本来就与旧值严丝合缝相接的邻居才跟着改。命中多条或值对不上，说明那处跳变是作者有意为之，不动。
                var neighbor = Vsm.Clips.Where(n => n.Name == c.Name && !ids.Contains(n.Id) && ImageObjectModel.Simple(n) &&
                    (end ? Math.Abs(n.Beat - group.End(map)) < 1e-9 && VsmDocument.Number(n.From) == VsmDocument.Number(before)
                        : Math.Abs(map.Beat(map.Time(n.Beat) + n.Duration * 60 / map.BpmAtBeat(n.Beat)) - c.Beat) < 1e-9 && VsmDocument.Number(n.To) == VsmDocument.Number(before))).ToArray();
                if (neighbor.Length == 1)
                {
                    var n = neighbor[0];
                    Vsm.Replace(n.Duration <= 0 ? n with { From = value, To = value } : end ? n with { From = value } : n with { To = value });
                }
            }
        });
    }
    /// <summary>
    /// 整体平移/缩放一段动画的时间区间并换缓动，姿态值不变。原本是关键帧（duration 为 0）的仍保持为关键帧。
    /// 新起点上若已有别的动画起跑则拒绝：同拍两条动画谁先谁后取决于源文件顺序，不该由拖时间轴的动作来决定。
    /// </summary>
    public void TimeImageGroup(ImageMotionGroup group, double start, double end, string ease, BpmMap map)
    {
        group = CurrentImageGroup(group); CheckBeat(start);
        if (!Easings.IsKnown(ease)) throw new FormatException("Unknown easing.");
        double duration = group.Duration == 0 ? 0 : ImageDuration(map, start, end);
        var ignore = group.Clips.Select(c => c.Id).ToHashSet();
        Change("Time image animation", () =>
        {
            foreach (var c in group.Clips)
            {
                CheckImageSpan(c.Name, start, duration == 0 ? start : end, map, ignore);
                if (Vsm.Clips.Any(n => !ignore.Contains(n.Id) && n.Name == c.Name && n.Proxy == -1 && Math.Abs(n.Beat - start) < 1e-9 && n.Duration > 0))
                    throw new InvalidOperationException("Another animation starts here: " + c.Name);
                Vsm.Replace(c with { Beat = start, Duration = duration, Ease = ease });
            }
        });
    }
    /// <summary>删除整组事件。这里不走 CurrentImageGroup：即使该组已经不可直接编辑，用户仍然有权把它删掉。</summary>
    public void DeleteImageGroup(ImageMotionGroup group)
    {
        RequireImage(group.ImageId);
        Change("Delete image animation", () => { foreach (var c in group.Clips) Vsm.Delete(c.Id); });
    }
    /// <summary>把一整组动画复制到另一拍。复制体一律换新 Guid——事件身份不能共用，否则后续改一条会连带改到源。</summary>
    public Guid DuplicateImageGroup(ImageMotionGroup group, double beat, BpmMap map)
    {
        group = CurrentImageGroup(group); CheckBeat(beat); Guid first = Guid.Empty;
        Change("Duplicate image animation", () =>
        {
            foreach (var c in group.Clips)
            {
                double end = map.Beat(map.Time(beat) + c.Duration * 60 / map.BpmAtBeat(beat));
                CheckImageSpan(c.Name, beat, end, map);
                var copy = c with { Id = Guid.NewGuid(), Beat = beat };
                if (first == Guid.Empty) first = copy.Id; Vsm.Add(copy);
            }
        });
        return first;
    }
    /// <summary>
    /// 给整条运动路径加一个平移量：imgx/imgy 的每一条事件（含首尾值）统一加 dx/dy，单位是 320×180 逻辑空间的像素。
    /// 只要有一条事件的取值不是固定数值（"_" 或 573613 表示沿用当前值），整次操作就放弃——平移一个运行时才知道的值没有意义。
    /// 该轴上完全没有事件时，在初始拍补一条“默认值 + 偏移”的关键帧。
    /// </summary>
    public void OffsetImagePath(string id, double dx, double dy, BpmMap map)
    {
        RequireImage(id);
        if (!double.IsFinite(dx + dy) || Math.Abs(dx) > 1e7 || Math.Abs(dy) > 1e7) throw new FormatException("Invalid offset.");
        Change("Offset image path: " + id, () =>
        {
            foreach (var (property, delta) in new[] { ("imgx", dx), ("imgy", dy) })
            {
                if (delta == 0) continue;
                string name = property + "_" + id;
                if (Vsm.Lines.Any(l => l.Event == null && !l.Text.TrimStart().StartsWith("//", StringComparison.Ordinal) &&
                    l.Text.Split(',').Length == 7 && l.Text.Split(',')[5].Trim() == name))
                    throw new InvalidOperationException("Unparsed path event for " + name + "; edit its source first.");
                var rows = Vsm.Clips.Where(c => c.Name == name && c.Proxy == -1).ToArray();
                if (rows.Any(c => !ImageObjectModel.Fixed(c.From) || !ImageObjectModel.Fixed(c.To)))
                    throw new InvalidOperationException("Dynamic path values must be edited as raw events.");
                if (rows.Length == 0)
                {
                    double at = ImageObjectModel.InitialBeat(Vsm, id); string v = VsmDocument.N(CustomImages.Default(property) + delta);
                    Vsm.Add(new(Guid.NewGuid(), at, 0, "linear", v, v, property + "_" + id, -1));
                }
                foreach (var c in rows) Vsm.Replace(c with { From = VsmDocument.N(VsmDocument.Number(c.From) + delta), To = VsmDocument.N(VsmDocument.Number(c.To) + delta) });
            }
        });
    }
}
