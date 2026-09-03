using AdrCampus.Application.Identity;
using AdrCampus.Core.Discovery;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Drafts;

namespace AdrCampus.Application.Drafts;

public sealed class DraftApplicationService(
    IDraftRepository repository,
    ISharedRecordRepository sharedRecords,
    IMemberAuthority memberAuthority,
    TimeProvider timeProvider)
{
    public async Task<CreateDraftResult> CreateAsync(
        CreateDraftCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await memberAuthority.IsActiveMemberAsync(
                command.OrganizationId,
                command.AuthorId,
                cancellationToken).ConfigureAwait(false))
        {
            return CreateDraftResult.Unauthorized();
        }

        DraftContent content;
        try
        {
            content = new DraftContent(
                command.Title,
                command.Context,
                command.Decision,
                command.Consequences);
        }
        catch (DraftValidationException exception)
        {
            return CreateDraftResult.Invalid(exception.Code, exception.Message);
        }

        var targetValidation = await ValidateTargetAsync(command.OrganizationId, command.DraftId, command.IntendedSupersessionTargetId, cancellationToken).ConfigureAwait(false);
        if (targetValidation is not null)
        {
            return CreateDraftResult.InvalidTarget(targetValidation.Value);
        }

        var draft = AdrDraft.Create(
            command.DraftId,
            command.OrganizationId,
            command.AuthorId,
            content,
            timeProvider.GetUtcNow(),
            command.IntendedSupersessionTargetId);
        var write = await repository.CreateAsync(
            draft,
            command.OperationId,
            cancellationToken).ConfigureAwait(false);
        return write.Status switch
        {
            DraftWriteStatus.Created => CreateDraftResult.Created(write.Draft!),
            DraftWriteStatus.AlreadyApplied => CreateDraftResult.AlreadyApplied(write.Draft!),
            DraftWriteStatus.OperationMismatch => CreateDraftResult.OperationMismatch(),
            _ => CreateDraftResult.Conflict(write.Draft)
        };
    }

    public async Task<DraftListResult> ListMineAsync(
        OrganizationId organizationId,
        MemberId authorId,
        CancellationToken cancellationToken = default)
    {
        if (!await memberAuthority.IsActiveMemberAsync(
                organizationId,
                authorId,
                cancellationToken).ConfigureAwait(false))
        {
            return DraftListResult.Unauthorized();
        }

        var drafts = await repository.ListByAuthorAsync(
            organizationId,
            authorId,
            cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        return DraftListResult.Success(drafts.Where(draft => !draft.IsExpired(now)).ToArray());
    }

    public async Task<GetDraftResult> GetMineAsync(
        OrganizationId organizationId,
        MemberId authorId,
        AdrId draftId,
        CancellationToken cancellationToken = default)
    {
        if (!await memberAuthority.IsActiveMemberAsync(
                organizationId,
                authorId,
                cancellationToken).ConfigureAwait(false))
        {
            return GetDraftResult.Unauthorized();
        }

        var draft = await repository.GetByAuthorAsync(
            organizationId,
            authorId,
            draftId,
            cancellationToken).ConfigureAwait(false);
        return draft is null || draft.IsExpired(timeProvider.GetUtcNow()) ? GetDraftResult.NotFound() : GetDraftResult.Success(draft);
    }

    public async Task<ReviseDraftResult> ReviseAsync(
        ReviseDraftCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await memberAuthority.IsActiveMemberAsync(command.OrganizationId, command.AuthorId, cancellationToken).ConfigureAwait(false))
        {
            return ReviseDraftResult.Unauthorized();
        }
        var current = await repository.GetByAuthorAsync(command.OrganizationId, command.AuthorId, command.DraftId, cancellationToken).ConfigureAwait(false);
        if (current is null || current.IsExpired(timeProvider.GetUtcNow())) return ReviseDraftResult.NotFound();
        if (current.Version != command.ExpectedVersion) return ReviseDraftResult.Conflict(current);
        DraftContent content;
        try { content = new DraftContent(command.Title, command.Context, command.Decision, command.Consequences); }
        catch (DraftValidationException exception) { return ReviseDraftResult.Invalid(exception.Code, exception.Message); }
        var targetValidation = await ValidateTargetAsync(command.OrganizationId, command.DraftId, command.IntendedSupersessionTargetId, cancellationToken).ConfigureAwait(false);
        if (targetValidation is not null) return ReviseDraftResult.InvalidTarget(current, targetValidation.Value);
        var revised = current.Revise(content, command.ExpectedVersion, timeProvider.GetUtcNow(), command.IntendedSupersessionTargetId);
        var write = await repository.SaveRevisionAsync(revised, command.ExpectedVersion, command.OperationId, cancellationToken).ConfigureAwait(false);
        return write.Status switch
        {
            DraftWriteStatus.Saved => ReviseDraftResult.Saved(write.Draft!),
            DraftWriteStatus.AlreadyApplied => ReviseDraftResult.AlreadyApplied(write.Draft!),
            DraftWriteStatus.OperationMismatch => ReviseDraftResult.OperationMismatch(),
            _ => ReviseDraftResult.Conflict(write.Draft)
        };
    }

    public async Task<EligibleSupersessionTargetsResult> ListEligibleSupersessionTargetsAsync(
        OrganizationId organizationId,
        MemberId memberId,
        AdrId? excludingDraftId = null,
        CancellationToken cancellationToken = default)
    {
        if (!await memberAuthority.IsActiveMemberAsync(organizationId, memberId, cancellationToken).ConfigureAwait(false))
        {
            return EligibleSupersessionTargetsResult.Unauthorized();
        }

        var targets = (await sharedRecords.ListSharedAsync(organizationId, cancellationToken).ConfigureAwait(false))
            .Where(record => record.OrganizationId == organizationId && record.Status == AdrLifecycleStatus.Accepted && record.Id != excludingDraftId)
            .OrderBy(record => record.Content.Title.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.Id.Value)
            .Select(record => new EligibleSupersessionTarget(record.Id, record.Content.Title))
            .ToArray();
        return EligibleSupersessionTargetsResult.Success(targets);
    }

    private async Task<SupersessionTargetValidationCode?> ValidateTargetAsync(
        OrganizationId organizationId,
        AdrId draftId,
        AdrId? targetId,
        CancellationToken cancellationToken)
    {
        if (targetId is null) return null;
        if (targetId == draftId) return SupersessionTargetValidationCode.SelfReference;
        var target = await sharedRecords.GetSharedAsync(organizationId, targetId.Value, cancellationToken).ConfigureAwait(false);
        return target is not null && target.OrganizationId == organizationId && target.Status == AdrLifecycleStatus.Accepted
            ? null
            : SupersessionTargetValidationCode.NotEligible;
    }
}

