using AdrCampus.Application.Proposals;
using AdrCampus.Core.Domain;

namespace AdrCampus.Plugin;

/// <summary>A request-scoped review session for one mounted office. Commands cannot select an actor or office.</summary>
public interface IProposalReview
{
    Task<PrepareDecisionResult> PrepareAsync(AdrId proposalId, DecisionOutcome outcome, string note,
        CancellationToken cancellationToken = default);
    Task<DecisionCommandResult> DecideAsync(ReviewDecision command,
        CancellationToken cancellationToken = default);
}

/// <summary>Preserve OperationId and all inputs when retrying the same decision.</summary>
public sealed record ReviewDecision(AdrId ProposalId, DateTimeOffset ExpectedProposedAtUtc,
    DecisionOutcome Outcome, string Note, OperationId OperationId);

/// <summary>
/// Implemented by the trusted host using its authenticated request/circuit context, never form data.
/// Return null for an unauthenticated caller. Membership is checked independently on every operation.
/// </summary>
public interface IProposalReviewCaller
{
    Task<MemberId?> GetMemberIdAsync(CancellationToken cancellationToken = default);
}
