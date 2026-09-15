using AethericForge.Runtime.Abstractions.Interfaces.Institutions;
using AethericForge.Runtime.Abstractions.Interfaces.Institutions.Plugins;
using AethericForge.Runtime.Institutions.Abstractions.Builders;
using AethericForge.Runtime.Institutions.Abstractions.Primitives;
using AethericForge.Runtime.Institutions.Decisions;

namespace AdrCampus.Plugin;

/// <summary>
/// Builds and mounts an ADR Campus Decisions Office beneath any parent Institution. Unlike the standalone
/// ADR Campus web host, this does not build its own Archive/Library/PostOffice/Registry - a Decisions
/// Office is not sovereign, so it expects those to already be available from the parent's institutional
/// scope (as Campus-required capabilities) and only owns what is genuinely its own: the Recorder.
/// </summary>
public sealed class DecisionsInstitutionFactory : IInstitutionFactory
{
    public Type ContractType => typeof(IDecisions);

    public IInstitutionManifest Manifest => Template.Descriptor;

    public IInstitutionTemplate Template { get; } = InstitutionTemplateBuilder.Create()
        .WithDescriptor("Decisions", new Version(1, 0, 0), "The ADR Campus decision-record office.")
        .Build();

    public IInstitution Create(IInstitution parent, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(services);

        var context = new DecisionsContext(Template, services, parent);
        return new Decisions(context, new AdrCampusRecorder());
    }
}
