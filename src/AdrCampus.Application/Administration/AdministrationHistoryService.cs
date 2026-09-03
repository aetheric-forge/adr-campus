using AdrCampus.Application.Identity;
using AdrCampus.Core.Administration;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Drafts;
using AdrCampus.Core.Membership;

namespace AdrCampus.Application.Administration;

/// <summary>
/// Reads the application-owned administration history that Milestones A–D each record events into.
/// Read-only, maintainer-only. Never returns private draft content.
/// </summary>
public sealed class AdministrationHistoryService(
    IOrganizationAdministrationRepository organizationRepository,
    IMembershipRepository membershipRepository,
    IDraftRecoveryRepository recoveryRepository,
    IMemberAuthority memberAuthority,
    IMemberDisplayNameDirectory displayNames)
{
    public async Task<AdministrationHistoryResult> ListAsync(OrganizationId organizationId, MemberId maintainerId, CancellationToken cancellationToken = default)
    {
        if (!await memberAuthority.IsActiveMaintainerAsync(organizationId, maintainerId, cancellationToken).ConfigureAwait(false))
        {
            return AdministrationHistoryResult.Unauthorized();
        }

        var organizationEvents = await organizationRepository.ListEventsAsync(organizationId, cancellationToken).ConfigureAwait(false);
        var membershipEvents = await membershipRepository.ListEventsAsync(organizationId, cancellationToken).ConfigureAwait(false);
        var recoveryEvents = await recoveryRepository.ListRecoveryEventsAsync(organizationId, cancellationToken).ConfigureAwait(false);
        var events = organizationEvents.Concat(membershipEvents).Concat(recoveryEvents)
            .OrderByDescending(evt => evt.OccurredAtUtc).ThenBy(evt => evt.Id)
            .ToArray();

        var memberIds = events
            .SelectMany(evt => evt.Type == AdministrationEventType.DraftReassigned
                ? new[] { evt.ActorId, evt.SubjectId, ToMemberId(evt.PreviousValue), ToMemberId(evt.NewValue) }
                : new[] { evt.ActorId, evt.SubjectId })
            .Where(id => id is not null)
            .Select(id => id!)
            .Distinct()
            .ToArray();
        var names = await displayNames.ResolveAsync(organizationId, memberIds, cancellationToken).ConfigureAwait(false);

        var items = events.Select(evt =>
        {
            var isReassignment = evt.Type == AdministrationEventType.DraftReassigned;
            var previous = isReassignment ? names.For(ToMemberId(evt.PreviousValue)!) : evt.PreviousValue;
            var next = isReassignment ? names.For(ToMemberId(evt.NewValue)!) : evt.NewValue;
            return new AdministrationHistoryItem(
                evt.OccurredAtUtc,
                evt.Type,
                evt.Source,
                evt.ActorId is null ? null : names.For(evt.ActorId),
                evt.SubjectId is null ? null : names.For(evt.SubjectId),
                previous,
                next,
                evt.DraftId?.Value);
        }).ToArray();
        return AdministrationHistoryResult.Success(items);
    }

    private static MemberId? ToMemberId(string? value) => string.IsNullOrWhiteSpace(value) ? null : new MemberId(value);
}

public sealed record AdministrationHistoryItem(DateTimeOffset OccurredAtUtc, AdministrationEventType Type, string Source, string? ActorDisplayName, string? SubjectDisplayName, string? PreviousValue, string? NewValue, Guid? DraftId);

public sealed record AdministrationHistoryResult(bool IsAuthorized, IReadOnlyList<AdministrationHistoryItem> Items)
{
    public static AdministrationHistoryResult Success(IReadOnlyList<AdministrationHistoryItem> items) => new(true, items);
    public static AdministrationHistoryResult Unauthorized() => new(false, []);
}
