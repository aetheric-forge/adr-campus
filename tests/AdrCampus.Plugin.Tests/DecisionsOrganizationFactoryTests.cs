using AethericForge.Runtime.Abstractions.Interfaces.Institutions;
using AethericForge.Runtime.Institutions.Abstractions.Builders;
using AethericForge.Runtime.Institutions.Decisions;
using AethericForge.Runtime.Models.Institutions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdrCampus.Plugin.Tests;

public class DecisionsOrganizationFactoryTests
{
    private sealed class TestInstitution(IInstitutionContext context) : InstitutionBase(context);

    private static IInstitution CreateOwner()
    {
        var template = InstitutionTemplateBuilder.Create()
            .WithDescriptor("TestCampus", new Version(1, 0, 0), "A test owning scope.")
            .Build();
        var services = new ServiceCollection().BuildServiceProvider();
        return new TestInstitution(new InstitutionContext(template, services));
    }

    [Fact]
    public void OrganizationId_IsDecisions()
    {
        var factory = new DecisionsOrganizationFactory();

        Assert.Equal("decisions", factory.OrganizationId);
    }

    [Fact]
    public void Create_MountsUnderTheGivenOwnerWithARecorder()
    {
        var factory = new DecisionsOrganizationFactory();
        var owner = CreateOwner();
        var services = new ServiceCollection().BuildServiceProvider();

        var decisions = (IDecisions)factory.Create(owner, services);

        Assert.Same(owner, decisions.Context.Owner);
        Assert.NotNull(decisions.Recorder);
    }

    [Fact]
    public void Create_RegistersCleanlyIntoTheOwnerScope()
    {
        var factory = new DecisionsOrganizationFactory();
        var owner = CreateOwner();
        var services = new ServiceCollection().BuildServiceProvider();

        var decisions = (IDecisions)factory.Create(owner, services);
        owner.RegisterOrganization(factory.OrganizationId, decisions);

        Assert.Same(decisions, owner.ResolveOrganization<IDecisions>(factory.OrganizationId));
    }
}
