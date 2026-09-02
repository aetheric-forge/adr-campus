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
}

public sealed record DraftSummary(
    AdrId Id,
    DraftTitle Title,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ModifiedAtUtc,
    long Version,
    AdrId? IntendedSupersessionTargetId = null);

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
