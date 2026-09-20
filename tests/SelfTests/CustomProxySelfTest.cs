using KuroakiGimmick.Graphics;

namespace KuroakiGimmick.Core;

/// <summary>GPU regression for Custom's application-surface capture across VSP depth boundaries.</summary>
public static class CustomProxySelfTest
{
    public static void CheckGpu(Canvas canvas)
    {
        CheckProjection(canvas);
        string dir = Path.Combine(Environment.CurrentDirectory, ".custom-proxy-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // A one-pixel uncompressed TGA keeps the fixture independent of private song art.
            byte[] tga = new byte[22]; tga[2] = 2; tga[12] = tga[14] = 1; tga[16] = 32; tga[17] = 0x28;
            tga[20] = tga[21] = 255;
            File.WriteAllBytes(Path.Combine(dir, "red.tga"), tga);
            string chart = Path.Combine(dir, "ENCORE.vsc"), vsp = Path.Combine(dir, "ENCORE.vsp");
            File.WriteAllText(chart, "0,3,0,b:120\n8000,0,0\n");
            const string header = "!obj:obj_custom_gimmick\n!proxies:2\n";
            string Mod(string name, double value, int proxy = -1) => $"0,0,linear,{value},{value},{name},{proxy}\n";
            var project = new ViewerProject { Chart = chart, Images = vsp, GameUiEnabled = false };
            using var renderer = new SceneRenderer(canvas);
            byte[] Render(double layer, string mods, bool notes = false)
            {
                File.WriteAllText(vsp, $"#Layer\nart,{layer}\n#Image\nart:\nstatic,tile,red.tga,0,20,20\n");
                var session = new Session(project.Copy(), editedVsm: header + Mod("imgy_tile", 70) + mods);
                if (session.Images.Items.Count != 1) throw new Exception("Custom proxy fixture did not load its image.");
                renderer.Render(session, 1, notes, false);
                return canvas.Read(renderer.Final);
            }
            void Check(bool ok, string label)
            {
                if (!ok) throw new Exception("CUSTOM PROXY: " + label);
                Console.WriteLine("PASS " + label);
            }
            bool Red(byte[] data, int x, int y) => data[(y * 320 + x) * 4] > 240 && data[(y * 320 + x) * 4 + 1] < 10;
            bool Black(byte[] data, int x, int y) => data.Skip((y * 320 + x) * 4).Take(3).All(v => v == 0);
            string move = Mod("pra", 1, 0) + Mod("prx", 60, 0);
            byte[]? baseline = null;
            foreach (double layer in new[] { -15999, -301, -201, -200, -100, 0, 350, 999, 1000, 15999 })
            {
                var pixels = Render(layer, move);
                Check(Red(pixels, 220, 70) && Black(pixels, 160, 70), "image moves once without a stationary copy at priority " + layer);
                Check(baseline == null || baseline.SequenceEqual(pixels), "proxy capture is independent of the VSP priority boundary " + layer);
                baseline = pixels;
            }
            string crop = Mod("pra", 1, 0) + Mod("prcl", 20, 0) + Mod("prcr", 60, 0) + Mod("prx", 120, 0)
                + Mod("imgx_tile", 40) + Mod("hom", 1);
            var cropped = Render(-500, crop);
            Check(Red(cropped, 160, 70) && Black(cropped, 40, 70), "a proxy can sample low-layer side art while hom hides the fixed original");
            var rotated = Render(-500, Mod("pra", 1, 0) + Mod("prrz", 90, 0) + Mod("imgx_tile", 180) + Mod("imgy_tile", 82));
            Check(Red(rotated, 160, 102) && Black(rotated, 160, 62), "Custom proxy rotation follows the source MatrixRotateZ convention");
            // Compare a moved full crop to an independently rendered untransformed application surface.
            string fullCrop = move + Mod("prcl", 0, 0) + Mod("prcr", 320, 0) + Mod("prct", 0, 0);
            var raw = Render(-500, Mod("imgx_tile", 40), true);
            var shifted = Render(-500, fullCrop + Mod("imgx_tile", 40), true);
            bool equal = true;
            for (int y = 5; y < 140; y++)
                for (int x = 180; x < 230; x++)
                    for (int c = 0; c < 4; c++)
                        equal &= raw[(y * 320 + x - 60) * 4 + c] == shifted[(y * 320 + x) * 4 + c];
            Check(equal, "track pixels follow proxy movement with a low-priority image present");
            var noProxy = Render(-500, Mod("imgx_tile", 40));
            Check(Red(noProxy, 40, 70), "a Custom scene without proxy events keeps its ordinary image position");
            var again = Render(-500, crop);
            Check(again.SequenceEqual(cropped), "switching proxy configurations and seeking back leaves no stale pixels");
            var huge = Render(-500, Mod("pra", 1, 0) + Mod("przm", 1000, 0) + Mod("imgy_tile", 82));
            Check(Red(huge, 100, 40) && Red(huge, 160, 90) && Red(huge, 220, 130), "1000x Custom proxy remains visible and covers the destination");
            // The backmost proxy magnifies an empty patch to cover the fixed source strips.
            string backdrop = move + Mod("pra", 1, 1) + Mod("przm", 1000, 1)
                + Mod("prx", 145000, 1) + Mod("pry", 75000, 1) + Mod("prcl", 320, 1) + Mod("prct", 0, 1);
            var masked = Render(-500, backdrop + Mod("imgx_tile", 40));
            Check(Black(masked, 40, 70), "a magnified blank source patch hides stationary side art");
            var perspective = Render(-500, Mod("pra", 1, 0) + Mod("imgx_tile", 180)
                + Mod("imgy_tile", 102) + Mod("prtrX", .01, 0) + Mod("prtrY", .01, 0));
            Check(Red(perspective, 174, 96) && Black(perspective, 188, 110), "VSM perspective parameters reach the scene shader and change projected image position");
            var reversed = Render(-500, Mod("pra", 1, 0) + Mod("imgx_tile", 180)
                + Mod("imgy_tile", 102) + Mod("prtrX", .01, 0) + Mod("prtrY", .01, 0)
                + Mod("prcl", 320, 0) + Mod("prct", 0, 0));
            Check(Red(reversed, 174, 96), "perspective accepts reversed source crop endpoints used by Custom charts");
            Check(Render(-500, crop).SequenceEqual(cropped), "perspective uniforms cannot leak into a subsequent affine frame");
        }
        finally { Directory.Delete(dir, true); }
    }

