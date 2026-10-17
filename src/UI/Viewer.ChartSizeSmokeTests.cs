#if !KG_MOBILE && !KG_SWITCH
using KuroakiGimmick.Core;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    void SmokeChartSizeWarningUi()
    {
        bool wasSettings = settings, oldUnlimited = preferences.UnlimitedChartSize, oldAnimations = preferences.UiAnimations;
        string oldSection = settingsSection, oldWorkflow = workflow, oldDisplayed = displayedWorkflow, oldLanguage = preferences.UiLanguage;
        using var limited = ChartLoadLimits.Override(false); // Rendering a status preview must not change the load policy.
        try
        {
            preferences.UiAnimations = false;
            settings = true; settingsSection = "editor";
            foreach (string language in new[] { UiLanguage.Chinese, UiLanguage.English })
            {
                SetUiLanguage(language, persist: false);
                preferences.UnlimitedChartSize = false;
                workflow = displayedWorkflow = "chart-size-warning";
                chartSizeRiskAcknowledged = false; chartSizeWarningScroll = 0;
                Draw(1440, 940);
                // Confirm occupies the bottom-right button. Unchecked clicks must do nothing.
                mouseX = 980; mouseY = 815; click = true; Draw(1440, 940);
                if (preferences.UnlimitedChartSize || workflow != "chart-size-warning")
                    throw new InvalidOperationException("Unchecked warning enabled unrestricted loading.");
                mouseX = 257; mouseY = 743; click = true; Draw(1440, 940);
                if (!chartSizeRiskAcknowledged) throw new InvalidOperationException("Warning checkbox was blocked by Settings.");
                if (Environment.GetEnvironmentVariable("KG_SIZE_WARNING_SCREENSHOT") is { } warningPath)
                    Canvas.SavePpm(UiTarget, warningPath + "-" + language + ".ppm");
                mouseX = 450; mouseY = 815; click = true; Draw(1440, 940);
                if (workflow != "" || preferences.UnlimitedChartSize)
                    throw new InvalidOperationException("Warning cancellation changed the saved setting.");
                preferences.UnlimitedChartSize = true; Draw(1440, 940);
                if (Environment.GetEnvironmentVariable("KG_SIZE_SETTINGS_SCREENSHOT") is { } settingsPath)
                    Canvas.SavePpm(UiTarget, settingsPath + "-" + language + ".ppm");
            }
            Console.WriteLine("PASS size warning UI: Chinese/English message boxes, unchecked confirmation blocked, checkbox interaction, cancel unchanged, enabled status layout");
        }
        finally
        {
            preferences.UnlimitedChartSize = oldUnlimited;
            settings = wasSettings; settingsSection = oldSection; workflow = oldWorkflow; displayedWorkflow = oldDisplayed;
            click = false; SetUiLanguage(oldLanguage, persist: false);
            Draw(1440, 940); // Remove the test overlay's hit-blocking fade before subsequent shortcut checks.
            preferences.UiAnimations = oldAnimations;
        }
    }
}
#endif
