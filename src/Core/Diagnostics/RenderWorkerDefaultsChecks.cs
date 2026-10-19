using KuroakiGimmick.Core;

namespace KuroakiGimmick;

internal static class RenderWorkerDefaultsChecks
{
    internal static int Run()
    {
        int maximum = Math.Max(1, Environment.ProcessorCount);
        if (ExportOptions.MaxRenderWorkers != maximum)
            throw new InvalidOperationException("Render worker upper bound does not match available logical CPU cores.");

        var defaults = new ViewerSettings();
        if (defaults.VideoRenderWorkers != maximum || ViewerSettings.ValidRenderWorkers(defaults.VideoRenderWorkers) != maximum)
            throw new InvalidOperationException("Fresh preferences must enable the maximum number of render workers.");
        if (ViewerSettings.ValidRenderWorkers(int.MaxValue) != maximum || ViewerSettings.ValidRenderWorkers(0) != 1)
            throw new InvalidOperationException("Render worker bounds check failed.");

        string directory = Path.Combine(Path.GetTempPath(), "kg-render-default-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        try
        {
            defaults.Persist(path);
            if (ViewerSettings.Load(path).VideoRenderWorkers != maximum)
                throw new InvalidOperationException("Default render worker count was not preserved across save/load.");

            // Do not overwrite a user's prior explicit choice when upgrading the application.
            defaults.VideoRenderWorkers = 1;
            defaults.Persist(path);
            if (ViewerSettings.Load(path).VideoRenderWorkers != 1)
                throw new InvalidOperationException("Existing explicit single-thread preference was overwritten.");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        Console.WriteLine($"PASS max-core render defaults: {maximum} available logical cores; saved manual override preserved.");
        return 0;
    }
}
