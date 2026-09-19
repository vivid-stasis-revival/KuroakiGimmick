using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 可序列化的源级诊断。Error 表示不能正确加载/执行的组件；普通提示不等同于运行失败。
/// </summary>
public record Diagnostic(string Source, int Line, string Message, bool Error = false);

