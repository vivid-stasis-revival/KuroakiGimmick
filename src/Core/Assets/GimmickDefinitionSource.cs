namespace KuroakiGimmick.Core;

/// <summary>
/// 对象定义的内容和相对资源根目录。Location 是诊断标识，不保证是磁盘路径；
/// 内嵌定义使用 embedded:// 标识，图片仍按 ResourceRoot 或曲包声明读取。
/// </summary>
/// <param name="Location">定义的定位路径。</param>
/// <param name="ResourceRoot">关联资源的根目录。</param>
/// <param name="Json">定义的 JSON 文本。</param>
/// <param name="Origin">定义来源标识。</param>
public sealed record GimmickDefinitionSource(string Location, string ResourceRoot, string Json, string Origin);
