// 读回与 PPM 截图，只处理像素数据，不隐式修改渲染状态。

using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using KuroakiGimmick.Native;
using StbImageWriteSharp;

namespace KuroakiGimmick.Graphics;

public sealed partial class Canvas : IDisposable
{
    /// <summary>为指定尺寸分配可复用的异步像素读回槽位。</summary>
    /// <param name="target">提供读回尺寸的目标。</param>
    /// <returns>由调用方管理的可复用读回槽位。</returns>
    public GpuReadback CreateReadback(Target target) => new(Gpu, target.Texture.Width, target.Texture.Height, Gpu.TextureRowPitch(target.Texture.Width));

    /// <summary>启动目标图像到读回槽位的传输。</summary>
    /// <param name="slot">空闲且尺寸匹配的读回槽位。</param>
    /// <param name="target">要下载的离屏目标。</param>
    public void BeginReadback(GpuReadback slot, Target target)
    {
        Flush();
        slot.Begin(target.Texture);
    }

    /// <summary>读回前先 Flush 掉挂起的几何；返回左上角为原点、紧密排列的 RGBA8。</summary>
    /// <param name="target">需要读取的离屏目标。</param>
    /// <returns>按行紧密排列的 RGBA8 像素。</returns>
    public byte[] Read(Target target)
    {
        Flush();
        return Gpu.Read(target.Texture);
    }

    /// <summary>
    /// 编码成 PNG 字节。读回的排列（左上原点、紧密 RGBA8）正好是编码器要的格式，中间不做任何重排；
    /// 返回字节而不是直接落盘，是因为卡片既要能保存也要能进剪贴板。
    /// </summary>
    /// <param name="target">要编码的目标。</param>
    /// <returns>完整的 PNG 文件字节。</returns>
    public byte[] EncodePng(Target target)
    {
        var bytes = Read(target);
        using var stream = new MemoryStream();
        new ImageWriter().WritePng(bytes, target.Texture.Width, target.Texture.Height,
            ColorComponents.RedGreenBlueAlpha, stream);
        return stream.ToArray();
    }

    /// <summary>写出 PNG；必要时先建目录。</summary>
    /// <param name="target">要保存的离屏目标。</param>
    /// <param name="path">PNG 输出路径。</param>
    public void SavePng(Target target, string path)
    {
        var png = EncodePng(target);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, png);
    }

    /// <summary>写出二进制 P6 PPM：只保留 RGB，丢弃每像素的第 4 个字节；必要时先建目录。</summary>
    /// <param name="target">要保存的离屏目标。</param>
    /// <param name="path">P6 PPM 输出路径。</param>
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

