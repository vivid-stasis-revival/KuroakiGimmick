using System.Text.Json;

namespace KuroakiGimmick.Core;

internal static class RecentProjectsSelfTest
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool ok, string label)
        {
            if (!ok) throw new Exception("Recent projects test failed: " + label);
            checks++;
            Console.WriteLine("PASS " + label);
        }
        string root = Path.Combine(Path.GetTempPath(), "kuroaki-recents-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string settingsPath = Path.Combine(root, "settings.json");
            var settings = ViewerSettings.Load(settingsPath);
            Check(settings.RecentProjects.Count == 0, "new settings have no recent projects");
            string A = Path.Combine(root, "a.sgv.json"), B = Path.Combine(root, "b.sgv.json");
            settings.RememberRecentSource(A);
            Check(settings.RecentProjects.SequenceEqual([A]), "remember first project");
            settings.RememberRecentSource(B);
            Check(settings.RecentProjects.SequenceEqual([B, A]), "new project leads recent list");
            settings.RememberRecentSource(A);
            Check(settings.RecentProjects.SequenceEqual([A, B]), "reopening project moves it to front");
            for (int i = 0; i < 14; i++) settings.RememberRecentSource(Path.Combine(root, $"project-{i}.sgv.json"));
            Check(settings.RecentProjects.Count == ViewerSettings.MaxRecentProjects &&
                settings.RecentProjects[0].EndsWith("project-13.sgv.json", StringComparison.Ordinal), "recent list is capped");
            string chart = Path.Combine(root, "song.vsc");
            settings.RememberRecentSource(chart);
            settings.RememberRecentSource(root);
            Check(settings.RecentProjects[0] == root && settings.RecentProjects[1] == chart,
                "charts and song folders enter recent list");
            settings.RememberRecentSource(root + Path.DirectorySeparatorChar);
            Check(settings.RecentProjects[0] == root && settings.RecentProjects.Count == ViewerSettings.MaxRecentProjects,
                "folder paths with trailing separators deduplicate");
            Check(!settings.RememberRecentSource(Path.Combine(root, "info.json")), "metadata JSON does not enter recent list");
            settings.PreviewVolume = .37; settings.GameUiFont = ViewerSettings.MonacoFont;
            settings.Persist(settingsPath);
            var reloaded = ViewerSettings.Load(settingsPath);
            Check(reloaded.RecentProjects.SequenceEqual(settings.RecentProjects), "recent list survives settings reload");
            Check(reloaded.PreviewVolume == .37 && reloaded.GameUiFont == ViewerSettings.MonacoFont,
                "persisting recents preserves preview defaults");
            var project = new ViewerProject { PreviewVolume = .91, GameUiFont = ViewerSettings.DefaultFont };
            string projectJson = JsonSerializer.Serialize(project, ViewerProject.Json);
            Check(!projectJson.Contains("RecentProjects", StringComparison.Ordinal), "recent list is absent from project JSON");
            File.WriteAllText(settingsPath, "{\"RecentProjects\":null,\"PreviewVolume\":0.4}");
            var nullList = ViewerSettings.Load(settingsPath);
            Check(nullList.RecentProjects.Count == 0 && nullList.PreviewVolume == .4, "null recents preserve other settings");
            File.WriteAllText(settingsPath, "{\"RecentProjects\":[\"bad\\u0000path.sgv.json\",null,42,true,\"skip.vsb\"]}");
            Check(ViewerSettings.Load(settingsPath).RecentProjects.Count == 1, "malformed recent entries are ignored without losing valid charts");
            File.WriteAllText(settingsPath, "{\"RecentProjects\":{\"invalid\":true},\"PreviewVolume\":0.6}");
            var malformed = ViewerSettings.Load(settingsPath);
            Check(malformed.RecentProjects.Count == 0 && malformed.PreviewVolume == .6,
                "invalid recent JSON preserves other settings");
            File.WriteAllText(settingsPath, "{\"recentProjects\":false,\"PreviewVolume\":0.2}");
            var lowerCase = ViewerSettings.Load(settingsPath);
            Check(lowerCase.RecentProjects.Count == 0 && lowerCase.PreviewVolume == .2,
                "case-insensitive malformed recent JSON preserves other settings");

            // 最近列表的标题取歌曲信息而不是文件名：谱面文件名本身就是难度（ENCORE.vsc），
            // 直接显示文件名会让整列看起来全是难度名。
            string song = Path.Combine(root, "scarlet");
            Directory.CreateDirectory(song);
            File.WriteAllText(Path.Combine(song, "ENCORE.vsc"), "0,3,0\n1000,0,0\n");
            File.WriteAllText(Path.Combine(song, "FINALE.vsc"), "0,3,0\n1000,0,0\n");
            File.WriteAllText(Path.Combine(song, "info.json"), """
            {
              "name":"Scarlet Death","artist":"lexycat","has_encore":true,
              "difficulty_display_3":"15","difficulty_display_4":"17",
              "enc_data":{"audio_id":"music_chart_scarlet.ogg"}
            }
            """);
            Check(RecentSource.Title(Path.Combine(song, "ENCORE.vsc")) == "Scarlet Death / lexycat @ BACKSTAGE  LV.17",
                "recent chart shows song name, artist, difficulty and level");
            Check(RecentSource.Title(song) == "Scarlet Death / lexycat @ BACKSTAGE  LV.17",
                "recent song folder shows the difficulty it would open");
            Check(RecentSource.Title(Path.Combine(song, "FINALE.vsc")) == "Scarlet Death / lexycat @ FINALE  LV.15",
                "each chart shows its own difficulty slot, not the folder's");
            Check(RecentSource.Title(chart) == "song.vsc", "a chart without song info keeps its file name");
            Check(RecentSource.Title(A) == "a", "a saved project keeps its project name");
            Check(RecentSource.Title(Path.Combine(root, "gone", "MIDDLE.vsc")) == "MIDDLE.vsc",
                "a removed chart still yields a title without throwing");

            // 只发 shatterinfo.json 的谱包：曲名与等级都只写在这一份文件里，曲师缺失时省略那一截。
            string shatter = Path.Combine(root, "drop");
            Directory.CreateDirectory(shatter);
            File.WriteAllText(Path.Combine(shatter, "SHATTER.vsc"), "0,3,0\n1000,0,0\n");
            File.WriteAllText(Path.Combine(shatter, SongInfo.ShatterFile),
                "{\"name\":\"Drop Song [Shatter]\",\"difficulty_number\":\"15+\",\"note_designer\":\"drop\"}");
            Check(RecentSource.Title(shatter) == "Drop Song [Shatter] @ SHATTER  LV.15+",
                "shatterinfo.json supplies the title and the folder falls back to SHATTER");
        }
        finally { Directory.Delete(root, true); }
        return checks;
    }
}
