namespace KuroakiGimmick.Core.Editing;

public sealed partial class EditorDocument
{
    /// <summary>一次提交两份源码草稿；不改写无关行，不展开循环，一步撤销恢复两份文档。</summary>
    public void ApplySource(string vsm, string vsp)
    {
        bool modsChanged = vsm != Vsm.Text, imagesChanged = vsp != Images.Text;
        if (!modsChanged && !imagesChanged) return;
        if (ChartLoadLimits.Exceeds(vsm.Length, 64 * 1024 * 1024) || ChartLoadLimits.Exceeds(vsp.Length, 16 * 1024 * 1024))
            throw new InvalidDataException("Source exceeds the VSM/VSP size limit.");
        var parsed = modsChanged ? VsmDocument.FromText(vsm) : null;
        // Validate recognized image declarations without requiring resources to exist yet.
        if (imagesChanged) VspDocument.Rewrite(vsp, token => token);
        Change("Edit VSM / VSP source", () =>
        {
            if (parsed != null) Vsm.Restore(parsed.Lines.ToArray());
            if (imagesChanged) Images.Restore(vsp);
        });
    }
}
