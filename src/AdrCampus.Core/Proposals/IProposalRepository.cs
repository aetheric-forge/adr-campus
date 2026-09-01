using AdrCampus.Core.Domain;
namespace AdrCampus.Core.Proposals;
public interface IProposalRepository
{
    Task<ProposalWriteResult> ProposeAsync(OrganizationId organizationId, MemberId authorId, AdrId draftId, long expectedDraftVersion, OperationId operationId, DateTimeOffset proposedAtUtc, CancellationToken cancellationToken = default);
    Task<AdrProposal?> GetAsync(OrganizationId organizationId, AdrId id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProposalSummary>> ListAsync(OrganizationId organizationId, CancellationToken cancellationToken = default);
}
public sealed record ProposalSummary(AdrId Id, DraftTitle Title, MemberId AuthorId, MemberId ProposerId, DateTimeOffset ProposedAtUtc);
public enum ProposalWriteStatus { Proposed, AlreadyApplied, Invalid, UnauthorizedOrNotFound, Conflict, OperationMismatch }
public sealed record ProposalWriteResult(ProposalWriteStatus Status, AdrProposal? Proposal, IReadOnlyList<ProposalValidationError> Errors)
{
    public bool IsSuccess => Status is ProposalWriteStatus.Proposed or ProposalWriteStatus.AlreadyApplied;
}
