using KuroakiGimmick.Core.Documentation;
using KuroakiGimmick.Core.Editing;
using KuroakiGimmick.Graphics;
using KuroakiGimmick.Native;
namespace KuroakiGimmick.UI;

/// <summary>
/// 文档正文里的 mod 名右键菜单：直接把该 gimmick 加到插入点。
/// 插入拍在打开菜单的一刻就锁定，之后即使播放头继续走，加进去的仍是当时那一拍。
/// </summary>
public sealed partial class Viewer
{
    readonly List<(Rect Rect, string Id)> referenceModHits = [];
    string referenceAddId = "";
    double referenceAddBeat;
    float referenceAddX, referenceAddY;
    bool referenceMenuInput;

    /// <summary>
    /// 登记正文中一处 mod 名的命中区。bounds 与 clip 求交，保证被裁掉的部分不会仍然可点。
    /// 只有 Kind 为 mod 的条目才登记，章节标题之类不参与。
    /// </summary>
    void RegisterReferenceMod(Rect bounds, Rect clip, string id)
    {
        if (VsmReference.Shared.Find(id)?.Kind != "mod") return;
        float x = Math.Max(bounds.X, clip.X), y = Math.Max(bounds.Y, clip.Y);
        float right = Math.Min(bounds.X + bounds.W, clip.X + clip.W), bottom = Math.Min(bounds.Y + bounds.H, clip.Y + clip.H);
        if (right > x && bottom > y) referenceModHits.Add((new(x, y, right - x, bottom - y), id));
    }
    /// <summary>右键命中取最后登记的那个区域，即绘制顺序上最靠上的一层；大纲盖住正文时不命中正文里的名字。</summary>
    bool HandleReferenceAddMenu(Sdl.Event e)
    {
        if (e.Type == 0x401 && e.Button == 3)
        {
            mouseX = e.X; mouseY = e.Y;
            var hit = referenceModHits.LastOrDefault(h => h.Rect.Contains(mouseX, mouseY));
            referenceAddId = hit.Id ?? "";
            if (referenceOutlineCoversArticle && referenceOutlineRect.Contains(mouseX, mouseY)) referenceAddId = "";
            referenceAddX = mouseX; referenceAddY = mouseY;
            referenceAddBeat = InsertionBeat;
            click = held = false; FocusReferenceSearch(false); motion.Snap("docs-add-menu", 0);
            return true;
        }
        if (referenceAddId.Length == 0) return false;
        if (e.Type is 0x400 or 0x401 or 0x402)
        {
            mouseX = e.X; mouseY = e.Y; held = false;
            if (e.Type == 0x401 && e.Button == 1) click = true;
            return true;
        }
        if (e.Type == 0x300 && e.Scan == 41) { referenceAddId = ""; click = false; }
        return true;
    }
    /// <summary>
    /// 把文档条目加到谱面。editFirst 为 true 时先打开 ADD GIMMICK 面板让用户改值再提交。
    /// </summary>
    void AddReferenceEntry(bool editFirst)
    {
        var entry = VsmReference.Shared.Find(referenceAddId);
        referenceAddId = "";
        if (entry?.Kind != "mod" || Current.IsEmpty || Busy) return;
        CloseReference(); referenceAlpha = 0; motion.Snap("docs", 0);
        if (!EnsureAuthoring()) return;
        editorMode = true;
        int target = GimmickAuthoring.SuggestedProxy(entry, newProxy, Current.Chart.Proxies);
        // 模板条目必须先落实成具体 identifier；切换 obj 也一律要求显式确认，不能一键静默改掉谱面对象。
        if (editFirst || entry.MatchPattern.Length > 0 ||
            (entry.Scope == "custom" && Current.Chart.ObjectName != "obj_custom_gimmick"))
        { OpenAddGimmick(entry, referenceAddBeat); return; }
        AddMod(referenceAddBeat, entry.Name, target);
    }
    /// <summary>画右键菜单。位置被夹进窗口内，菜单外的点击视为取消并被吞掉，不会穿透到下面的文档。</summary>
    void DrawReferenceAddMenu(int w, int h)
    {
        if (referenceAddId.Length == 0) return;
        var entry = VsmReference.Shared.Find(referenceAddId);
        var r = new Rect(Math.Clamp(referenceAddX, 12, Math.Max(12, w - 332)),
            Math.Clamp(referenceAddY, 12, Math.Max(12, h - 206)), Math.Min(320, w - 24), 194);
        using var fade = Canvas.Opacity(motion.To("docs-add-menu", 1, .12, 0));
        Canvas.Clip(null); Canvas.Fill(r, Color.Hex(0x252E3A)); Canvas.Border(r, DocsAccent);
        Text(entry?.Name ?? "", r.X + 12, r.Y + 12, 15, DocsText, max: r.W - 24, unified: true, bold: true);
        Text($"BEAT {referenceAddBeat:0.######}", r.X + 12, r.Y + 41, 13, DocsMuted);
        referenceMenuInput = true;
        if (ReferenceButton("ADD TO CHART", new(r.X + 12, r.Y + 67, r.W - 24, 34), enabled: !Current.IsEmpty && !Busy)) AddReferenceEntry(false);
        if (ReferenceButton("EDIT VALUES + ADD", new(r.X + 12, r.Y + 108, r.W - 24, 34), enabled: !Current.IsEmpty && !Busy)) AddReferenceEntry(true);
        if (ReferenceButton("CANCEL", new(r.X + 12, r.Y + 150, r.W - 24, 30))) referenceAddId = "";
        referenceMenuInput = false;
        if (click && !r.Contains(mouseX, mouseY)) { referenceAddId = ""; click = false; }
    }
}
