using System.Collections.Concurrent;
using AdrCampus.Core.Administration;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Membership;
using AethericForge.Runtime.Abstractions.Interfaces.Archive.Services;
using AethericForge.Runtime.Models.Archive.Primitives;

namespace AdrCampus.Providers.Archive;

/// <summary>
/// Stores membership observations and their event history as a single Archive object via the Archive
/// institution's typed <see cref="AethericForge.Runtime.Abstractions.Interfaces.Archive.Services.IArchivist"/>.
/// See <see cref="ArchiveOrganizationAdministrationRepository"/> for the locking caveat shared by both
/// repositories in this project.
/// </summary>
public sealed class ArchiveMembershipRepository(IArchivist archivist) : IMembershipRepository
{
    private const string Store = "adr-campus";
    private const string Key = "administration/membership-v1";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);
    private static readonly ArchiveReference Reference = new(Store, Key);

    public async Task<IReadOnlyList<MembershipProjection>> ListAsync(OrganizationId organizationId, CancellationToken cancellationToken = default)
    {
        var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return catalog.Members.Where(value => value.OrganizationId == organizationId.Value).Select(ToDomain).ToArray();
    }

    public async Task<MembershipWriteResult> ApplyAsync(MembershipProjection next, long? expectedVersion, AdministrationEvent administrationEvent, CancellationToken cancellationToken = default)
    {
        var gate = GetLock();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
            var index = catalog.Members.FindIndex(value => value.OrganizationId == next.OrganizationId.Value && value.MemberId == next.MemberId.Value);
            var current = index < 0 ? null : ToDomain(catalog.Members[index]);

            if (current is not null && current.Version == expectedVersion)
            {
                if (index < 0) catalog.Members.Add(FromDomain(next)); else catalog.Members[index] = FromDomain(next);
                catalog.Events.Add(FromDomain(administrationEvent));
                await SaveAsync(catalog, cancellationToken).ConfigureAwait(false);
                return new(MembershipWriteStatus.Applied, next);
            }
            if (current is null && expectedVersion is null)
            {
                catalog.Members.Add(FromDomain(next));
                catalog.Events.Add(FromDomain(administrationEvent));
                await SaveAsync(catalog, cancellationToken).ConfigureAwait(false);
                return new(MembershipWriteStatus.Applied, next);
            }
            if (current is not null && current.Version == next.Version && current.HasSameObservedState(next.Role, next.DisplayName))
            {
                return new(MembershipWriteStatus.AlreadyApplied, current);
            }
            return new(MembershipWriteStatus.Conflict, current);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<AdministrationEvent>> ListEventsAsync(OrganizationId organizationId, CancellationToken cancellationToken = default)
    {
        var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return catalog.Events.Where(value => value.OrganizationId == organizationId.Value)
            .OrderBy(value => value.OccurredAtUtc).ThenBy(value => value.Id).Select(ToDomain).ToArray();
    }

    private static SemaphoreSlim GetLock() => Locks.GetOrAdd($"{Store}/{Key}", static _ => new SemaphoreSlim(1, 1));

    private async Task<Catalog> ReadAsync(CancellationToken cancellationToken) =>
        await archivist.GetAsync<Catalog>(Reference, cancellationToken).ConfigureAwait(false) ?? new Catalog();

    private async Task SaveAsync(Catalog catalog, CancellationToken cancellationToken) =>
        await archivist.PutAsync(Store, Key, catalog, "application/json", cancellationToken).ConfigureAwait(false);

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
