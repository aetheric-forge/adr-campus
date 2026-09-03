using System.Text.Json;
using AdrCampus.Core.Administration;
using AdrCampus.Core.Domain;
using AethericForge.Runtime.Abstractions.Interfaces.Staging.Primitives;
using AethericForge.Runtime.Abstractions.Interfaces.Staging.Providers;
using AethericForge.Runtime.Models.Staging;

namespace AdrCampus.Providers.Drafts.Workbench;

public sealed class WorkbenchOrganizationAdministrationRepository(IStagingProvider staging) : IOrganizationAdministrationRepository
{
    private const string CatalogKey = "adr-campus/administration/catalog-v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private IStagingReference Reference => new StagingReference(staging.Stage, CatalogKey);

    public async Task<OrganizationAdministrationState?> GetAsync(OrganizationId organizationId, CancellationToken cancellationToken = default)
    {
        var catalog = await ReadAsync(cancellationToken);
        var record = catalog.Organizations.FirstOrDefault(value => value.OrganizationId == organizationId.Value);
        return record is null ? null : ToDomain(record);
    }

    public Task<OrganizationAdministrationWriteResult> BootstrapAsync(OrganizationAdministrationState state, AdministrationEvent administrationEvent, OperationId operationId, CancellationToken cancellationToken = default) =>
        WriteAsync(operationId, OperationKind.Bootstrap, state, null, administrationEvent, catalog =>
        {
            var current = catalog.Organizations.FirstOrDefault(value => value.OrganizationId == state.OrganizationId.Value);
            if (current is not null)
            {
                var domain = ToDomain(current);
                return domain.HasSameAuthorityConfiguration(state.SsoAuthority, state.MemberGroupReference, state.MaintainerGroupReference)
                    ? new(OrganizationAdministrationWriteStatus.AlreadyApplied, domain)
                    : new(OrganizationAdministrationWriteStatus.ConfigurationMismatch, domain);
            }
            catalog.Organizations.Add(FromDomain(state));
            catalog.Events.Add(FromDomain(administrationEvent));
            return new(OrganizationAdministrationWriteStatus.Created, state);
        }, cancellationToken);

    public Task<OrganizationAdministrationWriteResult> RenameAsync(OrganizationAdministrationState state, long expectedVersion, AdministrationEvent administrationEvent, OperationId operationId, CancellationToken cancellationToken = default) =>
        WriteAsync(operationId, OperationKind.Rename, state, expectedVersion, administrationEvent, catalog =>
        {
            var index = catalog.Organizations.FindIndex(value => value.OrganizationId == state.OrganizationId.Value);
            if (index < 0) return new(OrganizationAdministrationWriteStatus.Conflict, null);
            var current = ToDomain(catalog.Organizations[index]);
            if (current.Version != expectedVersion || state.Version != expectedVersion + 1 ||
                !current.HasSameAuthorityConfiguration(state.SsoAuthority, state.MemberGroupReference, state.MaintainerGroupReference))
                return new(OrganizationAdministrationWriteStatus.Conflict, current);
            catalog.Organizations[index] = FromDomain(state);
            catalog.Events.Add(FromDomain(administrationEvent));
            return new(OrganizationAdministrationWriteStatus.Saved, state);
        }, cancellationToken);

    public async Task<IReadOnlyList<AdministrationEvent>> ListEventsAsync(OrganizationId organizationId, CancellationToken cancellationToken = default)
    {
        var catalog = await ReadAsync(cancellationToken);
        return catalog.Events.Where(value => value.OrganizationId == organizationId.Value)
            .OrderBy(value => value.OccurredAtUtc).ThenBy(value => value.Id).Select(ToDomain).ToArray();
    }

