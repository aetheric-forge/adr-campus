using AdrCampus.Core.Domain;
using AdrCampus.Core.Maintenance;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Primitives;
using AethericForge.Runtime.Institutions.PostOffice;
using AethericForge.Runtime.Models.Post;

namespace AdrCampus.Providers.PostOffice;

/// <summary>
/// Custody ledger for maintenance commands, backed by the Post Office institution's Postmaster instead of
/// a raw staging blob. The whole ledger travels as the payload of a single, repeatedly-overwritten post
/// envelope (Post primitives model send/receive of individual envelopes, not a queryable store, so this
/// mirrors the previous single-blob-catalog shape on top of Accept/Collect). Postmaster exposes no
/// distributed lock, so writes are serialized with an in-process semaphore; see the sibling Archive and
/// Library providers for the same caveat.
/// </summary>
public sealed class PostOfficeMaintenanceDispatcher(IPostOffice postOffice) : IMaintenancePostOffice
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly IPostReference CatalogReference = new PostReference(
        "adr-campus-maintenance",
        "catalog",
        new PostContract("MaintenanceCatalog", "1.0.0", PostIntent.Command));

    public async Task<MaintenancePostResult> PostAsync(MaintenanceCommand command, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
            var existing = catalog.Commands.FirstOrDefault(c => c.Id == command.Id);
            if (existing is not null)
            {
                return new(MaintenancePostStatus.AlreadyAccepted, ToDomain(existing));
            }
            catalog.Commands.Add(FromDomain(command));
            await SaveAsync(catalog, cancellationToken).ConfigureAwait(false);
            return new(MaintenancePostStatus.Accepted, command);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<MaintenanceCommand?> CollectNextAsync(MaintenanceJob job, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
            var index = catalog.Commands.FindIndex(c => c.Job == job && !c.Collected);
            if (index < 0)
            {
                return null;
            }
            var collected = catalog.Commands[index] with { Collected = true };
            catalog.Commands[index] = collected;
            await SaveAsync(catalog, cancellationToken).ConfigureAwait(false);
            return ToDomain(collected);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task RecordOutcomeAsync(MaintenanceRunOutcome outcome, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
            if (catalog.Outcomes.Any(o => o.CommandId == outcome.CommandId))
            {
                return;
            }
            catalog.Outcomes.Add(new(outcome.CommandId, outcome.Status, outcome.ProcessedCount, outcome.RemainingCount, outcome.OccurredAtUtc, outcome.FailureReason));
            await SaveAsync(catalog, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<IReadOnlyList<MaintenanceRunRecord>> ListRunsAsync(OrganizationId organizationId, CancellationToken cancellationToken = default)
    {
        var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return catalog.Commands.Where(c => c.OrganizationId == organizationId.Value)
            .OrderByDescending(c => c.RequestedAtUtc)
            .Select(c => new MaintenanceRunRecord(ToDomain(c), ToDomain(catalog.Outcomes.FirstOrDefault(o => o.CommandId == c.Id)), c.Collected))
            .ToArray();
    }

    private async Task<Catalog> ReadAsync(CancellationToken cancellationToken)
    {
        var envelope = await postOffice.Postmaster.CollectAsync(CatalogReference, cancellationToken).ConfigureAwait(false);
        return envelope?.Payload as Catalog ?? new Catalog();
    }

    private async Task SaveAsync(Catalog catalog, CancellationToken cancellationToken)
    {
        var envelope = new PostEnvelope<Catalog>(CatalogReference, catalog, new PostMetadata());
        await postOffice.Postmaster.AcceptAsync(envelope, cancellationToken).ConfigureAwait(false);
    }

    private static CommandRecord FromDomain(MaintenanceCommand value) => new(value.Id, value.OrganizationId.Value, value.Job, value.RequestedAtUtc, value.Source, false);
    private static MaintenanceCommand ToDomain(CommandRecord value) => new(value.Id, new(value.OrganizationId), value.Job, value.RequestedAtUtc, value.Source);
    private static MaintenanceRunOutcome? ToDomain(OutcomeRecord? value) => value is null ? null : new(value.CommandId, value.Status, value.ProcessedCount, value.RemainingCount, value.OccurredAtUtc, value.FailureReason);

    public sealed class Catalog
    {
        public List<CommandRecord> Commands { get; set; } = [];
        public List<OutcomeRecord> Outcomes { get; set; } = [];
    }
    public sealed record CommandRecord(Guid Id, string OrganizationId, MaintenanceJob Job, DateTimeOffset RequestedAtUtc, string Source, bool Collected);
    public sealed record OutcomeRecord(Guid CommandId, MaintenanceRunStatus Status, int ProcessedCount, int RemainingCount, DateTimeOffset OccurredAtUtc, string? FailureReason);
}
