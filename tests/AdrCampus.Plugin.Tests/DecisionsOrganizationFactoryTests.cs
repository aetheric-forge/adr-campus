using AdrCampus.Core.Domain;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdrCampus.Plugin.Tests;

public sealed class DecisionsOrganizationFactoryTests
{
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
