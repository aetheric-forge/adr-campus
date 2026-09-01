using System.Text.Json;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Drafts;
using AethericForge.Runtime.Abstractions.Interfaces.Staging.Primitives;
using AethericForge.Runtime.Abstractions.Interfaces.Staging.Providers;
using AethericForge.Runtime.Models.Staging;

namespace AdrCampus.Providers.Drafts.Workbench;

public sealed class WorkbenchDraftRepository(IStagingProvider staging) : IDraftRepository
{
    private const string CatalogKey = "adr-campus/drafts/catalog-v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private IStagingReference Reference => new StagingReference(staging.Stage, CatalogKey);

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
            .Select(d => new DraftSummary(new AdrId(d.Id), new DraftTitle(d.Title), d.CreatedAtUtc, d.ModifiedAtUtc, d.Version)).ToArray();
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

    private async Task<DraftWriteResult> WriteAsync(OperationId operationId, string kind, AdrDraft draft, long? expectedVersion, Func<Catalog, DraftWriteResult> apply, CancellationToken cancellationToken)
    {
        await using var handle = await staging.AcquireLockAsync(Reference, TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
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
        if (!await staging.ExistsAsync(Reference, cancellationToken).ConfigureAwait(false)) return new Catalog();
        await using var stream = await staging.OpenReadAsync(Reference, cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<Catalog>(stream, Json, cancellationToken).ConfigureAwait(false) ?? new Catalog();
    }

    private async Task SaveAsync(Catalog catalog, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(); await JsonSerializer.SerializeAsync(stream, catalog, Json, cancellationToken).ConfigureAwait(false); stream.Position = 0;
        await staging.PutAsync(CatalogKey, stream, new StagingMetadata(contentType: "application/json", lastModifiedUtc: DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
    }

    private static DraftRecord FromDomain(AdrDraft d) => new(d.Id.Value, d.OrganizationId.Value, d.AuthorId.Value, d.Content.Title.Value, d.Content.Context, d.Content.Decision, d.Content.Consequences, d.CreatedAtUtc, d.ModifiedAtUtc, d.Version);
    private static AdrDraft ToDomain(DraftRecord d) => AdrDraft.Restore(new AdrId(d.Id), new OrganizationId(d.OrganizationId), new MemberId(d.AuthorId), new DraftContent(d.Title, d.Context, d.Decision, d.Consequences), d.CreatedAtUtc, d.ModifiedAtUtc, d.Version);
    public sealed class Catalog { public List<DraftRecord> Drafts { get; set; } = []; public List<OperationRecord> Operations { get; set; } = []; }
    public sealed record DraftRecord(Guid Id, string OrganizationId, string AuthorId, string Title, string Context, string Decision, string Consequences, DateTimeOffset CreatedAtUtc, DateTimeOffset ModifiedAtUtc, long Version);
    public sealed record OperationRecord(Guid Id, string Kind, DraftRecord Draft, long? ExpectedVersion);
}
