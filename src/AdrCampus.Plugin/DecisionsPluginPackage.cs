using AethericForge.Runtime.Abstractions.Interfaces.Institutions.Plugins;

namespace AdrCampus.Plugin;

public sealed class DecisionsPluginPackage : IInstitutionPluginPackage
{
    public IReadOnlyCollection<IInstitutionFactory> GetFactories() =>
        [new DecisionsInstitutionFactory()];
}
