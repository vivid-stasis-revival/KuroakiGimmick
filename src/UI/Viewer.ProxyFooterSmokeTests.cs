using KuroakiGimmick.Core;

namespace KuroakiGimmick.UI;

public sealed partial class Viewer
{
    /// <summary>用真实 GPU 读回验证灰色轨道底部跟随 proxy，且源位置不残留一份底部背景。</summary>
    void SmokeProxyFooter()
    {
        byte[] Frame(string objectName, string mods, bool proxy = true)
        {
            string vsm = $"!obj:{objectName}\n!proxies:{(proxy ? 1 : 0)}\n" +
                "0,0,linear,0,0,uialpha,-1\n0,0,linear,0,0,particle_alpha,-1\n" + mods;
            var session = new Session(new ViewerProject { RoomPreset = "none", Profile = "core", GameUiEnabled = false, RenderWidth = 320 }, editedVsm: vsm);
            Renderer.Render(session, 1, notes: true, effects: false);
            return Canvas.Read(Renderer.Final);
        }
        bool Gray(byte[] pixels, int x, int y)
        {
            int i = (y * 320 + x) * 4;
            return pixels[i] is > 40 and < 120 && pixels[i] == pixels[i + 1] && pixels[i] == pixels[i + 2];
        }
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        foreach (string obj in new[] { "obj_base_gimmick", "obj_custom_gimmick" })
        {
            var plain = Frame(obj, "", proxy: false);
            Check(Gray(plain, 160, 172), obj + ": non-proxy grey background lost its bottom");
            var shifted = Frame(obj, "0,0,linear,1,1,pra,0\n0,0,linear,-70,-70,prx,0\n");
            Check(Gray(shifted, 90, 172), obj + ": grey bottom did not follow horizontal proxy motion");
            Check(!Gray(shifted, 160, 172), obj + ": grey bottom remained at its original position");
            var raised = Frame(obj, "0,0,linear,1,1,pra,0\n0,0,linear,-30,-30,pry,0\n");
            Check(Gray(raised, 160, 142) && !Gray(raised, 160, 172), obj + ": vertical proxy motion detached the bottom");
            var scaled = Frame(obj, "0,0,linear,1,1,pra,0\n0,0,linear,0.5,0.5,przm,0\n");
            Check(Gray(scaled, 160, 127) && !Gray(scaled, 160, 172), obj + ": scaled proxy detached the bottom");
            var hidden = Frame(obj, "0,0,linear,0,0,pra,0\n");
            Check(!Gray(hidden, 160, 172), obj + ": invisible proxy left a fixed grey bottom");
            var cropped = Frame(obj, "0,0,linear,1,1,pra,0\n0,0,linear,0.2,0.2,prct,0\n");
            Check(!Gray(cropped, 160, 172), obj + ": authored proxy crop was ignored");
        }
        var rotated = Frame("obj_custom_gimmick", "0,0,linear,1,1,pra,0\n0,0,linear,30,30,prrz,0\n");
        Check(Gray(rotated, 115, 160) && !Gray(rotated, 160, 172), "Rotation detached the grey track bottom");
        Console.WriteLine("PASS proxy footer pixels: complete non-proxy background, translation, scale, rotation, alpha and authored cropping");
    }
}
