using AdrCampus.Core.Domain;

namespace AdrCampus.Core.Drafts;

/// <summary>
/// Stores private ADR drafts. Every read is scoped by organization and author so ordinary callers
/// cannot use this boundary to discover another member's draft.
/// </summary>
public interface IDraftRepository
{
    Task<DraftWriteResult> CreateAsync(
        AdrDraft draft,
        OperationId operationId,
        CancellationToken cancellationToken = default);

    Task<AdrDraft?> GetByAuthorAsync(
        OrganizationId organizationId,
        MemberId authorId,
        AdrId draftId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DraftSummary>> ListByAuthorAsync(
        OrganizationId organizationId,
        MemberId authorId,
        CancellationToken cancellationToken = default);

    Task<DraftWriteResult> SaveRevisionAsync(
        AdrDraft draft,
        long expectedPersistedVersion,
        OperationId operationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a draft that has been consumed by another institution (for example, published as a
    /// proposal into Library). Returns false if the draft is missing or the version no longer matches,
    /// which the caller treats as a best-effort cleanup rather than a hard failure.
    /// </summary>
    Task<bool> RemoveAsync(
        OrganizationId organizationId,
        MemberId authorId,
        AdrId draftId,
        long expectedVersion,
        CancellationToken cancellationToken = default);
}

public sealed record DraftSummary(
    AdrId Id,
    DraftTitle Title,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ModifiedAtUtc,
    long Version,
    AdrId? IntendedSupersessionTargetId = null,
    DateTimeOffset? RecoveryDeadlineUtc = null)
{
    public bool IsExpired(DateTimeOffset now) => RecoveryDeadlineUtc is not null && now >= RecoveryDeadlineUtc;
}

public enum DraftWriteStatus
{
    Created,
    Saved,
    AlreadyApplied,
    Conflict,
    OperationMismatch
}

public sealed record DraftWriteResult(DraftWriteStatus Status, AdrDraft? Draft)
{
    public bool IsSuccess => Status is DraftWriteStatus.Created or DraftWriteStatus.Saved or DraftWriteStatus.AlreadyApplied;
}
