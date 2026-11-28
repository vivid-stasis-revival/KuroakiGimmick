namespace KuroakiGimmick.Core;

/// <summary>为原版剧情预览显式提供的存档上下文，预览期间不读写游戏存档。</summary>
/// <param name="PlayerName">预览剧情使用的玩家名。</param>
/// <param name="StoryProgress">预览使用的剧情进度值。</param>
/// <param name="SelectedTitle">已选择的称号，默认空字符串。</param>
public sealed record NativeStoryContext(string PlayerName, int StoryProgress = 0, string SelectedTitle = "") : KenomaGMK.Runtime.Sequence.NativeStoryContext(PlayerName, StoryProgress, SelectedTitle);