    private async Task<OrganizationAdministrationWriteResult> WriteAsync(OperationId operationId, OperationKind kind, OrganizationAdministrationState requestedState, long? expectedVersion, AdministrationEvent administrationEvent, Func<Catalog, OrganizationAdministrationWriteResult> apply, CancellationToken cancellationToken)
    {
        await using var handle = await staging.AcquireLockAsync(Reference, TimeSpan.FromMinutes(1), cancellationToken);
        if (!handle.IsAcquired) throw new InvalidOperationException("Organization administration is busy. Retry the operation.");
        var catalog = await ReadAsync(cancellationToken);
        var requested = new OperationRecord(operationId.Value, kind, FromDomain(requestedState), expectedVersion, administrationEvent.ActorId?.Value);
        var prior = catalog.Operations.FirstOrDefault(value => value.Id == operationId.Value);
        if (prior is not null)
            return SameRequest(prior, requested)
                ? new(OrganizationAdministrationWriteStatus.AlreadyApplied, ToDomain(prior.State))
                : new(OrganizationAdministrationWriteStatus.OperationMismatch, null);
        var result = apply(catalog);
        if (result.Status is OrganizationAdministrationWriteStatus.Created or OrganizationAdministrationWriteStatus.Saved)
        {
            catalog.Operations.Add(requested);
            await SaveAsync(catalog, cancellationToken);
        }
        return result;
    }

    private static bool SameRequest(OperationRecord left, OperationRecord right) =>
        left.Kind == right.Kind && left.ExpectedVersion == right.ExpectedVersion &&
        left.State.OrganizationId == right.State.OrganizationId &&
        left.State.DisplayName == right.State.DisplayName &&
        left.State.SsoAuthority == right.State.SsoAuthority &&
        left.State.MemberGroupReference == right.State.MemberGroupReference &&
        left.State.MaintainerGroupReference == right.State.MaintainerGroupReference &&
        left.State.Version == right.State.Version && left.ActorId == right.ActorId;

    private async Task<Catalog> ReadAsync(CancellationToken cancellationToken)
    {
        if (!await staging.ExistsAsync(Reference, cancellationToken)) return new();
        await using var stream = await staging.OpenReadAsync(Reference, cancellationToken);
        return await JsonSerializer.DeserializeAsync<Catalog>(stream, Json, cancellationToken) ?? new();
    }

    private async Task SaveAsync(Catalog catalog, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, catalog, Json, cancellationToken); stream.Position = 0;
        await staging.PutAsync(CatalogKey, stream, new StagingMetadata(contentType: "application/json", lastModifiedUtc: DateTimeOffset.UtcNow), cancellationToken);
    }

    private static OrganizationRecord FromDomain(OrganizationAdministrationState value) => new(value.OrganizationId.Value, value.DisplayName.Value, value.SsoAuthority, value.MemberGroupReference, value.MaintainerGroupReference, value.InitializedAtUtc, value.ModifiedAtUtc, value.Version);
    private static OrganizationAdministrationState ToDomain(OrganizationRecord value) => OrganizationAdministrationState.Restore(new(value.OrganizationId), new(value.DisplayName), value.SsoAuthority, value.MemberGroupReference, value.MaintainerGroupReference, value.InitializedAtUtc, value.ModifiedAtUtc, value.Version);
    private static EventRecord FromDomain(AdministrationEvent value) => new(value.Id, value.OrganizationId.Value, value.Type, value.OccurredAtUtc, value.Source, value.ActorId?.Value, value.PreviousValue, value.NewValue);
    private static AdministrationEvent ToDomain(EventRecord value) => new(value.Id, new(value.OrganizationId), value.Type, value.OccurredAtUtc, value.Source, value.ActorId is null ? null : new(value.ActorId), value.PreviousValue, value.NewValue);

    public sealed class Catalog
    {
        public List<OrganizationRecord> Organizations { get; set; } = [];
        public List<EventRecord> Events { get; set; } = [];
        public List<OperationRecord> Operations { get; set; } = [];
    }
    public sealed record OrganizationRecord(string OrganizationId, string DisplayName, string SsoAuthority, string MemberGroupReference, string MaintainerGroupReference, DateTimeOffset InitializedAtUtc, DateTimeOffset ModifiedAtUtc, long Version);
    public sealed record EventRecord(Guid Id, string OrganizationId, AdministrationEventType Type, DateTimeOffset OccurredAtUtc, string Source, string? ActorId, string? PreviousValue, string? NewValue);
    public sealed record OperationRecord(Guid Id, OperationKind Kind, OrganizationRecord State, long? ExpectedVersion, string? ActorId = null);
    public enum OperationKind { Bootstrap, Rename }
}
