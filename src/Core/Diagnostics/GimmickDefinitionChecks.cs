namespace KuroakiGimmick.Core;

/// <summary>通过实际发布的可执行文件检查外部对象定义加载，无需私有素材或 SDL。</summary>
internal static class GimmickDefinitionChecks
{
    internal static int Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "kg-definition-checks-" + Guid.NewGuid().ToString("N"));
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
            checks++;
        }
        void Reject(string json, string expected)
        {
            try
            {
                GimmickValidation.Validate(GimmickDefinitionReader.Read(json));
            }
            catch (InvalidDataException ex) when (ex.Message.Contains(expected, StringComparison.Ordinal))
            {
                checks++;
                return;
            }
            throw new InvalidDataException("Malformed definition was not rejected: " + expected);
        }
        const string objectName = "obj_starcrashers_gimmick";
        // 回归用例使用合成数据，无需内嵌游戏素材。
        const string v2 = """
            {"version":2,"objectName":"obj_starcrashers_gimmick","completeObject":false,
             "behaviors":[{"type":"masked-starfield","mods":{"starspd_multiplier":"configured_speed"}}],
             "extraMods":["configured_speed"],"defaults":{"configured_speed":2},
             "signals":{"begin":{"signal":"effect.on"}},
             "compositeLifecycle":{"initial":false,"create":["effect.on"],"destroy":["effect.off"]}}
            """;
        try
        {
            var legacy = GimmickDefinitionReader.Read("""{"version":1,"objectName":"obj_legacy_check"}""");
            GimmickValidation.Validate(legacy);
            Check(legacy.Version == 1, "Legacy v1 definition changed");
            var definition = GimmickDefinitionReader.Read(v2);
            GimmickValidation.Validate(definition);
            Check(definition.HasBehavior("masked-starfield"), "v2 behavior was discarded");
            Check(definition.Signals["begin"].Signal == "effect.on", "v2 signal was discarded");
            Check(definition.CompositeLifecycle?.Create.SequenceEqual(new[] { "effect.on" }) == true,
                "v2 lifecycle was discarded");

            // 核心 Base 和 Custom 谱面使用内置渲染路径，即使无原版定义也能工作；原版对象仍应报告缺失或无效清单。
            var customCore = NativeGimmickProfile.Load(new ViewerProject { Profile = "auto" },
                new Chart { ObjectName = "obj_custom_gimmick" }, root);
            Check(customCore.Data == null && customCore.Issues.Count == 0,
                "Custom core object incorrectly requires embedded definitions");
            var baseCore = NativeGimmickProfile.Load(new ViewerProject { Profile = "auto" },
                new Chart { ObjectName = "obj_base_gimmick" }, root);
            Check(baseCore.Data == null && baseCore.Issues.Count == 0,
                "Base core object incorrectly requires embedded definitions");

            string directory = Path.Combine(root, "Gimmicks", objectName);
            Directory.CreateDirectory(directory);
            string manifest = Path.Combine(directory, "manifest.json");
            File.WriteAllText(manifest, v2);
            var profile = NativeGimmickProfile.Load(new ViewerProject { Profile = "auto" },
                new Chart { ObjectName = objectName }, root);
            Check(profile.Data?.Version == 2 && profile.ResourcesReady,
                "External v2 resource pack failed: " + string.Join("; ", profile.Issues.Values));
            Check(profile.DefinitionSource == "shared", "External manifest was not selected");
            Check(profile.HasBehavior("masked-starfield") && profile.Supports("configured_speed"),
                "Configured behavior/mod did not reach the loaded profile");
            Check(profile.Data!.ModAliases["starspd_multiplier"] == "configured_speed", "Behavior role alias missing");

            Reject(v2.Replace("\"version\":2", "\"version\":3"), "Unsupported object definition version: 3");
            Reject(v2.Replace(objectName, ""), "Object definition name must not be empty");
            Reject(v2.Replace("masked-starfield", "unknown-component"), "Unknown runtime component");
            File.WriteAllText(manifest, v2.Replace("\"version\":2", "\"version\":3"));
            var invalid = NativeGimmickProfile.Load(new ViewerProject { Profile = "auto" },
                new Chart { ObjectName = objectName }, root);
            Check(invalid.Data == null && invalid.Issues.TryGetValue("definition", out var issue)
                && issue.Contains("Unsupported object definition version: 3", StringComparison.Ordinal),
                "Invalid external manifest silently fell back to another definition");
            Console.WriteLine($"PASS object definitions: {checks} checks (v1/v2, external loading, behavior bindings, invalid schema rejection)");
            return 0;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
