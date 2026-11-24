namespace KuroakiGimmick.Core;

/// <summary>按来源索引访问的对话记录，选择和刷新使用独立控制项。</summary>
/// <param name="Sender">发送者标识或名称。</param>
/// <param name="Text">显示的消息内容。</param>
public sealed record NativeDialogueLine(string Sender, string Text);
