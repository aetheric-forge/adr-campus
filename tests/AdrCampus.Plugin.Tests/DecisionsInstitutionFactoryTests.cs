using AethericForge.Runtime.Abstractions.Interfaces.Institutions;
using AethericForge.Runtime.Institutions.Abstractions.Builders;
using AethericForge.Runtime.Institutions.Decisions;
using AethericForge.Runtime.Models.Institutions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdrCampus.Plugin.Tests;

public class DecisionsInstitutionFactoryTests
{
    private sealed class TestInstitution(IInstitutionContext context) : InstitutionBase(context);

    private static IInstitution CreateParent()
    {
        var template = InstitutionTemplateBuilder.Create()
            .WithDescriptor("TestCampus", new Version(1, 0, 0), "A test parent scope.")
            .Build();
        var services = new ServiceCollection().BuildServiceProvider();
        return new TestInstitution(new InstitutionContext(template, services));
    }

    [Fact]
    public void ContractType_IsIDecisions()
    {
        var factory = new DecisionsInstitutionFactory();

        Assert.Equal(typeof(IDecisions), factory.ContractType);
    }

    [Fact]
    public void Create_MountsUnderTheGivenParentWithARecorder()
    {
        var factory = new DecisionsInstitutionFactory();
        var parent = CreateParent();
        var services = new ServiceCollection().BuildServiceProvider();

        var decisions = (IDecisions)factory.Create(parent, services);

        Assert.Same(parent, decisions.Context.Parent);
        Assert.NotNull(decisions.Recorder);
    }

    [Fact]
    public void Create_RegistersCleanlyIntoTheParentScope()
    {
        var factory = new DecisionsInstitutionFactory();
        var parent = CreateParent();
        var services = new ServiceCollection().BuildServiceProvider();

        var decisions = (IDecisions)factory.Create(parent, services);
        parent.Register<IDecisions>(decisions);

        Assert.Same(decisions, parent.Resolve<IDecisions>());
    }
}
