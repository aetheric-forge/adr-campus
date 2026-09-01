using AdrCampus.Core.Domain;
using AdrCampus.Core.Drafts;

namespace AdrCampus.Providers.Drafts.InMemory;

public sealed class InMemoryDraftRepository : IDraftRepository
{
    private readonly object _sync = new();
    private readonly Dictionary<DraftKey, AdrDraft> _drafts = new();
    private readonly Dictionary<OperationId, AppliedOperation> _operations = new();

    public Task<DraftWriteResult> CreateAsync(
        AdrDraft draft,
        OperationId operationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            var operation = AppliedOperation.Create(draft);
            if (TryReplay(operationId, operation, out var replay))
            {
                return Task.FromResult(replay);
            }

            var key = DraftKey.For(draft);
            if (_drafts.TryGetValue(key, out var current))
            {
                return Task.FromResult(new DraftWriteResult(DraftWriteStatus.Conflict, current));
            }

            _drafts.Add(key, draft);
            _operations.Add(operationId, operation);
            return Task.FromResult(new DraftWriteResult(DraftWriteStatus.Created, draft));
        }
    }

    public Task<AdrDraft?> GetByAuthorAsync(
        OrganizationId organizationId,
        MemberId authorId,
        AdrId draftId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(organizationId);
        ArgumentNullException.ThrowIfNull(authorId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            return Task.FromResult(
                _drafts.TryGetValue(new DraftKey(organizationId, draftId), out var draft) &&
                draft.AuthorId == authorId
                    ? draft
                    : null);
        }
    }

    public Task<IReadOnlyList<DraftSummary>> ListByAuthorAsync(
        OrganizationId organizationId,
        MemberId authorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(organizationId);
        ArgumentNullException.ThrowIfNull(authorId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            IReadOnlyList<DraftSummary> summaries = _drafts.Values
                .Where(draft => draft.OrganizationId == organizationId && draft.AuthorId == authorId)
                .OrderByDescending(draft => draft.ModifiedAtUtc)
                .ThenBy(draft => draft.Id.Value)
                .Select(draft => new DraftSummary(
                    draft.Id,
                    draft.Content.Title,
                    draft.CreatedAtUtc,
                    draft.ModifiedAtUtc,
                    draft.Version))
                .ToArray();
            return Task.FromResult(summaries);
        }
    }

    public Task<DraftWriteResult> SaveRevisionAsync(
        AdrDraft draft,
        long expectedPersistedVersion,
        OperationId operationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedPersistedVersion < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedPersistedVersion),
                "A persisted draft version must be positive.");
        }
        if (draft.Version != checked(expectedPersistedVersion + 1))
        {
            throw new ArgumentException(
                "The revised draft must be exactly one version newer than the expected persisted version.",
                nameof(draft));
        }

        lock (_sync)
        {
            var operation = AppliedOperation.Revise(draft, expectedPersistedVersion);
            if (TryReplay(operationId, operation, out var replay))
            {
                return Task.FromResult(replay);
            }

            var key = DraftKey.For(draft);
            if (!_drafts.TryGetValue(key, out var current) ||
                current.Version != expectedPersistedVersion ||
                current.OrganizationId != draft.OrganizationId ||
                current.AuthorId != draft.AuthorId ||
                current.CreatedAtUtc != draft.CreatedAtUtc)
            {
                return Task.FromResult(new DraftWriteResult(DraftWriteStatus.Conflict, current));
            }

            _drafts[key] = draft;
            _operations.Add(operationId, operation);
            return Task.FromResult(new DraftWriteResult(DraftWriteStatus.Saved, draft));
        }
    }

    private bool TryReplay(
        OperationId operationId,
        AppliedOperation requested,
        out DraftWriteResult result)
    {
        if (!_operations.TryGetValue(operationId, out var applied))
        {
            result = null!;
            return false;
        }

        result = applied == requested
            ? new DraftWriteResult(DraftWriteStatus.AlreadyApplied, applied.Draft)
            : new DraftWriteResult(DraftWriteStatus.OperationMismatch, null);
        return true;
    }

    private readonly record struct DraftKey(OrganizationId OrganizationId, AdrId DraftId)
    {
        public static DraftKey For(AdrDraft draft) => new(draft.OrganizationId, draft.Id);
    }

    private sealed record AppliedOperation(
        WriteKind Kind,
        AdrDraft Draft,
        long? ExpectedPersistedVersion)
    {
        public static AppliedOperation Create(AdrDraft draft) => new(WriteKind.Create, draft, null);
        public static AppliedOperation Revise(AdrDraft draft, long expectedVersion) =>
            new(WriteKind.Revise, draft, expectedVersion);
    }

    private enum WriteKind
    {
        Create,
        Revise
    }
}
