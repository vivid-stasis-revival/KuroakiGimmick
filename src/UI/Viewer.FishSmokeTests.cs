// 通过 GPU 像素检查 fish 变形在不同呈现分辨率下的边缘。

using KuroakiGimmick.Core;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    void SmokeFish()
    {
        int previousWidth = Renderer.PresentationWidth;
        try
        {
            foreach (bool proxy in new[] { false, true })
                foreach (int sourceWidth in new[] { 320, 640 })
                {
                    string vsm = "!obj:obj_custom_gimmick\n!proxies:1\n"
                        + "0,0,linear,1,1,fish,-1\n"
                        + (proxy ? "0,0,linear,1,1,pra,0\n" : "");
                    var session = new Session(new ViewerProject
                    {
                        RoomPreset = "none",
                        Profile = "core",
                        GameUiEnabled = false,
                        RenderWidth = sourceWidth
                    }, editedVsm: vsm);
                    foreach (int width in new[] { 1280, 1920 })
                    {
                        Renderer.PresentationWidth = width;
                        Renderer.Render(session, 1, notes: false);
                        if (Renderer.Final.Texture.Width != width || session.Project.RenderWidth != sourceWidth)
                            throw new InvalidOperationException("Fish must use presentation resolution without changing source resolution.");
                        byte[] pixels = Canvas.Read(Renderer.Final);
                        int scale = width / sourceWidth, height = width * 9 / 16, differences = 0;
                        // 低分辨率变形后放大会让每块源像素区域内部完全相同；平滑变形的轨道边缘应按输出像素精度跨过这些区域。
                        for (int y = height / 8; y < height * 7 / 8; y++)
                            for (int x = width / 4; x < width * 3 / 4; x++)
                            {
                                int i = (y * width + x) * 4;
                                int origin = ((y / scale * scale) * width + x / scale * scale) * 4;
                                if (Math.Abs(pixels[i] - pixels[origin]) > 20) differences++;
                            }
                        if (differences < 100)
                            throw new InvalidOperationException($"Fish is still pixelated: source={sourceWidth}, output={width}, proxy={proxy}.");
                        Renderer.Render(session, 1, notes: false, effects: false);
                        if (Renderer.Final.Texture.Width != sourceWidth)
                            throw new InvalidOperationException("Disabling fish effects did not restore source resolution.");
                    }
                }
        }
        finally
        {
            Renderer.PresentationWidth = previousWidth;
        }
        Console.WriteLine("PASS #50 GPU fish: 320/640 sources, 1280/1920 output, subpixel curved edges, proxy/no proxy, effects on/off");
    }
}
