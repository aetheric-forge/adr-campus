using AdrCampus.Application.Identity;
using AdrCampus.Application.Proposals;
using AdrCampus.Core.Domain;
using AdrCampus.Providers.Drafts.Workbench;
using AdrCampus.Providers.Library;
using AethericForge.Runtime.Abstractions.Interfaces.Authorities;
using AethericForge.Runtime.Abstractions.Interfaces.Decisions.Services;
using AethericForge.Runtime.Models.Authorities;

namespace AdrCampus.Plugin;

/// <summary>ADR-specific operations exposed by the mounted Decisions organization.</summary>
public interface IAdrCampusRecorder : IRecorder
{
    /// <summary>
    /// The trusted host supplies request-scoped identity and membership adapters. Do not cache this
    /// session on the singleton organization or accept these adapters from a client payload.
    /// </summary>
    IProposalReview OpenReview(IProposalReviewCaller caller, IMemberAuthority authority, TimeProvider clock);
}

public sealed class AdrCampusRecorder : IAdrCampusRecorder
{
    private readonly OrganizationId _organizationId;
    internal WorkbenchDraftRepository Drafts { get; }
    internal LibraryProposalRepository Proposals { get; }
    public ITeam<IDecisionsClerk> Team { get; } = new Team<IDecisionsClerk>([]);

    internal AdrCampusRecorder(OrganizationId organizationId, WorkbenchDraftRepository drafts,
        LibraryProposalRepository proposals)
    {
        _organizationId = organizationId;
        Drafts = drafts;
        Proposals = proposals;
    }

    public IProposalReview OpenReview(IProposalReviewCaller caller, IMemberAuthority authority, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(clock);
        return new ReviewSession(_organizationId, caller,
            new ProposalApplicationService(Drafts, Proposals, Proposals, authority, clock));
    }

    private sealed class ReviewSession(OrganizationId organizationId, IProposalReviewCaller caller,
        ProposalApplicationService proposals) : IProposalReview
    {
        public async Task<PrepareDecisionResult> PrepareAsync(AdrId proposalId, DecisionOutcome outcome,
            string note, CancellationToken cancellationToken = default)
        {
            var member = await caller.GetMemberIdAsync(cancellationToken).ConfigureAwait(false);
            if (member is null) return PrepareDecisionResult.Unauthorized();
            return await proposals.PrepareDecisionAsync(organizationId, member, proposalId, outcome,
                note, cancellationToken).ConfigureAwait(false);
        }

        public async Task<DecisionCommandResult> DecideAsync(ReviewDecision command,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(command);
            var member = await caller.GetMemberIdAsync(cancellationToken).ConfigureAwait(false);
            if (member is null) return DecisionCommandResult.Unauthorized();
            return await proposals.DecideAsync(new DecisionCommand(organizationId, command.ProposalId,
                command.ExpectedProposedAtUtc, member, command.Outcome, command.Note,
                command.OperationId), cancellationToken).ConfigureAwait(false);
        }
    }
}
