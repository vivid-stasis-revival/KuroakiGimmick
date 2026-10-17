namespace KuroakiGimmick.UI;

internal sealed record ChartSizeWarningText(string Setting, string DefaultLimit, string Description,
    string Title, string Body, string Acknowledgement, string Cancel, string Confirm)
{
    internal const string Disabled = "Safety Limit: DISABLED";
    internal const string DisabledHint = "Congratulations. Your RAM is now a consumable resource.";
    internal static ChartSizeWarningText Get(bool chinese) => chinese ? new(
        "解除谱面大小限制（不推荐）", "默认限制：128 MiB",
        "允许加载任意大小的谱面文件。你的内存有多大，谱师的恶意就能有多大。",
        "警告：你正在解除最后一道防线",
        """
        你即将关闭 Kuroaki/Gimmick 的谱面文件大小保护。

        启用后：

        • 内存占用可能以一种令人不安的速度增长。
        • 软件可能卡死、崩溃，或者直接被操作系统处决。
        • 未保存的数据可能永久丢失。
        • 即使你有 128 GB 内存，也不意味着你的电脑同意这项决定。

        K/G 负责尝试加载，不负责说服你的操作系统活下去。

        请在继续前保存所有工作。由此产生的稳定性问题和数据丢失风险由用户自行承担。

        确认选项：
        """,
        "我已了解上述风险，并自愿参加本次内存压力测试。",
        "算了，活着挺好", "解除封印，我头铁") : new(
        "Remove Chart Size Limit (Not Recommended)", "Default limit: 128 MiB",
        "Allows loading chart files of any size. The more RAM you have, the more malicious your chart authors can get.",
        "WARNING: You Are Disabling the Last Line of Defense",
        """
        You are about to disable Kuroaki/Gimmick's chart file size protection.

        Once enabled:

        • Memory usage may grow at a deeply concerning rate.
        • The application may freeze, crash, or be outright executed by your operating system.
        • Unsaved data may be permanently lost.
        • Even if you have 128 GB of RAM, that doesn't mean your computer consented to this decision.

        K/G will attempt to load your chart. It will not negotiate with your operating system to keep itself alive.

        Please save all your work before proceeding. You assume responsibility for any resulting instability or data loss.

        Confirmation:
        """,
        "I understand the risks and voluntarily agree to participate in this memory stress test.",
        "Never Mind, I Choose Life", "Break the Seal, I Fear Nothing");
}
