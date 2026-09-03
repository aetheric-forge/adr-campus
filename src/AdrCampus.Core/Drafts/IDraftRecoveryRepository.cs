using AdrCampus.Core.Administration;
using AdrCampus.Core.Domain;

namespace AdrCampus.Core.Drafts;

/// <summary>
/// Maintainer- and system-facing draft recovery operations, kept separate from the strictly
/// author-scoped <see cref="IDraftRepository"/> so an ordinary member's access boundary is never
/// implicated by a recovery read or write. <see cref="ListEligibleAsync"/> is the only listing this
/// interface exposes and it never returns draft content.
/// </summary>
public interface IDraftRecoveryRepository
{
    Task<RecoveryWriteResult> StartRecoveryAsync(OrganizationId organizationId, AdrId draftId, MemberId authorId, long expectedVersion, DateTimeOffset deadlineUtc, AdministrationEvent administrationEvent, CancellationToken cancellationToken = default);
    Task<RecoveryWriteResult> CancelRecoveryAsync(OrganizationId organizationId, AdrId draftId, MemberId authorId, long expectedVersion, AdministrationEvent administrationEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecoveryEligibleDraft>> ListEligibleAsync(OrganizationId organizationId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<ReassignDraftResult> ReassignAsync(OrganizationId organizationId, AdrId draftId, MemberId formerAuthorId, MemberId newAuthorId, long expectedVersion, DateTimeOffset now, AdministrationEvent administrationEvent, OperationId operationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdministrationEvent>> ListRecoveryEventsAsync(OrganizationId organizationId, CancellationToken cancellationToken = default);
}

public enum RecoveryWriteStatus { Applied, AlreadyApplied, Conflict, NotFound }
public sealed record RecoveryWriteResult(RecoveryWriteStatus Status, AdrDraft? Draft);

public sealed record RecoveryEligibleDraft(AdrId Id, DraftTitle Title, MemberId FormerAuthorId, DateTimeOffset ExpiresAtUtc, long Version);

public enum ReassignDraftStatus { Reassigned, AlreadyApplied, Conflict, NotFound, Expired, OperationMismatch }
public sealed record ReassignDraftResult(ReassignDraftStatus Status, AdrDraft? Draft)
{
    public bool IsSuccess => Status is ReassignDraftStatus.Reassigned or ReassignDraftStatus.AlreadyApplied;
}
