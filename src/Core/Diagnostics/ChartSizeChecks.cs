using KuroakiGimmick.Core.Editing;

namespace KuroakiGimmick.Core;

internal static class ChartSizeChecks
{
    internal static int Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "kg-size-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string? previousEngine = Environment.GetEnvironmentVariable("KG_RUST_ENGINE");
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
        void Reject(Action action)
        {
            try { action(); } catch (Exception ex) when (ex is InvalidDataException or FormatException) { return; }
            throw new InvalidOperationException("A limited load was not rejected.");
        }
        try
        {
            Check(!new ViewerSettings().UnlimitedChartSize, "Unlimited loading must default to off.");
            var saved = AppJson.Deserialize<ViewerSettings>(AppJson.Serialize(new ViewerSettings { UnlimitedChartSize = true }, ViewerProject.Json), ViewerProject.Json)!;
            Check(saved.UnlimitedChartSize, "Unlimited setting did not round trip.");
            string text = Path.Combine(root, "resource.txt"); File.WriteAllText(text, "12345678");
            string chart = Path.Combine(root, "large.vsc");
            // A valid chart just over 128 MiB. Comment padding avoids manufacturing millions of notes or an OOM test.
            byte[] comment = System.Text.Encoding.ASCII.GetBytes("//" + new string(' ', 65533) + "\n");
            using (var output = File.Create(chart))
            {
                for (int i = 0; i < 2049; i++) output.Write(comment);
                output.Write("1000,0,1\n"u8);
            }
            foreach (string engine in new[] { "off", "required" })
            {
                Environment.SetEnvironmentVariable("KG_RUST_ENGINE", engine);
                using (ChartLoadLimits.Override(false))
                {
                    // Original loader confirms the guard runs before parsing, independently of native fallback diagnostics.
                    Reject(() => VscReader.LoadOriginal(chart));
                    Reject(() => VsmBeatRange.Parse("0:1000000:1", double.Parse));
                    Reject(() => ResourceFiles.ReadText(text, 4));
                    Reject(() => AudioData.ValidateSampleCount(AudioData.MaxDecodedBytes / 4 + 1));
                }
                using (ChartLoadLimits.Override(true))
                {
#if KG_MOBILE || KG_SWITCH
                    Check(!ChartLoadLimits.Unlimited, "Mobile/Switch must never disable limits.");
#else
                    Check(!ChartLoadLimits.Exceeds(long.MaxValue, 1), "An artificial size limit remained active.");
                    Check(VscReader.Load(chart).Notes.Count == 1, "Chart over 128 MiB failed to load.");
                    Check(VsmBeatRange.Parse("0:1000000:1", double.Parse).Count == 1_000_001, "Event expansion is still limited.");
                    Check(ResourceFiles.ReadText(text, 4) == "12345678", "Resource size limit remained active.");
                    Check(AudioData.ValidateSampleCount(AudioData.MaxDecodedBytes / 4 + 1) > 0, "PCM size limit remained active.");
                    Check(VsmDocument.FromText("0:1000000:1,0,linear,0,1,prx,0\n").Clips.Single().RepeatCount == 1_000_001, "Editor still capped repeat count.");
                    Reject(() => VsmBeatRange.Parse("0:5:0", double.Parse)); // Structural validation remains mandatory.
                    using (ChartLoadLimits.Override(false)) Check(ChartLoadLimits.Exceeds(129, 128), "Nested scope did not restore protection.");
                    Check(ChartLoadLimits.Unlimited, "Nested scope leaked its policy.");
#endif
                }
            }
            using (ChartLoadLimits.Override(false)) Reject(() => VscReader.LoadOriginal(chart));
#if !KG_MOBILE && !KG_SWITCH
            using (ChartLoadLimits.Override(true)) OptimizationAudioBridge.SelfTest();
#endif
            Console.WriteLine("PASS chart size: default off, persistence, >128 MiB managed/native loads, resource/PCM/event limits, structural validation, scoped policy and re-enable protection");
            return 0;
        }
        finally { Environment.SetEnvironmentVariable("KG_RUST_ENGINE", previousEngine); Directory.Delete(root, true); }
    }
}
