using AdrCampus.Application.Identity;
using AdrCampus.Core.Administration;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Drafts;

namespace AdrCampus.Application.Drafts;

public sealed class DraftRecoveryApplicationService(
    IDraftRepository draftRepository,
    IDraftRecoveryRepository recoveryRepository,
    IMemberAuthority memberAuthority,
    IMemberDisplayNameDirectory displayNames,
    TimeProvider timeProvider) : IDraftRecoveryCoordinator
{
    private const int RecoveryWindowDays = 30;

    public async Task StartRecoveryForDepartedMemberAsync(OrganizationId organizationId, MemberId formerMemberId, DateTimeOffset observedAtUtc, CancellationToken cancellationToken = default)
    {
        var drafts = await draftRepository.ListByAuthorAsync(organizationId, formerMemberId, cancellationToken).ConfigureAwait(false);
        var deadline = observedAtUtc.AddDays(RecoveryWindowDays);
        foreach (var draft in drafts)
        {
            var evt = new AdministrationEvent(Guid.NewGuid(), organizationId, AdministrationEventType.DraftRecoveryStarted, observedAtUtc, "SSO observation", SubjectId: formerMemberId, DraftId: draft.Id, NewValue: deadline.ToString("O"));
            await recoveryRepository.StartRecoveryAsync(organizationId, draft.Id, formerMemberId, draft.Version, deadline, evt, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task CancelRecoveryForReturningMemberAsync(OrganizationId organizationId, MemberId memberId, DateTimeOffset observedAtUtc, CancellationToken cancellationToken = default)
    {
        var drafts = await draftRepository.ListByAuthorAsync(organizationId, memberId, cancellationToken).ConfigureAwait(false);
        foreach (var draft in drafts)
        {
            var evt = new AdministrationEvent(Guid.NewGuid(), organizationId, AdministrationEventType.DraftRecoveryCancelled, observedAtUtc, "SSO observation", SubjectId: memberId, DraftId: draft.Id);
            await recoveryRepository.CancelRecoveryAsync(organizationId, draft.Id, memberId, draft.Version, evt, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<RecoveryListResult> ListEligibleAsync(OrganizationId organizationId, MemberId maintainerId, CancellationToken cancellationToken = default)
    {
        if (!await memberAuthority.IsActiveMaintainerAsync(organizationId, maintainerId, cancellationToken).ConfigureAwait(false))
        {
            return RecoveryListResult.Unauthorized();
        }

        var eligible = await recoveryRepository.ListEligibleAsync(organizationId, timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        var names = await displayNames.ResolveAsync(organizationId, eligible.Select(item => item.FormerAuthorId).Distinct().ToArray(), cancellationToken).ConfigureAwait(false);
        var items = eligible
            .Select(item => new RecoveryListItem(item.Id, item.Title, item.FormerAuthorId, names.For(item.FormerAuthorId), item.ExpiresAtUtc, item.Version))
            .OrderBy(item => item.ExpiresAtUtc).ThenBy(item => item.Id.Value)
            .ToArray();
        return RecoveryListResult.Success(items);
    }

    public async Task<DraftReassignmentResult> ReassignAsync(DraftReassignmentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await memberAuthority.IsActiveMaintainerAsync(command.OrganizationId, command.MaintainerId, cancellationToken).ConfigureAwait(false))
        {
            return DraftReassignmentResult.Unauthorized();
        }
        if (!await memberAuthority.IsActiveMemberAsync(command.OrganizationId, command.NewAuthorId, cancellationToken).ConfigureAwait(false))
        {
            return DraftReassignmentResult.RecipientNotActiveMember();
        }

        var now = timeProvider.GetUtcNow();
        var evt = new AdministrationEvent(
            Guid.NewGuid(), command.OrganizationId, AdministrationEventType.DraftReassigned, now, "ADR Campus",
            ActorId: command.MaintainerId, SubjectId: command.NewAuthorId, DraftId: command.DraftId,
            PreviousValue: command.FormerAuthorId.Value, NewValue: command.NewAuthorId.Value);
        var write = await recoveryRepository.ReassignAsync(
            command.OrganizationId, command.DraftId, command.FormerAuthorId, command.NewAuthorId,
            command.ExpectedVersion, now, evt, command.OperationId, cancellationToken).ConfigureAwait(false);
        return write.Status switch
        {
            ReassignDraftStatus.Reassigned => DraftReassignmentResult.Reassigned(write.Draft!),
            ReassignDraftStatus.AlreadyApplied => DraftReassignmentResult.AlreadyApplied(write.Draft!),
            ReassignDraftStatus.Expired => DraftReassignmentResult.Expired(),
            ReassignDraftStatus.NotFound => DraftReassignmentResult.NotFound(),
            ReassignDraftStatus.OperationMismatch => DraftReassignmentResult.OperationMismatch(),
            _ => DraftReassignmentResult.Conflict(write.Draft)
        };
    }
}

public sealed record RecoveryListItem(AdrId Id, DraftTitle Title, MemberId FormerAuthorId, string FormerAuthorDisplayName, DateTimeOffset ExpiresAtUtc, long Version);
public sealed record RecoveryListResult(bool IsAuthorized, IReadOnlyList<RecoveryListItem> Items)
{
    public static RecoveryListResult Success(IReadOnlyList<RecoveryListItem> items) => new(true, items);
    public static RecoveryListResult Unauthorized() => new(false, []);
}

public sealed record DraftReassignmentCommand(OrganizationId OrganizationId, MemberId MaintainerId, AdrId DraftId, MemberId FormerAuthorId, MemberId NewAuthorId, long ExpectedVersion, OperationId OperationId);
public enum DraftReassignmentStatus { Reassigned, AlreadyApplied, Unauthorized, RecipientNotActiveMember, NotFound, Conflict, Expired, OperationMismatch }
public sealed record DraftReassignmentResult(DraftReassignmentStatus Status, AdrDraft? Draft, string? ErrorMessage)
{
    public bool IsSuccess => Status is DraftReassignmentStatus.Reassigned or DraftReassignmentStatus.AlreadyApplied;
    public static DraftReassignmentResult Reassigned(AdrDraft draft) => new(DraftReassignmentStatus.Reassigned, draft, null);
    public static DraftReassignmentResult AlreadyApplied(AdrDraft draft) => new(DraftReassignmentStatus.AlreadyApplied, draft, null);
    public static DraftReassignmentResult Unauthorized() => new(DraftReassignmentStatus.Unauthorized, null, "Current Maintainer authority could not be established.");
    public static DraftReassignmentResult RecipientNotActiveMember() => new(DraftReassignmentStatus.RecipientNotActiveMember, null, "The selected recipient is not currently an active member.");
    public static DraftReassignmentResult NotFound() => new(DraftReassignmentStatus.NotFound, null, "The draft was not found.");
    public static DraftReassignmentResult Expired() => new(DraftReassignmentStatus.Expired, null, "This draft's recovery window has closed.");
    public static DraftReassignmentResult Conflict(AdrDraft? draft) => new(DraftReassignmentStatus.Conflict, draft, "This draft changed since the recovery list was loaded. Reload before trying again.");
    public static DraftReassignmentResult OperationMismatch() => new(DraftReassignmentStatus.OperationMismatch, null, "This operation identifier was already used for different work.");
}
