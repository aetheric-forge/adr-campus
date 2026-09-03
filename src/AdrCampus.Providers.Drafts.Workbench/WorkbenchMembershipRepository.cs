using System.Text.Json;
using AdrCampus.Core.Administration;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Membership;
using AethericForge.Runtime.Abstractions.Interfaces.Staging.Primitives;
using AethericForge.Runtime.Abstractions.Interfaces.Staging.Providers;
using AethericForge.Runtime.Models.Staging;

namespace AdrCampus.Providers.Drafts.Workbench;

public sealed class WorkbenchMembershipRepository(IStagingProvider staging) : IMembershipRepository
{
    private const string CatalogKey = "adr-campus/administration/membership-v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private IStagingReference Reference => new StagingReference(staging.Stage, CatalogKey);

    public async Task<IReadOnlyList<MembershipProjection>> ListAsync(OrganizationId organizationId, CancellationToken cancellationToken = default)
    {
        var catalog = await ReadAsync(cancellationToken);
        return catalog.Members.Where(value => value.OrganizationId == organizationId.Value).Select(ToDomain).ToArray();
    }

    public async Task<MembershipWriteResult> ApplyAsync(MembershipProjection next, long? expectedVersion, AdministrationEvent administrationEvent, CancellationToken cancellationToken = default)
    {
        await using var handle = await staging.AcquireLockAsync(Reference, TimeSpan.FromMinutes(1), cancellationToken);
        if (!handle.IsAcquired) throw new InvalidOperationException("Membership observation is busy. Retry the operation.");
        var catalog = await ReadAsync(cancellationToken);
        var index = catalog.Members.FindIndex(value => value.OrganizationId == next.OrganizationId.Value && value.MemberId == next.MemberId.Value);
        var current = index < 0 ? null : ToDomain(catalog.Members[index]);

        if (current is not null && current.Version == expectedVersion)
        {
            if (index < 0) catalog.Members.Add(FromDomain(next)); else catalog.Members[index] = FromDomain(next);
            catalog.Events.Add(FromDomain(administrationEvent));
            await SaveAsync(catalog, cancellationToken);
            return new(MembershipWriteStatus.Applied, next);
        }
        if (current is null && expectedVersion is null)
        {
            catalog.Members.Add(FromDomain(next));
            catalog.Events.Add(FromDomain(administrationEvent));
            await SaveAsync(catalog, cancellationToken);
            return new(MembershipWriteStatus.Applied, next);
        }
        if (current is not null && current.Version == next.Version && current.HasSameObservedState(next.Role, next.DisplayName))
        {
            return new(MembershipWriteStatus.AlreadyApplied, current);
        }
        return new(MembershipWriteStatus.Conflict, current);
    }

    public async Task<IReadOnlyList<AdministrationEvent>> ListEventsAsync(OrganizationId organizationId, CancellationToken cancellationToken = default)
    {
        var catalog = await ReadAsync(cancellationToken);
        return catalog.Events.Where(value => value.OrganizationId == organizationId.Value)
            .OrderBy(value => value.OccurredAtUtc).ThenBy(value => value.Id).Select(ToDomain).ToArray();
    }

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

    private static MemberRecord FromDomain(MembershipProjection value) => new(value.OrganizationId.Value, value.MemberId.Value, value.Role, value.DisplayName, value.FirstObservedAtUtc, value.LastObservedAtUtc, value.Version);
    private static MembershipProjection ToDomain(MemberRecord value) => MembershipProjection.Restore(new(value.OrganizationId), new(value.MemberId), value.Role, value.DisplayName, value.FirstObservedAtUtc, value.LastObservedAtUtc, value.Version);
    private static EventRecord FromDomain(AdministrationEvent value) => new(value.Id, value.OrganizationId.Value, value.Type, value.OccurredAtUtc, value.Source, value.ActorId?.Value, value.PreviousValue, value.NewValue, value.SubjectId?.Value);
    private static AdministrationEvent ToDomain(EventRecord value) => new(value.Id, new(value.OrganizationId), value.Type, value.OccurredAtUtc, value.Source, value.ActorId is null ? null : new(value.ActorId), value.PreviousValue, value.NewValue, value.SubjectId is null ? null : new(value.SubjectId));

    public sealed class Catalog
    {
        public List<MemberRecord> Members { get; set; } = [];
        public List<EventRecord> Events { get; set; } = [];
    }
    public sealed record MemberRecord(string OrganizationId, string MemberId, MemberRole Role, string DisplayName, DateTimeOffset FirstObservedAtUtc, DateTimeOffset LastObservedAtUtc, long Version);
    public sealed record EventRecord(Guid Id, string OrganizationId, AdministrationEventType Type, DateTimeOffset OccurredAtUtc, string Source, string? ActorId, string? PreviousValue, string? NewValue, string? SubjectId);
}
