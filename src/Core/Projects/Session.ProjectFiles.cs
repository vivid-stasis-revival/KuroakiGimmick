using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 会话装配根：按依赖顺序加载谱面、对象定义和资源，再构建同一条可随机访问的时间轴。CPU 资源不持有 GPU 对象。
/// </summary>
public sealed partial class Session
{
    /// <summary>打开谱面或项目；项目中的相对路径以项目文件所在目录解析，而非当前工作目录。</summary>
    public static Session Load(string path)
    {
        var project = ReadProject(path, out string? projectPath);
        return new(project, projectPath);
    }

    /// <summary>
    /// 只做路径解析，不加载音频/图片、不构建时间轴。
    /// 命令行可先覆盖资源和对象定义，避免先用错误配置完整加载一次再重来。
    /// </summary>
    internal static ViewerProject ReadProject(string path, out string? projectPath)
    {
        projectPath = null;
        path = Path.GetFullPath(path);
        if (path.EndsWith(".sgv.json", StringComparison.OrdinalIgnoreCase))
        {
            var p = AppJson.Deserialize<ViewerProject>(File.ReadAllText(path),
                ViewerProject.Json) ?? throw new InvalidDataException("Empty project.");
            if (p.Version != 1)
            {
                throw new InvalidDataException($"Unsupported project version {p.Version}.");
            }
            // 基准目录是工程文件所在目录，不是当前工作目录：同一个工程从任何位置启动都指向同一批素材。
            var dir = Path.GetDirectoryName(path)!;
            p.Chart = Resolve(dir, p.Chart);
            p.Gimmick = Resolve(dir, p.Gimmick);
            p.Audio = Resolve(dir, p.Audio);
            p.Images = Resolve(dir, p.Images);
            if (p.TextFiles != null) p.TextFiles = p.TextFiles.ToDictionary(x => x.Key, x => Resolve(dir, x.Value)!);
            p.WindowMotion = Resolve(dir, p.WindowMotion);
            p.Jacket = Resolve(dir, p.Jacket);
            p.FxProfile = Resolve(dir, p.FxProfile);
            p.GameUi = Resolve(dir, p.GameUi);
            p.GimmickAssets = Resolve(dir, p.GimmickAssets);
            p.GimmickDefinition = Resolve(dir, p.GimmickDefinition);
            projectPath = path;
            return p;
        }
        return SongFiles.Open(path);
    }

    /// <summary>按扩展名替换单项输入，再重建会话；其它已挂载资源保持不变。</summary>
    public Session Attach(string path)
    {
        var p = Project.Copy();
        path = Path.GetFullPath(path);
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".json" when path.EndsWith("_cgmk_config.json", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path) is "window_movement.json" or "cgmk_config.json":
                p.WindowMotion = path;
                break;
            case ".json" when Path.GetFileName(path) == "gimmick-object.json":
                p.GimmickDefinition = path;
                break;
            case ".json" when path.EndsWith(".gameui.json", StringComparison.OrdinalIgnoreCase):
                p.GameUi = path;
                break;
            case ".vsm":
                if (IsEmpty)
                {
                    return Load(path);
                }
                p.Gimmick = path;
                // 换了 VSM 就回到 auto：资源 profile 与 BPM 由新 gimmick 重新决定，不沿用上一份的固定选择。
                p.Profile = "auto";
                break;
            case ".vsp":
                p.Images = path;
                // 手动挂载的 VSP 可能来自任意目录，其中的图片路径改按工程基准解析，而不是 VSP 自身所在目录。
                p.ImagePathsRelativeToVsp = false;
                break;
            case ".jpg":
            case ".jpeg":
            case ".png":
                p.Jacket = path;
                break;
            case ".json" when path.EndsWith(".fx.json", StringComparison.OrdinalIgnoreCase) || System.IO.Path.GetFileName(path) == "gimmick-fx.json":
                p.FxProfile = path;
                break;
            case ".ogg":
            case ".wav":
            case ".mp3":
            case ".flac":
            case ".m4a":
                p.Audio = path;
                break;
            case ".vsb":
            case ".vsc":
                if (IsEmpty)
                {
                    return Load(path);
                }
                p.Chart = path;
                p.Notes = true;
                break;
            default:
                return Load(path);
        }
        return new(p, ProjectPath);
    }

    /// <summary>仅保存项目引用与设置，不复制或修改外部歌曲素材。</summary>
    public void Save(string path)
    {
        var p = Project.Copy();
        string dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        // 写回相对路径，工程连同素材整体搬目录后仍能打开；这里只改写引用，磁盘上的歌曲素材一个字节都不动。
        string? Rel(string? x) => x == null ? null : Path.GetRelativePath(dir, x);
        p.Chart = Rel(p.Chart);
        p.Gimmick = Rel(p.Gimmick);
        p.Audio = Rel(p.Audio);
        p.Images = Rel(p.Images);
        if (p.TextFiles != null) p.TextFiles = p.TextFiles.ToDictionary(x => x.Key, x => Rel(x.Value)!);
        p.WindowMotion = Rel(p.WindowMotion);
        p.Jacket = Rel(p.Jacket);
        p.FxProfile = Rel(p.FxProfile);
        p.GameUi = Rel(p.GameUi);
        p.GimmickAssets = Rel(p.GimmickAssets);
        p.GimmickDefinition = Rel(p.GimmickDefinition);
        File.WriteAllText(path, AppJson.Serialize(p, ViewerProject.Json));
    }
}
