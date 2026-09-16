using AdrCampus.Providers.Drafts.Workbench;
using AdrCampus.Providers.Library;
using AethericForge.Runtime.Institutions.Library;
using AethericForge.Runtime.Institutions.Workbench;
using Microsoft.Extensions.DependencyInjection;
using AethericForge.Runtime.Abstractions.Interfaces.Institutions;
using AethericForge.Runtime.Abstractions.Interfaces.Institutions.Plugins;
using AethericForge.Runtime.Institutions.Abstractions.Builders;
using AethericForge.Runtime.Institutions.Abstractions.Primitives;
using AethericForge.Runtime.Institutions.Decisions;

namespace AdrCampus.Plugin;

/// <summary>
/// Builds and mounts an ADR Campus Decisions Office beneath any owning Institution. Unlike the standalone
/// ADR Campus web host's own root Campus, this does not build its own Archive/Library/PostOffice/Registry -
/// a Decisions Office is not sovereign. This first operational slice requires the owner's Library and
/// Workbench and binds the existing ADR repositories to them. Dedicated workspace bindings are a later step.
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

        var binding = services.GetService<DecisionsOfficeBinding>()
            ?? throw new InvalidOperationException("Decisions requires an organization binding. Call AddDecisionsOffice before mounting it.");
        if (string.IsNullOrWhiteSpace(binding.OrganizationId.Value))
            throw new InvalidOperationException("Decisions requires a non-empty organization identity.");
        if (!owner.TryResolve<ILibrary>(out var library))
            throw new InvalidOperationException("Decisions proposal review requires ILibrary in the owning institution's scope.");
        if (!owner.TryResolve<IWorkbench>(out var workbench))
            throw new InvalidOperationException("Decisions proposal review requires IWorkbench in the owning institution's scope.");

        var drafts = new WorkbenchDraftRepository(workbench.Artificer);
        var proposals = new LibraryProposalRepository(library, drafts);
        var context = new DecisionsContext(Template, services, owner);
        return new Decisions(context, new AdrCampusRecorder(binding.OrganizationId, drafts, proposals));
    }
}
