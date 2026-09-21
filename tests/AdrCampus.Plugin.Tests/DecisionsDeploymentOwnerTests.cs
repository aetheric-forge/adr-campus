using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdrCampus.Plugin;
using Aetheric.Provisioning.Definitions;
using Aetheric.Provisioning.Engine;
using Aetheric.Provisioning.Workbench.Redis;
using Aetheric.Provisioning.Worker;
using Xunit;

namespace AdrCampus.Plugin.Tests;

public sealed class DecisionsDeploymentOwnerTests
{
    private static DecisionsDeploymentOwner Owner => new("https://github.com/example/owner", new string('a', 40),
        "campus", "production", "faculty", "workspace");

    [Fact]
    public void Owner_mapping_matches_runtime_contract_and_preserves_existing_storage_names()
    {
        var parent = JsonSerializer.Deserialize<ParentIdentity>(JsonSerializer.Serialize(Owner.ToParentIdentity()))!;
        Assert.Equal(Owner.Repository, parent.Repository);
        Assert.Equal(Owner.Revision, parent.Revision);
        Assert.Equal("campus", parent.ResourceLocations["ILibrary"]);
        Assert.True(WorkbenchParentLocation.TryParse(parent.ResourceLocations["IWorkbench"], out var workspace));
        Assert.Equal(new WorkbenchParentLocation(1, "production", "faculty", "workspace", "adr-campus-workbench"), workspace);
        Assert.Equal("adr-campus", DecisionsDeploymentOwner.LibraryScheme);
    }

    [Fact]
    public void Explicit_owner_and_exported_bindings_produce_parent_checks_without_resource_creation()
    {
        SourceDocument Document(string text, string path) => new(text, new(Owner.Repository, Owner.Revision, path,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))));
        var bundle = new SourceBundle(Document(OfficeProvisioningExport.Definition(DecisionsDefinition.Current), "definition.yaml"),
            Document(Owner.ExportBindings("deployment"), "bindings.yaml"));
        var loaded = new InstitutionYamlReader().Read(bundle).Institution!;
        var parent = Owner.ToParentIdentity();
        var plan = new ProvisioningPlanner([]).Plan(new(loaded.Requirements, loaded.Bindings,
            new ParentContext(parent.Repository, parent.Revision) { Capabilities = DecisionsDeploymentOwner.ParentSources },
            bundle.Definition.Provenance, bundle.Bindings.Provenance));
        Assert.True(plan.IsValid);
        Assert.Equal(2, plan.Plan!.Steps.Length);
        Assert.All(plan.Plan.Steps, step => Assert.Equal(StepKind.CheckParent, step.Kind));
    }

    [Fact]
    public void Incomplete_or_unpinned_owner_cannot_export_bindings()
    {
        foreach (var owner in new[] { Owner with { Revision = "main" }, Owner with { Repository = "https://user:password@example.test" },
            Owner with { WorkspaceOwnerId = "" }, Owner with { LibraryOwnerId = "db@owner" }, Owner with { WorkspaceEnvironment = " " } })
            Assert.ThrowsAny<ArgumentException>(() => owner.ExportBindings("production"));
    }
}
