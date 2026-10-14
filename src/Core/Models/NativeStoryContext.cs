namespace KuroakiGimmick.Core;

/// <summary>Explicit source-save context used only for native story preview; never reads or writes the game's save.</summary>
public sealed record NativeStoryContext(string PlayerName, int StoryProgress = 0, string SelectedTitle = "");
