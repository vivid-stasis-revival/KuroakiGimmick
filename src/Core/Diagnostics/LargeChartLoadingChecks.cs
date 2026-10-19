using System.Text;
using System.Text.RegularExpressions;
using KuroakiGimmick.Core.Editing;

namespace KuroakiGimmick.Core;

/// <summary>Headless source/encoding regressions for bounded-memory chart input.</summary>
internal static class LargeChartLoadingChecks
{
    internal static int Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "kg-loading-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string? previous = Environment.GetEnvironmentVariable("KG_RUST_ENGINE");
        try
        {
            using var limits = ChartLoadLimits.Override(true);
            void Check(bool condition, string reason)
            { if (!condition) throw new InvalidOperationException(reason); }

            // The regex is a test oracle for the previous parser, never a load path.
            foreach (string source in new[] { "", "\r", "\n\r\n", "a\rb\nc\r\nd", "a\n", "a\r\n\r\n",
                new string('x', 65_535) + "\r\nnext\rfinal", new string('x', 65_536) + "\r\nlast" })
            {
                using var reader = new StringReader(source);
                var actual = SourceLines.Read(reader).ToArray();
                var expected = Regex.Matches(source, @"([^\r\n]*)(\r\n|\r|\n|$)")
                    .Where(match => match.Length != 0)
                    .Select(match => new SourceLines.Line(match.Groups[1].Value, match.Groups[2].Value)).ToArray();
                Check(actual.SequenceEqual(expected), "Source line delimiter/buffer-boundary mismatch.");
            }

            const string text = "!obj:obj_custom_gimmick\r\n!proxies:2\r!title:雪🎵\n\n0,0,linear,0,1,prx,0\r\n1:3:1,0,linear,1,2,pry,1\rmpf\n0,4,callback";
            Encoding[] encodings = [new UTF8Encoding(false), new UTF8Encoding(true),
                new UnicodeEncoding(false, true), new UnicodeEncoding(true, true),
                new UTF32Encoding(false, true), new UTF32Encoding(true, true)];
            foreach (Encoding encoding in encodings)
            {
                string path = Path.Combine(root, encoding.CodePage + "-" + encoding.GetPreamble().Length + ".vsm");
                byte[] bytes = encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
                File.WriteAllBytes(path, bytes);
                var document = VsmDocument.Load(path);
                Check(document.Text == text && document.Bytes().SequenceEqual(bytes), "Editor BOM/encoding/newline roundtrip failed.");
                Check(document.Clips.Count() == 2 && document.NewLine == "\r\n", "Editor clip or preferred newline changed.");
                var reference = new Chart();
                VsmReader.ReplaceModsTextOriginal(reference, text, path);
                foreach (string mode in new[] { "off", "verify", "required" })
                {
                    Environment.SetEnvironmentVariable("KG_RUST_ENGINE", mode);
                    var actual = new Chart();
                    VsmReader.ReplaceMods(actual, path);
                    Check(actual.Mods.SequenceEqual(reference.Mods) && actual.PerFrame.SequenceEqual(reference.PerFrame) &&
                        actual.Metadata.SequenceEqual(reference.Metadata) && actual.Diagnostics.SequenceEqual(reference.Diagnostics),
                        "File VSM parity failed for " + encoding.EncodingName + " / " + mode);
                }
            }

            // Exercise file-backed input rather than only the small-input fast
            // path. UTF-8 LF/CRLF maps directly; other encodings and bare CR are
            // normalized through a bounded private UTF-8 temporary file.
            foreach (Encoding encoding in encodings)
            {
                string path = Path.Combine(root, "mapped-" + encoding.CodePage + "-" + encoding.GetPreamble().Length + ".vsm");
                using (var writer = new StreamWriter(path, false, encoding))
                {
                    string padding = new string(' ', 8192) + "\r\n";
                    for (int i = 0; i < 513; i++) writer.Write(padding);
                    writer.Write(text.Replace("\r\n", "\n").Replace('\r', '\n'));
                }
                var reference = new Chart(); VsmReader.ReplaceModsOriginal(reference, path);
                Environment.SetEnvironmentVariable("KG_RUST_ENGINE", "verify");
                var actual = new Chart(); VsmReader.ReplaceMods(actual, path);
                Check(actual.Mods.SequenceEqual(reference.Mods) && actual.Metadata.SequenceEqual(reference.Metadata),
                    "Mapped VSM input parity failed for " + encoding.EncodingName);
            }

            // Invalid UTF-8 is replaced in runtime input, but editor imports must
            // continue to reject it rather than silently damage a saved document.
            string invalid = Path.Combine(root, "invalid.vsm");
            File.WriteAllBytes(invalid, "!title:"u8.ToArray().Concat(new byte[] { 0xff }).Concat("\n0,0,linear,0,1,prx,-1\n"u8.ToArray()).ToArray());
            bool rejected = false;
            try { VsmDocument.Load(invalid); } catch (DecoderFallbackException) { rejected = true; }
            Check(rejected, "Editor accepted invalid UTF-8.");
            Environment.SetEnvironmentVariable("KG_RUST_ENGINE", "verify");
            var replacement = new Chart(); VsmReader.ReplaceMods(replacement, invalid);
            Check(replacement.Metadata["title"] == "\ufffd" && replacement.Mods.Count == 1, "Runtime UTF-8 replacement semantics changed.");

            // A decoded source over the native text threshold uses bounded UTF-8
            // transcoding, with CRLF and surrogate pairs straddling buffer edges.
            string large = new string(' ', 4 * 1024 * 1024 + 65_533) + "\r\n!title:🎵\r0,0,linear,0,1,prx,-1";
            var fromText = new Chart(); VsmReader.ReplaceModsText(fromText, large, "large.vsm");
            Check(fromText.Mods.Count == 1 && fromText.Mods[0].SourceLine == 3 && fromText.Metadata["title"] == "🎵", "Large native text normalization failed.");

            string bareCr = Path.Combine(root, "bare-cr.vsm");
            File.WriteAllText(bareCr, large, new UTF8Encoding(false));
            var fromFile = new Chart(); VsmReader.ReplaceMods(fromFile, bareCr);
            Check(fromFile.Mods.SequenceEqual(fromText.Mods) && fromFile.Metadata.SequenceEqual(fromText.Metadata), "Mapped bare-CR normalization failed.");

            foreach (bool valid in new[] { true, false })
            {
                byte[] splitUtf8 = new byte[4 * 1024 * 1024 + 128];
                Array.Fill(splitUtf8, (byte)' ');
                "!title:"u8.CopyTo(splitUtf8);
                int boundary = 1024 * 1024;
                splitUtf8[boundary - 1] = 0xc2;
                splitUtf8[boundary] = valid ? (byte)0xa0 : (byte)'X';
                splitUtf8[boundary + 1] = (byte)'Y';
                splitUtf8[boundary + 2] = (byte)'\n';
                byte[] final = "\n0,0,linear,0,1,prx,-1\n"u8.ToArray();
                final.CopyTo(splitUtf8, splitUtf8.Length - final.Length);
                string path = Path.Combine(root, valid ? "valid-split-utf8.vsm" : "invalid-split-utf8.vsm");
                File.WriteAllBytes(path, splitUtf8);
                Environment.SetEnvironmentVariable("KG_RUST_ENGINE", "required");
                var actual = new Chart(); VsmReader.ReplaceMods(actual, path);
                Check(actual.Mods.Count == 1 && actual.Metadata["title"] == (valid ? "Y" : "\ufffdXY"),
                    "Mapped UTF-8 decoder lost state at a chunk boundary.");
            }

            Environment.SetEnvironmentVariable("KG_RUST_ENGINE", "off");
            Check(!RustEngineBridge.TryApplyVsmFile(new Chart(), Path.Combine(root, "missing.vsm")), "Disabled native input performed I/O.");
            Console.WriteLine("PASS large chart loading: bounded source lines, exact editor BOM/newlines, UTF-8/16/32 off/verify/required parity, invalid-byte semantics and large native text.");
            return 0;
        }
        finally
        {
            Environment.SetEnvironmentVariable("KG_RUST_ENGINE", previous);
            Directory.Delete(root, true);
        }
    }
}
