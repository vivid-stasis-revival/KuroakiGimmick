namespace KuroakiGimmick.Core.Editing;

/// <summary>
/// 对原始 VSM 事件的一个可逆视图：只是把同拍同时长的几条事件拢在一起给 UI 看，分组与查看本身不改动任何源文件。
/// Editable 为 false 表示这组只能整体查看和删除，改值要走原始事件编辑器。
/// </summary>
/// <param name="ImageId">目标图片 ID。</param>
/// <param name="Clips">属于该动作的 VSM 事件。</param>
/// <param name="Editable">是否允许通过编辑器修改该组。</param>
public sealed record ImageMotionGroup(string ImageId, VsmDocument.Clip[] Clips, bool Editable)
{
    /// <summary>组的身份取第一条事件的 Id；分组本身没有身份，重新分组后要靠它把选中项找回来。</summary>
    public Guid Id => Clips[0].Id;
    /// <summary>组内首条事件的起始拍。</summary>
    public double Beat => Clips[0].Beat;
    /// <summary>组内首条事件的持续拍数。</summary>
    public double Duration => Clips[0].Duration;
    /// <summary>组内首条事件使用的缓动名称。</summary>
    public string Ease => Clips[0].Ease;
    /// <summary>本组覆盖的通道并集，用来判断某次编辑请求的通道是否都在这组里。</summary>
    public ImageChannels Channels => Clips.Aggregate(ImageChannels.None, (c, clip) =>
        c | ImageEditPose.Channel(clip.Name[..clip.Name.IndexOf('_')]));
    /// <summary>时间轴上显示的短标签。RAW 优先于一切，提醒用户这组不能直接拖。</summary>
    public string Label => !Editable ? "RAW" : Duration <= 0 ? "POSE" : Channels switch
    {
        ImageChannels.Position => "MOVE",
        ImageChannels.Scale => "SCALE",
        ImageChannels.Rotation => "ROTATE",
        ImageChannels.Alpha => "OPACITY",
        _ => "TRANSFORM"
    };
    /// <summary>结束拍：duration 是按起点 BPM 折算的拍数，必须先换回秒再经 BPM map 转回拍，中途变速才不会算偏。</summary>
    /// <param name="map">在歌曲秒数与拍数之间转换的 BPM 映射。</param>
    /// <returns>按起点 BPM 折算后的结束拍。</returns>
    public double End(BpmMap map) => map.Beat(map.Time(Beat) + Math.Max(0, Duration) * 60 / map.BpmAtBeat(Beat));
}

