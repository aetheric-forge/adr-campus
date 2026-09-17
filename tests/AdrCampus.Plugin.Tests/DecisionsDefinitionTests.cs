using AethericContracts.Interactions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdrCampus.Plugin.Tests;

public sealed class DecisionsDefinitionTests
{
    [Fact]
    public void InstalledTemplateIncludesPackageDefinitionAndResolvesCapabilityReferences()
    {
        using var host = new ReviewHost();
        var template = host.Office.Context.Template;
        Assert.Equal(7, template.Capabilities.Count);
        Assert.Equal(5, template.Domains.Count);
        Assert.Equal(2, template.Resources.Count);
        Assert.Equal(3, template.Workflows.Count);
        var review = Assert.Single(template.Capabilities.Where(x => x.Name == "Review and Decide"));
        var domain = Assert.Single(template.Domains.Where(x => x.Name == "Proposal & Review"));
        Assert.Contains(review, domain.RequiredCapabilities);
        var maintainer = Assert.Single(template.Roles.Where(x => x.Name == "Maintainer"));
        Assert.Contains(review, maintainer.Capabilities);
    }

    [Fact]
    public async Task DeclaredExecutionResolvesAUsableScopedInteractionInIndependentHost()
    {
        using var host = new ReviewHost();
        var proposal = await host.SeedAsync();
        using var scope = host.Services.CreateScope();
        var execution = Assert.Single(DecisionsDefinition.Current.Executions);
        var provider = Assert.Single(scope.ServiceProvider.GetServices<IInteractionProvider>());
        Assert.Equal(execution.Interaction, provider.Id);
        var actions = await provider.GetActionsAsync(proposal.Id.Value.ToString());
        Assert.Equal(2, actions.Count);
        var view = await provider.Open(proposal.Id.Value.ToString(), "accept").LoadAsync();
        Assert.True(view.IsAvailable);
    }

    [Fact]
    public void PackageDescriptionChangeUpdatesInstalledTemplateAndGeneratedArtifact()
    {
        var original = DecisionsDefinition.Current;
        var changed = original with { Descriptor = original.Descriptor with { Description = "Revised office description" } };
        Assert.Equal("Revised office description", changed.CreateTemplate().Descriptor.Description);
        Assert.Contains("Revised office description", changed.ToYaml());
    }

    [Theory]
    [InlineData("missing.operation", 1, "decisions.review", "operation")]
    [InlineData("decisions.proposal-review", 2, "decisions.review", "version")]
    [InlineData("decisions.proposal-review", 1, "missing.interaction", "interaction")]
    public void InvalidExecutionFailsBeforeTemplateInstallation(string operation, int version, string interaction, string diagnostic)
    {
        var original = DecisionsDefinition.Current;
        var changed = original with { Workflows = original.Workflows.Select(x => x.Id == "proposal-review"
            ? x with { Execution = new(operation, version, interaction) } : x).ToArray() };
        Assert.Contains(diagnostic, Assert.Throws<InvalidOperationException>(() => changed.CreateTemplate()).Message);
    }

    [Fact]
    public void WorkflowCannotReferenceExecutionAbsentFromCapabilities()
    {
        var original = DecisionsDefinition.Current;
        var broken = original with { Capabilities = original.Capabilities.Select(x => x with { Execution = null }).ToArray() };
        Assert.Contains("exposed by a capability", Assert.Throws<InvalidOperationException>(broken.Validate).Message);
    }

    [Fact]
    public void UnknownCapabilityAndDuplicateIdsFailValidation()
    {
        var original = DecisionsDefinition.Current;
        var broken = original with { Roles = [original.Roles[0] with { Capabilities = ["missing"] }] };
        Assert.Contains("unknown capability", Assert.Throws<InvalidOperationException>(broken.Validate).Message);
        var duplicate = original with { Workflows = [original.Workflows[0], original.Workflows[0]] };
        Assert.Contains("unique IDs", Assert.Throws<InvalidOperationException>(duplicate.Validate).Message);
    }

    [Theory]
    [InlineData("ILibrary")]
    [InlineData("IWorkbench")]
    public void CannotOmitRequiredRepositoryBindings(string contract)
    {
        var original = DecisionsDefinition.Current;
        var broken = original with { Dependencies = original.Dependencies.Where(x => x.Contract != contract).ToArray() };
        Assert.Contains(contract, Assert.Throws<InvalidOperationException>(broken.Validate).Message);
    }

    [Fact]
    public void UnknownRequiredBindingsAndUnimplementedOwnershipFailValidation()
    {
        var original = DecisionsDefinition.Current;
        var broken = original with { Dependencies = [..original.Dependencies, new("UnknownStore", "Missing binding", true)] };
        Assert.Contains("UnknownStore", Assert.Throws<InvalidOperationException>(broken.Validate).Message);
        var owned = original with { Resources = [original.Resources[0] with { Ownership = "owned" }] };
        Assert.Contains("not supported", Assert.Throws<InvalidOperationException>(owned.Validate).Message);
    }

    [Fact]
    public void PolicyTextCannotBecomeExecutableAndDoesNotSupplySettings()
    {
        var original = DecisionsDefinition.Current;
        Assert.All(original.Policies, x => Assert.Null(x.Execution));
        Assert.Empty(original.CreateTemplate().InitialState.Configuration);
        var broken = original with { Policies = [original.Policies[0] with { Execution = original.Executions.Single() }] };
        Assert.Contains("cannot declare execution", Assert.Throws<InvalidOperationException>(broken.Validate).Message);
    }

    [Fact]
    public void CheckedInYamlMatchesExecutableDefinition()
    {
        var artifact = Path.Combine(AppContext.BaseDirectory, "decisions-institution.yaml");
        Assert.Equal(DecisionsDefinition.Current.ToYaml(), File.ReadAllText(artifact).Replace("\r\n", "\n"));
    }
}
