using AdrCampus.Plugin;

if (args.Length == 2 && args[0] is "--write-provisioning" or "--check-provisioning")
{
    var definition = DecisionsDefinition.Current;
    var documents = new Dictionary<string, string>
    {
        ["decisions.yaml"] = OfficeProvisioningExport.Definition(definition),
        ["decisions.bindings.yaml"] = OfficeProvisioningExport.Bindings(definition, "example",
            new Dictionary<string, string> { ["ILibrary"] = "owner.library", ["IWorkbench"] = "owner.workbench" })
    };
    if (args[0] == "--write-provisioning") Directory.CreateDirectory(args[1]);
    foreach (var (name, text) in documents)
    {
        var path = Path.Combine(args[1], name);
        if (args[0] == "--write-provisioning") File.WriteAllText(path, text);
        else if (!File.Exists(path) || File.ReadAllText(path).Replace("\r\n", "\n") != text)
        {
            Console.Error.WriteLine($"Provisioning artifact '{path}' is stale. Regenerate with --write-provisioning.");
            return 1;
        }
    }
    Console.WriteLine("Provisioning artifacts match the package definition (example bindings only).");
    return 0;
}

if (args.Length != 2 || args[0] is not ("--write" or "--check"))
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/AdrCampus.Definition -- --write|--check <definition.yaml> OR --write-provisioning|--check-provisioning <directory>");
    return 2;
}
var generated = DecisionsDefinition.Current.ToYaml();
if (args[0] == "--write")
{
    File.WriteAllText(args[1], generated);
    return 0;
}
if (!File.Exists(args[1]) || File.ReadAllText(args[1]).Replace("\r\n", "\n") != generated)
{
    Console.Error.WriteLine("Institution definition is stale. Regenerate it with --write.");
    return 1;
}
Console.WriteLine("Institution definition matches the executable package.");
return 0;