    static void CheckProjection(Canvas canvas)
    {
        ShaderCompiler.ValidateHlsl(SceneRenderer.ProxyProjectionVertexSource, true);
        using var shader = new Shader(canvas.Gpu, SceneRenderer.ProxyProjectionVertexSource,
            "#version 330 core\nin vec2 v_vTexcoord;in vec4 v_vColour;uniform sampler2D gm_BaseTexture;out vec4 fragColor;void main(){fragColor=texture(gm_BaseTexture,v_vTexcoord)*v_vColour;}");
        byte[] grid = new byte[320 * 180 * 4];
        for (int y = 0; y < 180; y++)
            for (int x = 0; x < 320; x++)
            {
                int i = (y * 320 + x) * 4;
                grid[i] = (byte)Math.Min(255, x); grid[i + 1] = (byte)y; grid[i + 2] = 200; grid[i + 3] = 255;
            }
        using var texture = new Texture(canvas.Gpu, 320, 180, grid);
        using var target = new Target(canvas.Gpu, 320, 180);
        const double sx = .8, sy = .9, kx = -.3, ky = .15;
        double cosine = Math.Cos(.35), sine = Math.Sin(.35);
        foreach (var (tx, ty) in new[] { (0.0, 0.0), (.003, 0.0), (0.0, -.002), (.003, -.002), (-.004, .003), (.04, .02) })
        {
            canvas.Begin(target, 320, 180, 320, 180, Color.Hex(0));
            shader.Vec2("proxyOrigin", 160, 82); shader.Vec2("proxyScale", sx, sy);
            shader.Vec2("proxyRotation", cosine, sine); shader.Vec2("proxyTrapezoid", tx, ty);
            shader.Vec2("proxySkew", kx, ky); shader.Vec2("proxyTranslation", 150, 90);
            canvas.Quad(texture, new(80, 10, 160, 150), Color.White, new(.25f, 10f / 180, .5f, 150f / 180), shader: shader);
            var pixels = canvas.Read(target);
            int checkedPixels = 0;
            // Independent inverse mapping at destination pixel centers. Interior pixels must
            // recover the source coordinate gradient, including across the two-triangle seam.
            for (int y = 0; y < 180; y++)
                for (int x = 0; x < 320; x++)
                {
                    double qx = x + .5 - 150, qy = y + .5 - 90, det = 1 - kx * ky;
                    double u = (qx - kx * qy) / det, v = (qy - ky * qx) / det;
                    double reciprocalW = 1 - tx * u - ty * v;
                    if (reciprocalW <= .01) continue;
                    u /= reciprocalW; v /= reciprocalW;
                    double sourceX = (u * cosine + v * sine) / sx + 160;
                    double sourceY = (-u * sine + v * cosine) / sy + 82;
                    if (sourceX < 82 || sourceX > 238 || sourceY < 12 || sourceY > 158) continue;
                    int i = (y * 320 + x) * 4;
                    if (Math.Abs(pixels[i] - Math.Floor(sourceX)) > 1 || Math.Abs(pixels[i + 1] - Math.Floor(sourceY)) > 1 || pixels[i + 2] != 200)
                        throw new Exception($"Perspective UV mismatch at {x},{y} for {tx},{ty}: {pixels[i]},{pixels[i + 1]} vs {sourceX},{sourceY}.");
                    checkedPixels++;
                }
            if (checkedPixels < 500) throw new Exception("Perspective reference did not cover enough pixels.");
            Console.WriteLine($"PASS homogeneous projection {tx},{ty}: {checkedPixels} inverse-mapped pixels; HLSL translation");
        }
    }
}
