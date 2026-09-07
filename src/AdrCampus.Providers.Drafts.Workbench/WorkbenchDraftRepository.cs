using System.Text.Json;
using AdrCampus.Core.Administration;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Drafts;
using AethericForge.Runtime.Abstractions.Interfaces.Staging.Primitives;
using AethericForge.Runtime.Abstractions.Interfaces.Workbench.Services;
using AethericForge.Runtime.Models.Staging;

namespace AdrCampus.Providers.Drafts.Workbench;

/// <summary>
/// Stores private ADR drafts, their recovery lifecycle, and expired-draft purge bookkeeping in the
/// Workbench institution's staging area. Proposals and shared/decided records no longer live here; see
/// AdrCampus.Providers.Library.LibraryProposalRepository.
/// </summary>
public sealed class WorkbenchDraftRepository(IArtificer artificer) : IDraftRepository, IDraftRecoveryRepository, IExpiredDraftPurgeRepository
{
    private const string Stage = "adr-campus-workbench";
    private const string CatalogKey = "adr-campus/drafts/catalog-v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private IStagingReference Reference => new StagingReference(Stage, CatalogKey);

    public Task<DraftWriteResult> CreateAsync(AdrDraft draft, OperationId operationId, CancellationToken cancellationToken = default) =>
        WriteAsync(operationId, "create", draft, null, catalog =>
        {
            var current = catalog.Drafts.FirstOrDefault(item => item.OrganizationId == draft.OrganizationId.Value && item.Id == draft.Id.Value);
            if (current is not null) return new DraftWriteResult(DraftWriteStatus.Conflict, ToDomain(current));
            catalog.Drafts.Add(FromDomain(draft));
            return new DraftWriteResult(DraftWriteStatus.Created, draft);
        }, cancellationToken);

    public async Task<AdrDraft?> GetByAuthorAsync(OrganizationId organizationId, MemberId authorId, AdrId draftId, CancellationToken cancellationToken = default)
    {
        var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
        var item = catalog.Drafts.FirstOrDefault(d => d.OrganizationId == organizationId.Value && d.AuthorId == authorId.Value && d.Id == draftId.Value);
        return item is null ? null : ToDomain(item);
    }

    public async Task<IReadOnlyList<DraftSummary>> ListByAuthorAsync(OrganizationId organizationId, MemberId authorId, CancellationToken cancellationToken = default)
    {
        var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return catalog.Drafts.Where(d => d.OrganizationId == organizationId.Value && d.AuthorId == authorId.Value)
            .OrderByDescending(d => d.ModifiedAtUtc).ThenBy(d => d.Id)
            .Select(d => new DraftSummary(new AdrId(d.Id), new DraftTitle(d.Title), d.CreatedAtUtc, d.ModifiedAtUtc, d.Version, ToAdrId(d.IntendedSupersessionTargetId), d.RecoveryDeadlineUtc)).ToArray();
    }

    public Task<DraftWriteResult> SaveRevisionAsync(AdrDraft draft, long expectedPersistedVersion, OperationId operationId, CancellationToken cancellationToken = default) =>
        WriteAsync(operationId, "revise", draft, expectedPersistedVersion, catalog =>
        {
            var index = catalog.Drafts.FindIndex(d => d.OrganizationId == draft.OrganizationId.Value && d.Id == draft.Id.Value);
            if (index < 0) return new DraftWriteResult(DraftWriteStatus.Conflict, null);
            var current = catalog.Drafts[index];
            if (current.Version != expectedPersistedVersion || current.AuthorId != draft.AuthorId.Value || current.CreatedAtUtc != draft.CreatedAtUtc)
                return new DraftWriteResult(DraftWriteStatus.Conflict, ToDomain(current));
            catalog.Drafts[index] = FromDomain(draft);
            return new DraftWriteResult(DraftWriteStatus.Saved, draft);
        }, cancellationToken);