public sealed record CreateDraftCommand(
    AdrId DraftId,
    OperationId OperationId,
    OrganizationId OrganizationId,
    MemberId AuthorId,
    string Title,
    string? Context,
    string? Decision,
    string? Consequences,
    AdrId? IntendedSupersessionTargetId = null);

public enum SupersessionTargetValidationCode { NotEligible, SelfReference }

public enum CreateDraftStatus
{
    Created,
    AlreadyApplied,
    Invalid,
    Unauthorized,
    Conflict,
    OperationMismatch
}

public sealed record CreateDraftResult(
    CreateDraftStatus Status,
    AdrDraft? Draft,
    DraftValidationCode? ValidationCode,
    string? ErrorMessage,
    SupersessionTargetValidationCode? TargetValidationCode = null)
{
    public bool IsSuccess => Status is CreateDraftStatus.Created or CreateDraftStatus.AlreadyApplied;
    public static CreateDraftResult Created(AdrDraft draft) => new(CreateDraftStatus.Created, draft, null, null);
    public static CreateDraftResult AlreadyApplied(AdrDraft draft) => new(CreateDraftStatus.AlreadyApplied, draft, null, null);
    public static CreateDraftResult Invalid(DraftValidationCode code, string message) => new(CreateDraftStatus.Invalid, null, code, message);
    public static CreateDraftResult InvalidTarget(SupersessionTargetValidationCode code) => new(CreateDraftStatus.Invalid, null, null, TargetError(code), code);
    public static CreateDraftResult Unauthorized() => new(CreateDraftStatus.Unauthorized, null, null, "Current membership could not be established.");
    public static CreateDraftResult Conflict(AdrDraft? draft) => new(CreateDraftStatus.Conflict, draft, null, "A draft with this identifier already exists.");
    public static CreateDraftResult OperationMismatch() => new(CreateDraftStatus.OperationMismatch, null, null, "This operation identifier was already used for different work.");
    private static string TargetError(SupersessionTargetValidationCode code) => code == SupersessionTargetValidationCode.SelfReference ? "An ADR cannot replace itself." : "Only a currently accepted decision in this organization can be replaced.";
}

