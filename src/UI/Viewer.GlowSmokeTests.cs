// 通过 GPU 像素检查辉光累积、强度和迭代次数。

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
        void Parameter(string name, double value) => layer.Parameters[name] = AppJson.SerializeToElement(value);
        Parameter("g_GlowRadius", 141);
        Parameter("g_GlowGamma", 0);
        // 常量图像经过圆盘卷积核保持不变，从而隔离原版 bm_max 混合；五次低强度 pass 应累积而非只取最大值。
        foreach (int width in new[] { 320, 640 })
        {
            int height = width * 9 / 16;
            fx.Resize(width, height);
            source.Resize(width, height);
            output.Resize(width, height);
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
        // 原版半径以 surface 像素计。亮块的物理尺寸相同时，两种分辨率下的局部光晕应相同。
        Parameter("g_GlowQuality", 1);
        Parameter("g_GlowRadius", 12);
        byte[]? reference = null;
        foreach (int width in new[] { 320, 640 })
        {
            int height = width * 9 / 16;
            fx.Resize(width, height);
            source.Resize(width, height);
            output.Resize(width, height);
            Canvas.Begin(source, width, height, width, height, Color.Hex(0));
            Canvas.Fill(new(width / 2 - 4, height / 2 - 4, 8, 8), Color.White);
            fx.RunGlow(output, source.Texture, layer, 1);
            byte[] pixels = Canvas.Read(output);
            var patch = new byte[48 * 48];
            for (int y = 0; y < 48; y++)
                for (int x = 0; x < 48; x++)
                    patch[y * 48 + x] = pixels[((height / 2 - 24 + y) * width + width / 2 - 24 + x) * 4];

            if (patch[24 * 48 + 29] == 0 || patch[24 * 48 + 47] != 0)
                throw new InvalidOperationException("Glow must reach the neighboring background and leave distant black pixels black.");
            if (reference != null && !patch.SequenceEqual(reference))
                throw new InvalidOperationException("Glow radius changed in surface pixels when the render width doubled.");
            reference = patch;
        }
        Console.WriteLine("PASS #49 GPU glow: bm_max accumulation, intensity, local halo and surface-pixel radius at 320/640 widths");
    }
}
