using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    readonly UiMotion motion = new();
    float workspaceAlpha = 1, settingsAlpha, helpAlpha, valueAlpha, discardAlpha, infoCardAlpha, startupAlpha;
    bool? animatedWorkspace, animatedDiagnostics;
    string animatedInspector = "", animatedStatus = "";
    /// <summary>有覆盖层已经关闭、但淡出还没播完。这期间控件必须继续屏蔽，否则点击会穿到正在消失的那一层上。</summary>
    bool UiClosingOverlay => !referenceOpen && referenceAlpha > .001f ||
        !settings && settingsAlpha > .001f || !help && helpAlpha > .001f ||
        !modalActive && valueAlpha > .001f || pendingDiscard == null && discardAlpha > .001f ||
        !infoCard && infoCardAlpha > .001f || !startup && startupAlpha > .001f;
    // 阻塞型覆盖层会让下方的工作区失效。轨道帮助被刻意排除在外：
    // 把它自己的可见性当作其悬停来源的输入会构成一个单帧反馈回路
    // （显示帮助 -> overlay=true -> 悬停来源被清空 -> 隐藏帮助 -> overlay=false -> 又显示）。
    bool UiBlockingOverlayVisible => LayoutVisible || ImageImportVisible || WorkflowVisible || dialogOpen ||
        ReferenceVisible || settings || help || modalActive || menuOpen || pendingDiscard != null || UiClosingOverlay
        || InfoCardVisible || StartupVisible;
    /// <summary>含轨道帮助在内的"有东西挡着"判定；只用于抑制 seek、音量拖动这类工作区交互，不参与帮助自身的可见性计算。</summary>
    bool UiOverlayVisible => UiBlockingOverlayVisible || trackHelpVisualActive;

    /// <summary>
    /// 每帧绘制前推进 UI 过渡，必须在读取任何 *Alpha 之前调用。时间取自单调的 UI 时钟，与歌曲时间和渲染帧率无关；
    /// 关闭 UiAnimations 时所有 tween 立即落到目标值。工作区模式切换和状态栏换文案用 Snap 重新起跳，不做跨状态的插值。
    /// </summary>
    void BeginUiMotion()
    {
        motion.Begin(uptime.Elapsed.TotalSeconds, preferences.UiAnimations);
        if (animatedWorkspace != null && animatedWorkspace != editorMode) motion.Snap("workspace", 0);
        animatedWorkspace = editorMode;
        workspaceAlpha = motion.To("workspace", 1, .16);
        layoutAlpha = motion.Show("layout", layoutOpen, .16);
        imageImportAlpha = motion.Show("image-import", pendingImages != null, .16);
        workflowAlpha = motion.Show("workflow", workflow.Length > 0, .16);
        referenceAlpha = motion.Show("docs", referenceOpen, .18);
        settingsAlpha = motion.Show("settings", settings, .17);
        helpAlpha = motion.Show("quick-help", help, .16);
        valueAlpha = motion.Show("value-modal", modalActive, .15);
        discardAlpha = motion.Show("discard-modal", pendingDiscard != null, .15);
        infoCardAlpha = motion.Show("info-card", infoCard, .17);
        startupAlpha = motion.Show("startup", startup, .2);
        if (animatedStatus != message) { animatedStatus = message; motion.Snap("status", .4f); }
    }

    // 所有控件使用同一套短促、可随时打断的过渡。不存在会移动的点击目标：
    // hover / 按下反馈只改变填充、描边和文字，不改变输入矩形。
    static Color Mix(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        return new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t,
            a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);
    }
    /// <summary>自测入口：关闭 UI 动画，让单帧截图拿到的是最终布局，而不是入场过程中的半透明帧。</summary>
    public void SetUiAnimationsForTest(bool enabled) => preferences.UiAnimations = enabled;
}
