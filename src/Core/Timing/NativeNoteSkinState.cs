namespace KuroakiGimmick.Core;

/// <summary>Source obj_note_rendering Step selection, evaluated from absolute time for reverse seeks.</summary>
public static class NativeNoteSkinState
{
    public static string? Name(string objectName, double time, Func<string, double> mod) => objectName switch
    {
        "obj_extendnova_gimmick" or "obj_sekaisen_gimmick" => mod("en_whiteoverlay") >= .5 ? "extendnova" : "normal",
        "obj_stopmotion_gimmick" or "obj_self_gimmick" => mod("stopmotionnoteskin") >= .5 ? "stopmotion" : "normal",
        "obj_stargazers_gimmick" => time >= 76.128 && time <= 96.758 ? "stargazers" : "normal",
        _ => null
    };
}
