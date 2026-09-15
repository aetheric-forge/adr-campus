using AethericForge.Runtime.Abstractions.Interfaces.Institutions;
using AethericForge.Runtime.Abstractions.Interfaces.Institutions.Plugins;
using AethericForge.Runtime.Institutions.Abstractions.Builders;
using AethericForge.Runtime.Institutions.Abstractions.Primitives;
using AethericForge.Runtime.Institutions.Decisions;

namespace AdrCampus.Plugin;

/// <summary>
/// Builds and mounts an ADR Campus Decisions Office beneath any owning Institution. Unlike the standalone
/// ADR Campus web host's own root Campus, this does not build its own Archive/Library/PostOffice/Registry -
/// a Decisions Office is not sovereign, so it expects those to already be available from the owner's
/// institutional scope (as Campus-required capabilities) and only owns what is genuinely its own: the
/// Recorder.
/// </summary>
public sealed class DecisionsOrganizationFactory : IOrganizationFactory
{
    public string OrganizationId => "decisions";

    public IInstitutionManifest Manifest => Template.Descriptor;

    public IInstitutionTemplate Template { get; } = InstitutionTemplateBuilder.Create()
        .WithDescriptor("Decisions", new Version(1, 0, 0), "The ADR Campus decision-record office.")
        .Build();

    public IOrganization Create(IInstitution owner, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(services);

        var context = new DecisionsContext(Template, services, owner);
        return new Decisions(context, new AdrCampusRecorder());
    }
}
