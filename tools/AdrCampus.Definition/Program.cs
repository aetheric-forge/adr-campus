using AdrCampus.Plugin;

if (args.Length != 2 || args[0] is not ("--write" or "--check"))
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/AdrCampus.Definition -- --write|--check <definition.yaml>");
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
