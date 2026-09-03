using System.Text.Json;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Maintenance;
using AethericForge.Runtime.Abstractions.Interfaces.Staging.Primitives;
using AethericForge.Runtime.Abstractions.Interfaces.Staging.Providers;
using AethericForge.Runtime.Models.Staging;

namespace AdrCampus.Providers.Drafts.Workbench;

public sealed class WorkbenchMaintenancePostOffice(IStagingProvider staging) : IMaintenancePostOffice
{
    private const string CatalogKey = "adr-campus/maintenance/catalog-v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private IStagingReference Reference => new StagingReference(staging.Stage, CatalogKey);

    public async Task<MaintenancePostResult> PostAsync(MaintenanceCommand command, CancellationToken cancellationToken = default)
    {
        await using var handle = await staging.AcquireLockAsync(Reference, TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
        if (!handle.IsAcquired) throw new InvalidOperationException("The maintenance Post Office is busy. Retry the operation.");
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

    public async Task<MaintenanceCommand?> CollectNextAsync(MaintenanceJob job, CancellationToken cancellationToken = default)
    {
        await using var handle = await staging.AcquireLockAsync(Reference, TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
        if (!handle.IsAcquired) throw new InvalidOperationException("The maintenance Post Office is busy. Retry the operation.");
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

    public async Task RecordOutcomeAsync(MaintenanceRunOutcome outcome, CancellationToken cancellationToken = default)
    {
        await using var handle = await staging.AcquireLockAsync(Reference, TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
        if (!handle.IsAcquired) throw new InvalidOperationException("The maintenance Post Office is busy. Retry the operation.");
        var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
        if (catalog.Outcomes.Any(o => o.CommandId == outcome.CommandId))
        {
            return;
        }
        catalog.Outcomes.Add(new(outcome.CommandId, outcome.Status, outcome.ProcessedCount, outcome.RemainingCount, outcome.OccurredAtUtc, outcome.FailureReason));
        await SaveAsync(catalog, cancellationToken).ConfigureAwait(false);
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
        if (!await staging.ExistsAsync(Reference, cancellationToken).ConfigureAwait(false)) return new();
        await using var stream = await staging.OpenReadAsync(Reference, cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<Catalog>(stream, Json, cancellationToken).ConfigureAwait(false) ?? new();
    }

    private async Task SaveAsync(Catalog catalog, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, catalog, Json, cancellationToken).ConfigureAwait(false); stream.Position = 0;
        await staging.PutAsync(CatalogKey, stream, new StagingMetadata(contentType: "application/json", lastModifiedUtc: DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
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
