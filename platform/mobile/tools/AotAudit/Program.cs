using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;

if (args.Length != 1 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: AotAudit /path/to/KuroakiGimmick.app");
    return 2;
}
var modules = new Dictionary<string, Guid>(StringComparer.Ordinal);
foreach (string path in Directory.EnumerateFiles(args[0], "*.dll"))
{
    using var file = File.OpenRead(path);
    using var pe = new PEReader(file);
    var reader = pe.GetMetadataReader();
    modules.Add(reader.GetString(reader.GetAssemblyDefinition().Name), reader.GetGuid(reader.GetModuleDefinition().Mvid));
}
var references = new Regex(@"([A-Za-z0-9_.+-]+)\x00([A-Fa-f0-9]{8}-[A-Fa-f0-9]{4}-[A-Fa-f0-9]{4}-[A-Fa-f0-9]{4}-[A-Fa-f0-9]{12})\x00");
int checkedReferences = 0, failures = 0, images = 0;
foreach (string path in Directory.EnumerateFiles(args[0], "*.aotdata.arm64"))
{
    images++;
    string data = Encoding.Latin1.GetString(File.ReadAllBytes(path));
    var matches = references.Matches(data);
    if (matches.Count == 0) { Console.Error.WriteLine("No AOT dependency table: " + Path.GetFileName(path)); failures++; }
    foreach (Match match in matches)
    {
        string name = match.Groups[1].Value;
        // The compiler-only generic-instantiation module has no packaged IL.
        if (name == "aot-instances" && Path.GetFileName(path) == "aot-instances.aotdata.arm64") continue;
        Guid expected = Guid.Parse(match.Groups[2].Value);
        checkedReferences++;
        if (!modules.TryGetValue(name, out var actual) || actual != expected)
        {
            failures++;
            Console.Error.WriteLine($"{Path.GetFileName(path)} → {name}: expected {expected}, bundled {(modules.ContainsKey(name) ? actual.ToString() : "missing")}");
        }
    }
}
if (images == 0) { Console.Error.WriteLine("No ARM64 AOT data found."); return 1; }
Console.WriteLine($"AOT dependency audit: {images} images, {checkedReferences} references, {failures} mismatches.");
return failures == 0 ? 0 : 1;
