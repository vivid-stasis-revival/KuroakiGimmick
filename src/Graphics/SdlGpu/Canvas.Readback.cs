using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using KuroakiGimmick.Native;
using StbImageWriteSharp;

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

    /// <summary>
    /// 编码成 PNG 字节。读回的排列（左上原点、紧密 RGBA8）正好是编码器要的格式，中间不做任何重排；
    /// 返回字节而不是直接落盘，是因为卡片既要能保存也要能进剪贴板。
    /// </summary>
    public byte[] EncodePng(Target target)
    {
        var bytes = Read(target);
        using var stream = new MemoryStream();
        new ImageWriter().WritePng(bytes, target.Texture.Width, target.Texture.Height,
            ColorComponents.RedGreenBlueAlpha, stream);
        return stream.ToArray();
    }

    /// <summary>写出 PNG；必要时先建目录。</summary>
    public void SavePng(Target target, string path)
    {
        var png = EncodePng(target);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, png);
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