public sealed record DraftListResult(bool IsAuthorized, IReadOnlyList<DraftSummary> Drafts)
{
    public static DraftListResult Success(IReadOnlyList<DraftSummary> drafts) => new(true, drafts);
    public static DraftListResult Unauthorized() => new(false, Array.Empty<DraftSummary>());
}

public enum GetDraftStatus
{
    Success,
    NotFound,
    Unauthorized
}

public sealed record GetDraftResult(GetDraftStatus Status, AdrDraft? Draft)
{
    public static GetDraftResult Success(AdrDraft draft) => new(GetDraftStatus.Success, draft);
    public static GetDraftResult NotFound() => new(GetDraftStatus.NotFound, null);
    public static GetDraftResult Unauthorized() => new(GetDraftStatus.Unauthorized, null);
}

public sealed record ReviseDraftCommand(AdrId DraftId, OperationId OperationId, OrganizationId OrganizationId, MemberId AuthorId, long ExpectedVersion, string Title, string? Context, string? Decision, string? Consequences, AdrId? IntendedSupersessionTargetId = null);
public enum ReviseDraftStatus { Saved, AlreadyApplied, Invalid, Unauthorized, NotFound, Conflict, OperationMismatch }
public sealed record ReviseDraftResult(ReviseDraftStatus Status, AdrDraft? Draft, DraftValidationCode? ValidationCode, string? ErrorMessage, SupersessionTargetValidationCode? TargetValidationCode = null)
{
    public bool IsSuccess => Status is ReviseDraftStatus.Saved or ReviseDraftStatus.AlreadyApplied;
    public static ReviseDraftResult Saved(AdrDraft draft) => new(ReviseDraftStatus.Saved, draft, null, null);
    public static ReviseDraftResult AlreadyApplied(AdrDraft draft) => new(ReviseDraftStatus.AlreadyApplied, draft, null, null);
    public static ReviseDraftResult Invalid(DraftValidationCode code, string message) => new(ReviseDraftStatus.Invalid, null, code, message);
    public static ReviseDraftResult InvalidTarget(AdrDraft current, SupersessionTargetValidationCode code) => new(ReviseDraftStatus.Invalid, current, null, code == SupersessionTargetValidationCode.SelfReference ? "An ADR cannot replace itself." : "Only a currently accepted decision in this organization can be replaced. The last saved target was preserved.", code);
    public static ReviseDraftResult Unauthorized() => new(ReviseDraftStatus.Unauthorized, null, null, "Current membership could not be established.");
    public static ReviseDraftResult NotFound() => new(ReviseDraftStatus.NotFound, null, null, "The draft was not found.");
    public static ReviseDraftResult Conflict(AdrDraft? current) => new(ReviseDraftStatus.Conflict, current, null, "This draft changed after you opened it. Your entered content has been preserved; reload before saving again.");
    public static ReviseDraftResult OperationMismatch() => new(ReviseDraftStatus.OperationMismatch, null, null, "This operation identifier was already used for different work.");
}

public sealed record EligibleSupersessionTarget(AdrId Id, DraftTitle Title);
public sealed record EligibleSupersessionTargetsResult(bool IsAuthorized, IReadOnlyList<EligibleSupersessionTarget> Targets)
{
    public static EligibleSupersessionTargetsResult Success(IReadOnlyList<EligibleSupersessionTarget> targets) => new(true, targets);
    public static EligibleSupersessionTargetsResult Unauthorized() => new(false, []);
}
