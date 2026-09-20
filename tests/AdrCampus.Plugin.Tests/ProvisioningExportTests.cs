using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Aetheric.Provisioning.Definitions;
using Aetheric.Provisioning.Engine;
using Xunit;

namespace AdrCampus.Plugin.Tests;

public sealed class ProvisioningExportTests
{
    private static readonly IReadOnlyDictionary<string, string> Sources = new Dictionary<string, string>
        { ["ILibrary"] = "owner.library", ["IWorkbench"] = "owner.workbench" };

    private static SourceBundle Bundle(string definition, string bindings)
    {
        SourceDocument Document(string text, string path) => new(text, new SourceProvenance(
            "https://github.com/aetheric-forge/adr-campus", new string('a', 40), path,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))));
        return new(Document(definition, "decisions.yaml"), Document(bindings, "decisions.bindings.yaml"));
    }

    [Fact]
    public void Checked_in_exports_match_generator_and_plan_only_required_parent_checks()
    {
        var definition = DecisionsDefinition.Current;
        var text = OfficeProvisioningExport.Definition(definition);
        var bindings = OfficeProvisioningExport.Bindings(definition, "example", Sources);
        foreach (var (name, expected) in new[] { ("decisions.yaml", text), ("decisions.bindings.yaml", bindings) })
            Assert.Equal(expected, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "provisioning", name)).Replace("\r\n", "\n"));
        var bundle = Bundle(text, bindings);
        var read = new InstitutionYamlReader().Read(bundle);
        Assert.Empty(read.Issues);
        var loaded = Assert.IsType<LoadedInstitution>(read.Institution);
        Assert.Equal(new[] { "ILibrary", "IWorkbench" }, loaded.Requirements.ParentContracts);
        Assert.All(loaded.Requirements.Resources, resource => Assert.Equal("parent", resource.Ownership));
        Assert.Empty(loaded.Bindings.Resources);
        var plan = new ProvisioningPlanner([]).Plan(new(loaded.Requirements, loaded.Bindings,
            new ParentContext("existing-owner", "owner-revision") { Capabilities = loaded.Bindings.ParentSources },
            bundle.Definition.Provenance, bundle.Bindings.Provenance));
        Assert.True(plan.IsValid, string.Join("; ", plan.Issues.Select(x => x.Message)));
        Assert.Equal(new[] { "ILibrary", "IWorkbench" }, plan.Plan!.Steps.Select(x => x.Target));
        Assert.All(plan.Plan.Steps, step => Assert.Equal(StepKind.CheckParent, step.Kind));
    }

    [Fact]
    public void Projection_translates_schema_without_changing_package_execution_or_optional_dependencies()
    {
        var original = DecisionsDefinition.Current;
        var changed = original with { Descriptor = original.Descriptor with { Name = "Changed Decisions" } };
        var exported = OfficeProvisioningExport.Definition(changed);
        var json = JsonNode.Parse(exported[exported.IndexOf('{')..])!;
        Assert.Equal("Changed Decisions", json["descriptor"]!["name"]!.GetValue<string>());
        Assert.NotNull(json["domains"]![0]!["requiredCapabilities"]);
        Assert.Null(json["domains"]![0]!["capabilities"]);
        Assert.Empty(json["initialState"]!["configuration"]!.AsObject());
        Assert.DoesNotContain("\"execution\"", exported);
        Assert.DoesNotContain("\"required\"", exported);
        Assert.Contains("\"execution\"", original.ToYaml());
        Assert.Equal(3, original.Dependencies.Count(x => !x.Required));
        Assert.Single(original.Executions);
    }

    [Fact]
    public void Missing_blank_and_extra_parent_sources_cannot_be_exported()
    {
        foreach (var sources in new[] {
            new Dictionary<string, string> { ["ILibrary"] = "owner.library" },
            new Dictionary<string, string> { ["ILibrary"] = "owner.library", ["IWorkbench"] = " " },
            new Dictionary<string, string> { ["ILibrary"] = "owner.library", ["IWorkbench"] = "owner.workbench", ["IArchive"] = "owner.archive" } })
            Assert.Throws<ArgumentException>(() => OfficeProvisioningExport.Bindings(DecisionsDefinition.Current, "test", sources));
    }

    [Fact]
    public void Tampered_bindings_with_missing_parent_are_rejected_by_real_planner()
    {
        var text = OfficeProvisioningExport.Bindings(DecisionsDefinition.Current, "test", Sources);
        var json = JsonNode.Parse(text[text.IndexOf('{')..])!;
        json["bindings"]!.AsObject().Remove("IWorkbench");
        var bundle = Bundle(OfficeProvisioningExport.Definition(DecisionsDefinition.Current), json.ToJsonString());
        var read = new InstitutionYamlReader().Read(bundle);
        Assert.Empty(read.Issues);
        var loaded = read.Institution!;
        var plan = new ProvisioningPlanner([]).Plan(new(loaded.Requirements, loaded.Bindings,
            new ParentContext("owner", "revision"), bundle.Definition.Provenance, bundle.Bindings.Provenance));
        Assert.False(plan.IsValid);
        Assert.Contains(plan.Issues, issue => issue.Code == "dependency.binding_missing" && issue.Target == "IWorkbench");
    }
}
