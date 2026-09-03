using AdrCampus.Application.Identity;
using AdrCampus.Core.Discovery;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Drafts;
using AdrCampus.Core.Proposals;
namespace AdrCampus.Application.Proposals;
public sealed class ProposalApplicationService(IDraftRepository drafts, IProposalRepository proposals, ISharedRecordRepository sharedRecords, IMemberAuthority members, TimeProvider clock)
{
    public async Task<PrepareProposalResult> PrepareAsync(OrganizationId organizationId, MemberId authorId, AdrId draftId, CancellationToken cancellationToken = default)
    {
        if (!await members.IsActiveMemberAsync(organizationId, authorId, cancellationToken).ConfigureAwait(false)) return PrepareProposalResult.Unauthorized();
        var draft = await drafts.GetByAuthorAsync(organizationId, authorId, draftId, cancellationToken).ConfigureAwait(false);
        if (draft is null || draft.IsExpired(clock.GetUtcNow())) return PrepareProposalResult.NotFound();
        var validation = ProposalValidator.Validate(draft.Content);
        if (!validation.IsValid) return PrepareProposalResult.Invalid(draft, validation.Errors);
        var target = await ResolveTargetAsync(draft.OrganizationId, draft.IntendedSupersessionTargetId, cancellationToken).ConfigureAwait(false);
        if (draft.IntendedSupersessionTargetId is not null && target is null) return PrepareProposalResult.InvalidTarget(draft, validation.Content!);
        return PrepareProposalResult.Ready(draft, validation.Content!, target);
    }
    public async Task<ProposalCommandResult> ProposeAsync(ProposeCommand command, CancellationToken cancellationToken = default)
    {
        if (!await members.IsActiveMemberAsync(command.OrganizationId, command.AuthorId, cancellationToken).ConfigureAwait(false)) return ProposalCommandResult.Unauthorized();
        var write = await proposals.ProposeAsync(command.OrganizationId, command.AuthorId, command.DraftId, command.ExpectedDraftVersion, command.OperationId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        return new(write.Status, write.Proposal, write.Errors);
    }
    public async Task<ProposalQueryResult<AdrProposal>> GetAsync(OrganizationId organizationId, MemberId memberId, AdrId id, CancellationToken cancellationToken = default)
    {
        if (!await members.IsActiveMemberAsync(organizationId, memberId, cancellationToken).ConfigureAwait(false)) return ProposalQueryResult<AdrProposal>.Unauthorized();
        var proposal = await proposals.GetAsync(organizationId, id, cancellationToken).ConfigureAwait(false);
        return proposal is null ? ProposalQueryResult<AdrProposal>.NotFound() : ProposalQueryResult<AdrProposal>.Success(proposal);
    }
    public async Task<ProposalQueryResult<IReadOnlyList<ProposalSummary>>> ListAsync(OrganizationId organizationId, MemberId memberId, CancellationToken cancellationToken = default)
    {
        if (!await members.IsActiveMemberAsync(organizationId, memberId, cancellationToken).ConfigureAwait(false)) return ProposalQueryResult<IReadOnlyList<ProposalSummary>>.Unauthorized();
        return ProposalQueryResult<IReadOnlyList<ProposalSummary>>.Success(await proposals.ListAsync(organizationId, cancellationToken).ConfigureAwait(false));
    }
    public async Task<PrepareDecisionResult> PrepareDecisionAsync(OrganizationId organizationId, MemberId deciderId, AdrId proposalId, DecisionOutcome outcome, string note, CancellationToken cancellationToken = default)
    {
        if (!await members.IsActiveMaintainerAsync(organizationId, deciderId, cancellationToken).ConfigureAwait(false)) return PrepareDecisionResult.Unauthorized();
        var proposal = await proposals.GetAsync(organizationId, proposalId, cancellationToken).ConfigureAwait(false);
        if (proposal is null || proposal.FinalDecision is not null) return PrepareDecisionResult.NotFound();
        var validation = DecisionNoteValidator.Validate(outcome, note);
        var target = await ResolveTargetAsync(organizationId, proposal.IntendedSupersessionTargetId, cancellationToken).ConfigureAwait(false);
        return validation.IsValid ? PrepareDecisionResult.Ready(proposal, outcome, validation.Note!, target) : PrepareDecisionResult.Invalid(proposal, outcome, note, validation.Errors, target);
    }
    public async Task<DecisionCommandResult> DecideAsync(DecisionCommand command, CancellationToken cancellationToken = default)
    {
        if (!await members.IsActiveMaintainerAsync(command.OrganizationId, command.DeciderId, cancellationToken).ConfigureAwait(false)) return DecisionCommandResult.Unauthorized();
        var result = await proposals.DecideAsync(command.OrganizationId, command.ProposalId, command.ExpectedProposedAtUtc, command.DeciderId, command.Outcome, command.Note, command.OperationId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        return new(result.Status, result.Record, result.Errors);
    }
    public async Task<ProposalQueryResult<IReadOnlyList<DecidedSummary>>> ListDecidedAsync(OrganizationId organizationId, MemberId memberId, DecisionOutcome outcome, CancellationToken cancellationToken = default)
    {
        if (!await members.IsActiveMemberAsync(organizationId, memberId, cancellationToken).ConfigureAwait(false)) return ProposalQueryResult<IReadOnlyList<DecidedSummary>>.Unauthorized();
        return ProposalQueryResult<IReadOnlyList<DecidedSummary>>.Success(await proposals.ListDecidedAsync(organizationId, outcome, cancellationToken).ConfigureAwait(false));
    }

    private async Task<SupersessionTargetReference?> ResolveTargetAsync(OrganizationId organizationId, AdrId? targetId, CancellationToken cancellationToken)
    {
        if (targetId is null) return null;
        var target = await sharedRecords.GetSharedAsync(organizationId, targetId.Value, cancellationToken).ConfigureAwait(false);
        return target is null || target.OrganizationId != organizationId ? null : new(target.Id, target.Content.Title, target.Status);
    }
}
public sealed record ProposeCommand(OrganizationId OrganizationId, MemberId AuthorId, AdrId DraftId, long ExpectedDraftVersion, OperationId OperationId);
public sealed record SupersessionTargetReference(AdrId Id, DraftTitle Title, AdrLifecycleStatus Status);
public sealed record PrepareProposalResult(bool IsAuthorized, bool IsFound, AdrDraft? Draft, ProposalContent? Content, IReadOnlyList<ProposalValidationError> Errors, SupersessionTargetReference? Target = null)
{
    public bool IsReady => Content is not null && Errors.Count == 0;
    public static PrepareProposalResult Ready(AdrDraft draft, ProposalContent content, SupersessionTargetReference? target) => new(true, true, draft, content, [], target);
    public static PrepareProposalResult Invalid(AdrDraft draft, IReadOnlyList<ProposalValidationError> errors) => new(true, true, draft, null, errors);
    public static PrepareProposalResult InvalidTarget(AdrDraft draft, ProposalContent content) => new(true, true, draft, content, [new("Replacement target", ProposalValidationCode.TargetNotEligible, "The intended target is no longer an accepted decision. Remove it or select a currently accepted decision before proposing.")]);
    public static PrepareProposalResult Unauthorized() => new(false, false, null, null, []);
    public static PrepareProposalResult NotFound() => new(true, false, null, null, []);
}
public sealed record ProposalCommandResult(ProposalWriteStatus Status, AdrProposal? Proposal, IReadOnlyList<ProposalValidationError> Errors)
{
    public bool IsSuccess => Status is ProposalWriteStatus.Proposed or ProposalWriteStatus.AlreadyApplied;
    public static ProposalCommandResult Unauthorized() => new(ProposalWriteStatus.UnauthorizedOrNotFound, null, []);
}
public sealed record ProposalQueryResult<T>(bool IsAuthorized, bool IsFound, T? Value)
{
    public static ProposalQueryResult<T> Success(T value) => new(true, true, value);
    public static ProposalQueryResult<T> Unauthorized() => new(false, false, default);
    public static ProposalQueryResult<T> NotFound() => new(true, false, default);
}
public sealed record DecisionCommand(OrganizationId OrganizationId, AdrId ProposalId, DateTimeOffset ExpectedProposedAtUtc, MemberId DeciderId, DecisionOutcome Outcome, string Note, OperationId OperationId);
public sealed record PrepareDecisionResult(bool IsAuthorized, bool IsFound, AdrProposal? Proposal, DecisionOutcome? Outcome, string? Note, IReadOnlyList<DecisionNoteValidationError> Errors, SupersessionTargetReference? Target = null)
{
    public bool IsReady => Proposal is not null && Outcome is not null && Note is not null && Errors.Count == 0;
    public static PrepareDecisionResult Ready(AdrProposal proposal, DecisionOutcome outcome, string note, SupersessionTargetReference? target) => new(true, true, proposal, outcome, note, [], target);
    public static PrepareDecisionResult Invalid(AdrProposal proposal, DecisionOutcome outcome, string note, IReadOnlyList<DecisionNoteValidationError> errors, SupersessionTargetReference? target) => new(true, true, proposal, outcome, note, errors, target);
    public static PrepareDecisionResult Unauthorized() => new(false, false, null, null, null, []);
    public static PrepareDecisionResult NotFound() => new(true, false, null, null, null, []);
}
public sealed record DecisionCommandResult(DecisionWriteStatus Status, AdrProposal? Record, IReadOnlyList<DecisionNoteValidationError> Errors)
{
    public bool IsSuccess => Status is DecisionWriteStatus.Decided or DecisionWriteStatus.AlreadyApplied;
    public static DecisionCommandResult Unauthorized() => new(DecisionWriteStatus.UnauthorizedOrNotFound, null, []);
}
