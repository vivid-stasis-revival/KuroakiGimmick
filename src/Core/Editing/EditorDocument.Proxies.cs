// 删除 proxy 并同步调整相关事件。

using System.Text.Json.Nodes;
using KuroakiGimmick.Core.Windows;

namespace KuroakiGimmick.Core.Editing;

public sealed partial class EditorDocument
{
    /// <summary>删除目标 proxy 并同步调整关联事件与编号。</summary>
    /// <param name="target">要删除的 proxy 编号。</param>
    public void DeleteProxy(int target) => Change("Delete proxy " + target, () =>
    {
        Vsm.DeleteProxy(target);
        if (Windows.Bindings is { } bindings)
        {
            int index = 0;
            foreach (var binding in bindings.OfType<JsonObject>().ToArray())
            {
                int originalIndex = index++;
                int proxy = WindowMotionConfig.Integer(binding, "proxy", originalIndex);
                if (proxy == target)
                {
                    bindings.Remove(binding);
                    continue;
                }
                // 移除较早的绑定会改变位置默认值，须先把现有默认值显式写出。
                binding["proxy"] = proxy > target ? proxy - 1 : proxy;
                if (binding["window"] == null) binding["window"] = 100 + originalIndex;
                if (binding["source"] == null) binding["source"] = 1000 + originalIndex;
            }
        }
        var order = new List<string>();
        foreach (string key in Project.EditorTrackOrder)
        {
            bool group = key.StartsWith("G:", StringComparison.Ordinal);
            int colon = key.IndexOf(':');
            int lane = key.IndexOf("/lane:", StringComparison.Ordinal);
            string token = group ? key[2..(lane < 0 ? key.Length : lane)] : colon >= 0 ? key[..colon] : "";
            if (!int.TryParse(token, out int proxy) || proxy < target)
            {
                order.Add(key);
                continue;
            }
            if (proxy > target) order.Add(group ? "G:" + (proxy - 1) + (lane < 0 ? "" : key[lane..]) : (proxy - 1) + key[colon..]);
        }
        Project.EditorTrackOrder = order;
    });
}
