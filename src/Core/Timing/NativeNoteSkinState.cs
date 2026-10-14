namespace KuroakiGimmick.Core;

/// <summary>Source obj_note_rendering Step selection, evaluated from absolute time for reverse seeks.</summary>
public static class NativeNoteSkinState
{
    public static GimmickSkinRule? At(GimmickDefinition? definition,double time,Func<string,double> mod) =>
        definition?.SkinRules.FirstOrDefault(rule=>(rule.Mod==null||mod(rule.Mod)>=rule.Threshold)
            &&(rule.Start==null||time>=rule.Start)&&(rule.End==null||time<=rule.End));
    public static string? Name(GimmickDefinition? definition,double time,Func<string,double> mod)=>At(definition,time,mod)?.Skin;
}
