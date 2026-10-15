using System.Text.Json;
using KuroakiGimmick.Core;
using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    void SmokeGlow()
    {
        using var fx = new CustomEffects(Canvas);
        using var source = new Target(Canvas.Gpu, 320, 180);
        using var output = new Target(Canvas.Gpu, 320, 180);
        var layer = new GameFxProfile.Layer();
        void Parameter(string name, double value) => layer.Parameters[name] = JsonSerializer.SerializeToElement(value);
        Parameter("g_GlowRadius", 141); Parameter("g_GlowGamma", 0);
        // A constant image is unchanged by the disk kernel, isolating the original bm_max blend.
        // In particular, five faint passes must accumulate instead of taking their maximum.
        foreach (int width in new[] { 320, 640 })
        {
            int height = width * 9 / 16;
            fx.Resize(width, height); source.Resize(width, height); output.Resize(width, height);
            foreach (int quality in new[] { 1, 5 })
            foreach (double intensity in new[] { 0, .5, 1 })
            {
                Parameter("g_GlowQuality", quality);
                Canvas.Begin(source, width, height, width, height, Color.Hex(0x204080));
                fx.RunGlow(output, source.Texture, layer, intensity);
                byte[] actual = Canvas.Read(output);
                int[] channels = [32, 64, 128];
                for (int c = 0; c < 3; c++)
                {
                    double expected = channels[c] / 255.0;
                    double src = expected * Math.Floor(intensity * 255) / 255;
                    for (int pass = 0; pass < quality; pass++)
                        expected = Math.Round((src + expected * (1 - src)) * 255) / 255;
                    for (int i = c; i < actual.Length; i += 4)
                        if (Math.Abs(actual[i] - expected * 255) > 2)
                            throw new InvalidOperationException($"Glow bm_max mismatch: width={width}, quality={quality}, intensity={intensity}, channel={c}, expected={expected * 255}, actual={actual[i]}");
                }
            }
        }
        Console.WriteLine("PASS #49 GPU glow: GameMaker bm_max accumulation, 1/5 passes, zero/half/full intensity, 320/640 render widths");
    }
}
