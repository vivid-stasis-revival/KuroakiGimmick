namespace KuroakiGimmick.Core.Editing;

/// <summary>
/// 编辑器自用的拍号书签，只存在工程文件的 EditorMarkers 里。它永远不会变成 VSM 事件或游戏关键帧，导出的谱面里不留任何痕迹。
/// Beat 单位是拍，Id 是它的身份，改名换拍都不换 Id。
/// </summary>
public sealed record TimelineMarker(Guid Id, double Beat, string Label);
