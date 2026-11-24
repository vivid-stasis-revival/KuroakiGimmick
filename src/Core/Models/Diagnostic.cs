using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 可序列化的源级诊断。Error 表示不能正确加载/执行的组件；普通提示不等同于运行失败。
/// </summary>
/// <param name="Source">产生诊断的源文件或组件。</param>
/// <param name="Line">诊断关联的源行号。</param>
/// <param name="Message">展示给调用方的诊断文本。</param>
/// <param name="Error">为 true 时表示错误。</param>
public record Diagnostic(string Source, int Line, string Message, bool Error = false);

