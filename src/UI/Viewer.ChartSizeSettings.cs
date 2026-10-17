#if !KG_MOBILE && !KG_SWITCH
using KuroakiGimmick.Core;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    bool chartSizeRiskAcknowledged;
    int chartSizeWarningScroll;
    ChartSizeWarningText SizeWarning => ChartSizeWarningText.Get(L.Language == UiLanguage.Chinese);

    void SetUnlimitedChartSize(bool enabled)
    {
        preferences.UnlimitedChartSize = enabled;
        SaveSettings();
    }

    void RequestUnlimitedChartSize()
    {
        if (Busy || editorBuild != null) return;
        if (preferences.UnlimitedChartSize) { SetUnlimitedChartSize(false); return; }
        transport.SetPlaying(false); click = held = false;
        Canvas.Flush();
        try
        {
            if (NativeRiskMessageBox.TryShow(host.Window, SizeWarning, out bool accepted))
            {
                if (accepted) SetUnlimitedChartSize(true);
                return;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        { Console.Error.WriteLine("Native warning dialog: " + ex.Message); }
        // Linux and desktop systems without a native checkbox dialog use the equivalent modal message box.
        chartSizeRiskAcknowledged = false; chartSizeWarningScroll = 0;
        workflow = displayedWorkflow = "chart-size-warning";
    }

    void DrawChartSizeOptions(Rect r)
    {
        var text = SizeWarning;
        float x = r.X + 14, y = r.Y + 14;
        foreach (string line in EditorHelpLayout.Wrap(text.Setting, r.W - 170, value => fonts.Measure(value, 15)))
        { Text(line, x, y, 15, white); y += 22; }
        if (Button(L.Get(preferences.UnlimitedChartSize ? "ON" : "OFF"), new(r.X + r.W - 126, r.Y + 12, 110, 32),
            active: preferences.UnlimitedChartSize, enabled: !Busy && editorBuild == null, key: "settings-unlimited-chart"))
            RequestUnlimitedChartSize();
        y += 20;
        Text(text.DefaultLimit, x, y, 13, muted); y += 30;
        foreach (string line in EditorHelpLayout.Wrap(text.Description, r.W - 28, value => fonts.Measure(value, 13)))
        { Text(line, x, y, 13, white); y += 22; }
        if (preferences.UnlimitedChartSize)
        {
            y += 25; Text(ChartSizeWarningText.Disabled, x, y, 16, soft, true); y += 30;
            foreach (string line in EditorHelpLayout.Wrap(ChartSizeWarningText.DisabledHint, r.W - 28, value => fonts.Measure(value, 13)))
            { Text(line, x, y, 13, muted); y += 20; }
        }
    }

    void DrawChartSizeWarning(int w, int h)
    {
        bool previousSettingsInput = settingsInput;
        settingsInput = true; // This message box intentionally sits over the still-open Settings page.
        try
        {
        var text = SizeWarning;
        float width = Math.Min(1000, w - 48), height = Math.Min(760, h - 48);
        var box = new Rect((w - width) / 2, (h - height) / 2, width, height);
        Canvas.Fill(box, panel); Canvas.Border(box, soft);
        float x = box.X + 24, y = box.Y + 20;
        foreach (string line in EditorHelpLayout.Wrap(text.Title, box.W - 48, value => fonts.Measure(value, 18)))
        { Text(line, x, y, 18, soft); y += 24; }
        var body = new Rect(x, y + 12, box.W - 48, box.Y + box.H - 146 - y);
        var lines = text.Body.Split('\n').SelectMany(line => line.Length == 0 ? new[] { "" }
            : EditorHelpLayout.Wrap(line, body.W - 10, value => fonts.Measure(value, 13))).ToArray();
        int visible = Math.Max(1, (int)(body.H / 20));
        chartSizeWarningScroll = Math.Clamp(chartSizeWarningScroll, 0, Math.Max(0, lines.Length - visible));
        Canvas.Clip(body);
        for (int i = chartSizeWarningScroll; i < Math.Min(lines.Length, chartSizeWarningScroll + visible); i++)
            Text(lines[i], x, body.Y + (i - chartSizeWarningScroll) * 20, 13, white);
        Canvas.Clip(null);
        var check = new Rect(x, box.Y + box.H - 119, 26, 26);
        if (WorkflowButton("", check, active: chartSizeRiskAcknowledged))
            chartSizeRiskAcknowledged = !chartSizeRiskAcknowledged;
        if (chartSizeRiskAcknowledged)
        {
            Canvas.Line(check.X + 5, check.Y + 13, check.X + 11, check.Y + 20, 2, white);
            Canvas.Line(check.X + 11, check.Y + 20, check.X + 22, check.Y + 6, 2, white);
        }
        float textY = check.Y + 3;
        foreach (string line in EditorHelpLayout.Wrap(text.Acknowledgement, box.W - 88, value => fonts.Measure(value, 12)))
        { Text(line, x + 36, textY, 12, white); textY += 18; }
        float half = (box.W - 60) / 2;
        if (WorkflowButton(text.Cancel, new(x, box.Y + box.H - 52, half, 32))) workflow = "";
        if (WorkflowButton(text.Confirm, new(x + half + 12, box.Y + box.H - 52, half, 32), primary: true, enabled: chartSizeRiskAcknowledged))
        {
            if (chartSizeRiskAcknowledged) SetUnlimitedChartSize(true);
            workflow = "";
        }
        }
        finally { settingsInput = previousSettingsInput; }
    }
}
#endif