    public async Task<bool> RemoveAsync(OrganizationId organizationId, MemberId authorId, AdrId draftId, long expectedVersion, CancellationToken cancellationToken = default)
    {
        await using var handle = await artificer.AcquireLockAsync(Reference, TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
        if (!handle.IsAcquired) throw new InvalidOperationException("The draft Workbench is busy. Retry the operation.");
        var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
        var index = catalog.Drafts.FindIndex(d => d.OrganizationId == organizationId.Value && d.AuthorId == authorId.Value && d.Id == draftId.Value);
        if (index < 0) return false;
        if (catalog.Drafts[index].Version != expectedVersion) return false;
        catalog.Drafts.RemoveAt(index);
        await SaveAsync(catalog, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<RecoveryWriteResult> StartRecoveryAsync(OrganizationId organizationId, AdrId draftId, MemberId authorId, long expectedVersion, DateTimeOffset deadlineUtc, AdministrationEvent administrationEvent, CancellationToken cancellationToken = default)
    {
        await using var handle = await artificer.AcquireLockAsync(Reference, TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
        if (!handle.IsAcquired) throw new InvalidOperationException("The draft Workbench is busy. Retry the operation.");
        var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
        var index = catalog.Drafts.FindIndex(d => d.OrganizationId == organizationId.Value && d.Id == draftId.Value);
        if (index < 0) return new(RecoveryWriteStatus.NotFound, null);
        var current = ToDomain(catalog.Drafts[index]);
        if (current.AuthorId != authorId) return new(RecoveryWriteStatus.Conflict, current);
        if (current.RecoveryDeadlineUtc is not null) return new(RecoveryWriteStatus.AlreadyApplied, current);
        if (current.Version != expectedVersion) return new(RecoveryWriteStatus.Conflict, current);
        var next = current.StartRecovery(deadlineUtc, administrationEvent.OccurredAtUtc);
        catalog.Drafts[index] = FromDomain(next);
        catalog.RecoveryEvents.Add(FromDomain(administrationEvent));
        await SaveAsync(catalog, cancellationToken).ConfigureAwait(false);
        return new(RecoveryWriteStatus.Applied, next);
    }

    public async Task<RecoveryWriteResult> CancelRecoveryAsync(OrganizationId organizationId, AdrId draftId, MemberId authorId, long expectedVersion, AdministrationEvent administrationEvent, CancellationToken cancellationToken = default)
    {
        await using var handle = await artificer.AcquireLockAsync(Reference, TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
        if (!handle.IsAcquired) throw new InvalidOperationException("The draft Workbench is busy. Retry the operation.");
        var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
        var index = catalog.Drafts.FindIndex(d => d.OrganizationId == organizationId.Value && d.Id == draftId.Value);
        if (index < 0) return new(RecoveryWriteStatus.NotFound, null);
        var current = ToDomain(catalog.Drafts[index]);
        if (current.AuthorId != authorId) return new(RecoveryWriteStatus.Conflict, current);
        if (current.IsExpired(administrationEvent.OccurredAtUtc)) return new(RecoveryWriteStatus.Expired, current);
        if (current.RecoveryDeadlineUtc is null) return new(RecoveryWriteStatus.AlreadyApplied, current);
        if (current.Version != expectedVersion) return new(RecoveryWriteStatus.Conflict, current);
        var next = current.CancelRecovery(administrationEvent.OccurredAtUtc);
        catalog.Drafts[index] = FromDomain(next);
        catalog.RecoveryEvents.Add(FromDomain(administrationEvent));
        await SaveAsync(catalog, cancellationToken).ConfigureAwait(false);
        return new(RecoveryWriteStatus.Applied, next);
    }

    public async Task<IReadOnlyList<RecoveryEligibleDraft>> ListEligibleAsync(OrganizationId organizationId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return catalog.Drafts
            .Where(d => d.OrganizationId == organizationId.Value && d.RecoveryDeadlineUtc is not null && d.RecoveryDeadlineUtc > now)
            .OrderBy(d => d.RecoveryDeadlineUtc).ThenBy(d => d.Id)
            .Select(d => new RecoveryEligibleDraft(new AdrId(d.Id), new DraftTitle(d.Title), new MemberId(d.AuthorId), d.RecoveryDeadlineUtc!.Value, d.Version))
            .ToArray();
    }

    public async Task<ReassignDraftResult> ReassignAsync(OrganizationId organizationId, AdrId draftId, MemberId formerAuthorId, MemberId newAuthorId, long expectedVersion, DateTimeOffset now, AdministrationEvent administrationEvent, OperationId operationId, CancellationToken cancellationToken = default)
    {
        await using var handle = await artificer.AcquireLockAsync(Reference, TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
        if (!handle.IsAcquired) throw new InvalidOperationException("The draft Workbench is busy. Retry the operation.");
        var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
        var prior = catalog.ReassignmentOperations.FirstOrDefault(o => o.Id == operationId.Value);
        if (prior is not null)
        {
            var same = prior.OrganizationId == organizationId.Value && prior.DraftId == draftId.Value && prior.FormerAuthorId == formerAuthorId.Value && prior.NewAuthorId == newAuthorId.Value && prior.ExpectedVersion == expectedVersion;
            return same ? new(ReassignDraftStatus.AlreadyApplied, ToDomain(prior.Draft)) : new(ReassignDraftStatus.OperationMismatch, null);
        }
        var index = catalog.Drafts.FindIndex(d => d.OrganizationId == organizationId.Value && d.Id == draftId.Value);
        if (index < 0) return new(ReassignDraftStatus.NotFound, null);
        var current = ToDomain(catalog.Drafts[index]);
        if (current.AuthorId != formerAuthorId || current.Version != expectedVersion || current.RecoveryDeadlineUtc is null) return new(ReassignDraftStatus.Conflict, current);
        if (current.IsExpired(now)) return new(ReassignDraftStatus.Expired, current);
        var reassigned = current.Reassign(newAuthorId, now);
        catalog.Drafts[index] = FromDomain(reassigned);
        catalog.RecoveryEvents.Add(FromDomain(administrationEvent));
        catalog.ReassignmentOperations.Add(new(operationId.Value, organizationId.Value, draftId.Value, formerAuthorId.Value, newAuthorId.Value, expectedVersion, FromDomain(reassigned)));
        await SaveAsync(catalog, cancellationToken).ConfigureAwait(false);
        return new(ReassignDraftStatus.Reassigned, reassigned);
    }

    public async Task<IReadOnlyList<AdministrationEvent>> ListRecoveryEventsAsync(OrganizationId organizationId, CancellationToken cancellationToken = default)
    {
        var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return catalog.RecoveryEvents.Where(e => e.OrganizationId == organizationId.Value)
            .OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Id).Select(ToDomain).ToArray();
    }

    public async Task<IReadOnlyList<AdrId>> ListExpiredAsync(OrganizationId organizationId, DateTimeOffset now, int batchSize, CancellationToken cancellationToken = default)
    {
        var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return catalog.Drafts
            .Where(d => d.OrganizationId == organizationId.Value && d.RecoveryDeadlineUtc is not null && d.RecoveryDeadlineUtc <= now)
            .OrderBy(d => d.RecoveryDeadlineUtc).ThenBy(d => d.Id)
            .Take(batchSize)
            .Select(d => new AdrId(d.Id))
            .ToArray();
    }

    public async Task<int> PurgeBatchAsync(OrganizationId organizationId, IReadOnlyCollection<AdrId> draftIds, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken = default)
    {
        if (draftIds.Count == 0) return 0;
        await using var handle = await artificer.AcquireLockAsync(Reference, TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
        if (!handle.IsAcquired) throw new InvalidOperationException("The draft Workbench is busy. Retry the operation.");
        var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
        var ids = draftIds.Select(id => id.Value).ToHashSet();
        var purged = 0;
        foreach (var record in catalog.Drafts.Where(d => d.OrganizationId == organizationId.Value && ids.Contains(d.Id) && d.RecoveryDeadlineUtc is not null && d.RecoveryDeadlineUtc <= occurredAtUtc).ToArray())
        {
            catalog.Drafts.Remove(record);
            catalog.RecoveryEvents.Add(FromDomain(new AdministrationEvent(Guid.NewGuid(), organizationId, AdministrationEventType.DraftExpired, occurredAtUtc, "Maintenance", SubjectId: new(record.AuthorId), DraftId: new(record.Id))));
            purged++;
        }
        if (purged > 0)
        {
            await SaveAsync(catalog, cancellationToken).ConfigureAwait(false);
        }
        return purged;
    }

    private async Task<DraftWriteResult> WriteAsync(OperationId operationId, string kind, AdrDraft draft, long? expectedVersion, Func<Catalog, DraftWriteResult> apply, CancellationToken cancellationToken)
    {
        await using var handle = await artificer.AcquireLockAsync(Reference, TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
        if (!handle.IsAcquired) throw new InvalidOperationException("The draft Workbench is busy. Retry the operation.");
        var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
        var requested = new OperationRecord(operationId.Value, kind, FromDomain(draft), expectedVersion);
        var prior = catalog.Operations.FirstOrDefault(o => o.Id == operationId.Value);
        if (prior is not null) return prior == requested ? new DraftWriteResult(DraftWriteStatus.AlreadyApplied, ToDomain(prior.Draft)) : new DraftWriteResult(DraftWriteStatus.OperationMismatch, null);
        var result = apply(catalog);
        if (result.IsSuccess) { catalog.Operations.Add(requested); await SaveAsync(catalog, cancellationToken).ConfigureAwait(false); }
        return result;
    }

    private async Task<Catalog> ReadAsync(CancellationToken cancellationToken)
    {
        if (!await artificer.ExistsAsync(Reference, cancellationToken).ConfigureAwait(false)) return new Catalog();
        await using var stream = await artificer.OpenReadAsync(Reference, cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<Catalog>(stream, Json, cancellationToken).ConfigureAwait(false) ?? new Catalog();
    }

    private async Task SaveAsync(Catalog catalog, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(); await JsonSerializer.SerializeAsync(stream, catalog, Json, cancellationToken).ConfigureAwait(false); stream.Position = 0;
        await artificer.PutAsync(Stage, CatalogKey, stream, new StagingMetadata(contentType: "application/json", lastModifiedUtc: DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
    }

    private static DraftRecord FromDomain(AdrDraft d) => new(d.Id.Value, d.OrganizationId.Value, d.AuthorId.Value, d.Content.Title.Value, d.Content.Context, d.Content.Decision, d.Content.Consequences, d.CreatedAtUtc, d.ModifiedAtUtc, d.Version, d.IntendedSupersessionTargetId?.Value, d.RecoveryDeadlineUtc);
    private static AdrDraft ToDomain(DraftRecord d) => AdrDraft.Restore(new AdrId(d.Id), new OrganizationId(d.OrganizationId), new MemberId(d.AuthorId), new DraftContent(d.Title, d.Context, d.Decision, d.Consequences), d.CreatedAtUtc, d.ModifiedAtUtc, d.Version, ToAdrId(d.IntendedSupersessionTargetId), d.RecoveryDeadlineUtc);
    private static AdrId? ToAdrId(Guid? value) => value is null ? null : new AdrId(value.Value);
    private static EventRecord FromDomain(AdministrationEvent value) => new(value.Id, value.OrganizationId.Value, value.Type, value.OccurredAtUtc, value.Source, value.ActorId?.Value, value.PreviousValue, value.NewValue, value.SubjectId?.Value, value.DraftId?.Value);
    private static AdministrationEvent ToDomain(EventRecord value) => new(value.Id, new(value.OrganizationId), value.Type, value.OccurredAtUtc, value.Source, value.ActorId is null ? null : new(value.ActorId), value.PreviousValue, value.NewValue, value.SubjectId is null ? null : new(value.SubjectId), value.DraftId is null ? null : new(value.DraftId.Value));

    public sealed class Catalog { public List<DraftRecord> Drafts { get; set; } = []; public List<OperationRecord> Operations { get; set; } = []; public List<EventRecord> RecoveryEvents { get; set; } = []; public List<ReassignmentOperationRecord> ReassignmentOperations { get; set; } = []; }
    public sealed record DraftRecord(Guid Id, string OrganizationId, string AuthorId, string Title, string Context, string Decision, string Consequences, DateTimeOffset CreatedAtUtc, DateTimeOffset ModifiedAtUtc, long Version, Guid? IntendedSupersessionTargetId = null, DateTimeOffset? RecoveryDeadlineUtc = null);
    public sealed record OperationRecord(Guid Id, string Kind, DraftRecord Draft, long? ExpectedVersion);
    public sealed record EventRecord(Guid Id, string OrganizationId, AdministrationEventType Type, DateTimeOffset OccurredAtUtc, string Source, string? ActorId, string? PreviousValue, string? NewValue, string? SubjectId, Guid? DraftId);
    public sealed record ReassignmentOperationRecord(Guid Id, string OrganizationId, Guid DraftId, string FormerAuthorId, string NewAuthorId, long ExpectedVersion, DraftRecord Draft);
}
