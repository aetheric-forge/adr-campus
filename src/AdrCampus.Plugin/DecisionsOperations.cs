using AethericContracts.Interactions;
using AdrCampus.Application.Identity;
using AethericForge.Runtime.Abstractions.Interfaces.Institutions;
using AethericForge.Runtime.Institutions.Library;
using AethericForge.Runtime.Institutions.Workbench;
using Microsoft.Extensions.DependencyInjection;

namespace AdrCampus.Plugin;

/// <summary>Package-owned dispatch table. Definition strings never load arbitrary types.</summary>
internal static class DecisionsOperations
{
    internal const string ReviewId = "decisions.proposal-review";

    internal static void Validate(ExecutionReference reference)
    {
        if (reference.Operation != ReviewId)
            throw new InvalidOperationException($"Unknown Decisions operation '{reference.Operation}'.");
        if (reference.Version != 1)
            throw new InvalidOperationException($"Unsupported version {reference.Version} of '{reference.Operation}'; supported version is 1.");
        if (reference.Interaction != ProposalReviewInteractionProvider.ProviderId)
            throw new InvalidOperationException($"Unknown interaction '{reference.Interaction}' for '{reference.Operation}'.");
    }

    internal static void Register(IServiceCollection services, OfficeDefinition definition)
    {
        definition.Validate();
        foreach (var execution in definition.Executions)
        {
            Validate(execution);
            services.AddScoped<IProposalReview>(sp => sp.GetRequiredService<AdrCampusRecorder>().OpenReview(
                sp.GetRequiredService<IProposalReviewCaller>(), sp.GetRequiredService<IMemberAuthority>(),
                sp.GetRequiredService<TimeProvider>()));
            services.AddScoped<IInteractionProvider, ProposalReviewInteractionProvider>();
        }
    }

    internal static void ValidateParent(IInstitution owner, OfficeDefinition definition)
    {
        definition.Validate();
        foreach (var dependency in definition.Dependencies.Where(x => x.Required))
        {
            var found = dependency.Contract switch
            {
                "ILibrary" => owner.TryResolve<ILibrary>(out _),
                "IWorkbench" => owner.TryResolve<IWorkbench>(out _),
                _ => false
            };
            if (!found)
                throw new InvalidOperationException($"Decisions requires {dependency.Contract} in the owning institution's scope. {dependency.Reason}");
        }
    }
}
