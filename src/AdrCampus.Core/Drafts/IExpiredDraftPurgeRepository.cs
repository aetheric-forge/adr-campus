using AdrCampus.Core.Domain;

namespace AdrCampus.Core.Drafts;

/// <summary>
/// Physical removal of expired drafts. Distinct from <see cref="IDraftRecoveryRepository"/> because
/// purging is a storage-maintenance concern: expiry already makes a draft inaccessible on its own
/// (<see cref="AdrDraft.IsExpired"/>), independent of whether this has run.
/// </summary>
public interface IExpiredDraftPurgeRepository
{
    Task<IReadOnlyList<AdrId>> ListExpiredAsync(OrganizationId organizationId, DateTimeOffset now, int batchSize, CancellationToken cancellationToken = default);
    Task<int> PurgeBatchAsync(OrganizationId organizationId, IReadOnlyCollection<AdrId> draftIds, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken = default);
}
