using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using KuroakiGimmick.Native;

namespace KuroakiGimmick.Graphics;

/// <summary>读回与 PPM 截图，只处理像素数据，不隐式修改渲染状态。</summary>
public sealed partial class Canvas : IDisposable
{
    /// <summary>读回前先 Flush 掉挂起的几何；返回左上角为原点、紧密排列的 RGBA8。</summary>
    public byte[] Read(Target target)
    {
        Flush();
        return Gpu.Read(target.Texture);
    }

    /// <summary>写出二进制 P6 PPM：只保留 RGB，丢弃每像素的第 4 个字节；必要时先建目录。</summary>
    public void SavePpm(Target target, string path)
    {
        var bytes = Read(target);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var f = File.Create(path);
        f.Write(Encoding.ASCII.GetBytes($"P6\n{target.Texture.Width} {target.Texture.Height}\n255\n"));
        for (int i = 0; i < bytes.Length; i += 4)
        {
            f.Write(bytes, i, 3);
        }
    }
}

