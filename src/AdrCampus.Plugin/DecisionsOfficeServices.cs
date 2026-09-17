using AdrCampus.Application.Identity;
using AdrCampus.Application.Proposals;
using AdrCampus.Core.Discovery;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Drafts;
using AdrCampus.Core.Proposals;
using AethericForge.Runtime.Abstractions.Interfaces.Institutions;
using AethericForge.Runtime.Institutions.Decisions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AdrCampus.Plugin;

/// <summary>Trusted deployment binding for the single office currently supported by this package.</summary>
public sealed record DecisionsOfficeBinding(OrganizationId OrganizationId);

public static class DecisionsOfficeServices
{
    /// <summary>
    /// Registers the package's repositories and review services. The host supplies its owning
    /// institution, authenticated IProposalReviewCaller and IMemberAuthority adapters. This does not
    /// mount the organization; the owner mounts DecisionsOrganizationFactory during composition.
    /// </summary>
    public static IServiceCollection AddDecisionsOffice(this IServiceCollection services,
        OrganizationId organizationId, Func<IServiceProvider, IInstitution> resolveOwner)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(resolveOwner);
        ArgumentNullException.ThrowIfNull(organizationId);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(DecisionsOfficeBinding)))
            throw new InvalidOperationException("AddDecisionsOffice supports one mounted office per service provider.");
        if (string.IsNullOrWhiteSpace(organizationId.Value))
            throw new ArgumentException("Decisions requires a non-empty organization identity.", nameof(organizationId));

        services.AddSingleton(new DecisionsOfficeBinding(organizationId));
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<AdrCampusRecorder>(sp =>
        {
            var office = resolveOwner(sp).ResolveOrganization<IDecisions>("decisions");
            return office.Recorder as AdrCampusRecorder
                ?? throw new InvalidOperationException("The mounted decisions organization must use AdrCampusRecorder.");
        });
        services.AddSingleton<IDraftRepository>(sp => sp.GetRequiredService<AdrCampusRecorder>().Drafts);
        services.AddSingleton<IDraftRecoveryRepository>(sp => sp.GetRequiredService<AdrCampusRecorder>().Drafts);
        services.AddSingleton<IExpiredDraftPurgeRepository>(sp => sp.GetRequiredService<AdrCampusRecorder>().Drafts);
        services.AddSingleton<IProposalRepository>(sp => sp.GetRequiredService<AdrCampusRecorder>().Proposals);
        services.AddSingleton<ISharedRecordRepository>(sp => sp.GetRequiredService<AdrCampusRecorder>().Proposals);
        // Legacy drafting/discovery pages share exactly the same stores while they are migrated.
        services.AddScoped<ProposalApplicationService>();
        services.AddScoped<AethericContracts.Interactions.IInteractionProvider, ProposalReviewInteractionProvider>();
        services.AddScoped<IProposalReview>(sp => sp.GetRequiredService<AdrCampusRecorder>().OpenReview(
            sp.GetRequiredService<IProposalReviewCaller>(), sp.GetRequiredService<IMemberAuthority>(),
            sp.GetRequiredService<TimeProvider>()));
        return services;
    }
}
