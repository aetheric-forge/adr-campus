using AdrCampus.Core.Domain;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdrCampus.Plugin.Tests;

public sealed class DecisionsOrganizationFactoryTests
{
    [Fact]
    public async Task Custom_parent_workspace_supports_drafting_and_proposal_without_a_legacy_stage()
    {
        using var host = new ReviewHost(workbenchStage: "orchard-sky-workbench");
        var proposal = await host.SeedAsync();
        Assert.Equal(ReviewHost.Organization, proposal.OrganizationId);
        Assert.Equal(ReviewHost.Maintainer, proposal.AuthorId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("stage:other")]
    [InlineData(" stage")]
    public void Invalid_workspace_binding_is_rejected_before_mount(string stage)
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() => services.AddDecisionsOffice(
            ReviewHost.Organization, _ => throw new NotImplementedException(), stage));
    }

    [Fact]
    public void MountsOperationalRecorderUnderGivenOwner()
    {
        using var host = new ReviewHost();
        Assert.Equal("decisions", new DecisionsOrganizationFactory().OrganizationId);
        Assert.Same(host.Owner, host.Office.Context.Owner);
        Assert.IsAssignableFrom<IAdrCampusRecorder>(host.Office.Recorder);
    }

    [Theory]
    [InlineData(false, true, "ILibrary")]
    [InlineData(true, false, "IWorkbench")]
    public void MissingParentCapabilityHasActionableDiagnostic(bool library, bool workbench, string contract)
    {
        using var host = new ReviewHost(library, workbench);
        var error = Assert.Throws<InvalidOperationException>(() => host.Owner);
        Assert.Contains(contract, error.Message);
        Assert.Contains("owning institution", error.Message);
    }

    [Fact]
    public void MissingDeploymentBindingHasActionableDiagnostic()
    {
        using var host = new ReviewHost(mount: false);
        using var emptyServices = new ServiceCollection().BuildServiceProvider();
        var error = Assert.Throws<InvalidOperationException>(() =>
            new DecisionsOrganizationFactory().Create(host.Owner, emptyServices));
        Assert.Contains("AddDecisionsOffice", error.Message);
    }

    [Fact]
    public void SecondOfficeRegistrationIsRejectedUntilInstanceBindingsAreSupported()
    {
        var services = new ServiceCollection();
        services.AddDecisionsOffice(ReviewHost.Organization, _ => throw new NotImplementedException());
        Assert.Throws<InvalidOperationException>(() => services.AddDecisionsOffice(
            new OrganizationId("other"), _ => throw new NotImplementedException()));
    }
}
