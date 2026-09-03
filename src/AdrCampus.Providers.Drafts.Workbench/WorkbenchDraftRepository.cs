using System.Text.Json;
using AdrCampus.Core.Administration;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Discovery;
using AdrCampus.Core.Drafts;
using AdrCampus.Core.Proposals;
using AethericForge.Runtime.Abstractions.Interfaces.Staging.Primitives;
using AethericForge.Runtime.Abstractions.Interfaces.Staging.Providers;
using AethericForge.Runtime.Models.Staging;

namespace AdrCampus.Providers.Drafts.Workbench;

public sealed class WorkbenchDraftRepository(IStagingProvider staging) : IDraftRepository, IProposalRepository, ISharedRecordRepository, IDraftRecoveryRepository
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
            .Select(d => new DraftSummary(new AdrId(d.Id), new DraftTitle(d.Title), d.CreatedAtUtc, d.ModifiedAtUtc, d.Version, ToAdrId(d.IntendedSupersessionTargetId))).ToArray();
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

    public async Task<ProposalWriteResult> ProposeAsync(OrganizationId organizationId, MemberId authorId, AdrId draftId, long expectedDraftVersion, OperationId operationId, DateTimeOffset proposedAtUtc, CancellationToken cancellationToken = default)
    {
        await using var handle = await staging.AcquireLockAsync(Reference, TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
        if (!handle.IsAcquired) throw new InvalidOperationException("The ADR catalog is busy. Retry the operation.");
        var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
        var prior = catalog.ProposalOperations.FirstOrDefault(o => o.Id == operationId.Value);
        if (prior is not null)
        {
            var same = prior.OrganizationId == organizationId.Value && prior.AuthorId == authorId.Value && prior.DraftId == draftId.Value && prior.ExpectedVersion == expectedDraftVersion;
            return same ? new(ProposalWriteStatus.AlreadyApplied, ToDomain(prior.Proposal), []) : new(ProposalWriteStatus.OperationMismatch, null, []);
        }
        var draftIndex = catalog.Drafts.FindIndex(d => d.OrganizationId == organizationId.Value && d.AuthorId == authorId.Value && d.Id == draftId.Value);
        if (draftIndex < 0) return new(ProposalWriteStatus.UnauthorizedOrNotFound, null, []);
        var draft = ToDomain(catalog.Drafts[draftIndex]);
        if (draft.Version != expectedDraftVersion) return new(ProposalWriteStatus.Conflict, null, []);
        var validation = ProposalValidator.Validate(draft.Content);
        if (!validation.IsValid) return new(ProposalWriteStatus.Invalid, null, validation.Errors);
        if (draft.IntendedSupersessionTargetId is not null)
        {
            var target = catalog.Proposals.FirstOrDefault(record => record.OrganizationId == organizationId.Value && record.Id == draft.IntendedSupersessionTargetId.Value.Value);
            if (target is null || ToDomain(target).Status != AdrLifecycleStatus.Accepted)
                return new(ProposalWriteStatus.TargetNotEligible, null, [new("Replacement target", ProposalValidationCode.TargetNotEligible, "The intended target is no longer an accepted decision. Return to the draft and select another target or remove it.")]);
        }
        var proposal = new AdrProposal(draft.Id, draft.OrganizationId, draft.AuthorId, authorId, validation.Content!, draft.CreatedAtUtc, proposedAtUtc, draft.Version, IntendedSupersessionTargetId: draft.IntendedSupersessionTargetId);
        var record = FromDomain(proposal);
        catalog.Drafts.RemoveAt(draftIndex);
        catalog.Proposals.Add(record);
        catalog.ProposalOperations.Add(new(operationId.Value, organizationId.Value, authorId.Value, draftId.Value, expectedDraftVersion, record));
        await SaveAsync(catalog, cancellationToken).ConfigureAwait(false);
        return new(ProposalWriteStatus.Proposed, proposal, []);
    }

    public async Task<AdrProposal?> GetAsync(OrganizationId organizationId, AdrId id, CancellationToken cancellationToken = default)
    {
        var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
        var record = catalog.Proposals.FirstOrDefault(p => p.OrganizationId == organizationId.Value && p.Id == id.Value);
        return record is null ? null : ToDomain(record);
    }

    public async Task<IReadOnlyList<ProposalSummary>> ListAsync(OrganizationId organizationId, CancellationToken cancellationToken = default)
    {
        var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return catalog.Proposals.Where(p => p.OrganizationId == organizationId.Value && p.FinalDecision is null).OrderByDescending(p => p.ProposedAtUtc).ThenBy(p => p.Id)
            .Select(p => new ProposalSummary(new AdrId(p.Id), new DraftTitle(p.Title), new MemberId(p.AuthorId), new MemberId(p.ProposerId), p.ProposedAtUtc)).ToArray();
    }

    public async Task<DecisionWriteResult> DecideAsync(OrganizationId organizationId, AdrId proposalId, DateTimeOffset expectedProposedAtUtc, MemberId deciderId, DecisionOutcome outcome, string note, OperationId operationId, DateTimeOffset decidedAtUtc, CancellationToken cancellationToken = default)
    {
        await using var handle = await staging.AcquireLockAsync(Reference, TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
        if (!handle.IsAcquired) throw new InvalidOperationException("The ADR catalog is busy. Retry the operation.");
        var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
        var prior = catalog.DecisionOperations.FirstOrDefault(o => o.Id == operationId.Value);
        if (prior is not null)
        {
            var same = prior.OrganizationId == organizationId.Value && prior.ProposalId == proposalId.Value && prior.ExpectedProposedAtUtc == expectedProposedAtUtc && prior.DeciderId == deciderId.Value && prior.Outcome == outcome && prior.Note == note;
            return same ? new(DecisionWriteStatus.AlreadyApplied, ToDomain(prior.Record), []) : new(DecisionWriteStatus.OperationMismatch, null, []);
        }
        var validation = DecisionNoteValidator.Validate(outcome, note);
        if (!validation.IsValid) return new(DecisionWriteStatus.Invalid, null, validation.Errors);
        var index = catalog.Proposals.FindIndex(p => p.OrganizationId == organizationId.Value && p.Id == proposalId.Value);
        if (index < 0) return new(DecisionWriteStatus.UnauthorizedOrNotFound, null, []);
        var current = ToDomain(catalog.Proposals[index]);
        if (current.ProposedAtUtc != expectedProposedAtUtc || current.FinalDecision is not null) return new(DecisionWriteStatus.Conflict, current, []);
        var decided = current.Decide(outcome, deciderId, validation.Note!, decidedAtUtc);
        if (outcome == DecisionOutcome.Accepted && current.IntendedSupersessionTargetId is not null)
        {
            var targetId = current.IntendedSupersessionTargetId.Value;
            var targetIndex = catalog.Proposals.FindIndex(record => record.OrganizationId == organizationId.Value && record.Id == targetId.Value);
            if (targetIndex < 0) return new(DecisionWriteStatus.TargetNotAccepted, current, []);
            var target = ToDomain(catalog.Proposals[targetIndex]);
            if (target.Status != AdrLifecycleStatus.Accepted) return new(DecisionWriteStatus.TargetNotAccepted, current, []);
            if (WouldCreateCycle(catalog, organizationId, current.Id, target.Id)) return new(DecisionWriteStatus.InvalidRelationship, current, []);
            decided = decided.CompleteSupersessionOf(target.Id, decidedAtUtc);
            catalog.Proposals[targetIndex] = FromDomain(target.MarkSupersededBy(decided.Id, decidedAtUtc));
        }
        var record = FromDomain(decided);
        catalog.Proposals[index] = record;
        catalog.DecisionOperations.Add(new(operationId.Value, organizationId.Value, proposalId.Value, expectedProposedAtUtc, deciderId.Value, outcome, note, record));
        await SaveAsync(catalog, cancellationToken).ConfigureAwait(false);
        return new(DecisionWriteStatus.Decided, decided, []);
    }

    private static bool WouldCreateCycle(Catalog catalog, OrganizationId organizationId, AdrId replacementId, AdrId targetId)
    {
        if (replacementId == targetId) return true;
        var visited = new HashSet<Guid>();
        AdrId? currentId = targetId;
        while (currentId is not null && visited.Add(currentId.Value.Value))
        {
            if (currentId == replacementId) return true;
            var record = catalog.Proposals.FirstOrDefault(candidate => candidate.OrganizationId == organizationId.Value && candidate.Id == currentId.Value.Value);
            currentId = record?.SupersedesTargetId is null ? null : new AdrId(record.SupersedesTargetId.Value);
        }
        return currentId is not null;
    }

    public async Task<IReadOnlyList<DecidedSummary>> ListDecidedAsync(OrganizationId organizationId, DecisionOutcome outcome, CancellationToken cancellationToken = default)
    {
        var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return catalog.Proposals.Where(p => p.OrganizationId == organizationId.Value && p.FinalDecision?.Outcome == outcome)
            .OrderByDescending(p => p.FinalDecision!.DecidedAtUtc).ThenBy(p => p.Id)
            .Select(p => { var record = ToDomain(p); return new DecidedSummary(record.Id, record.Content.Title, record.AuthorId, record.FinalDecision!); }).ToArray();
    }

    public async Task<IReadOnlyList<AdrProposal>> ListSharedAsync(OrganizationId organizationId, CancellationToken cancellationToken = default)
    {
        var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return catalog.Proposals
            .Where(record => record.OrganizationId == organizationId.Value)
            .Select(ToDomain)
            .ToArray();
    }

    public Task<AdrProposal?> GetSharedAsync(OrganizationId organizationId, AdrId id, CancellationToken cancellationToken = default) => GetAsync(organizationId, id, cancellationToken);

    public async Task<RecoveryWriteResult> StartRecoveryAsync(OrganizationId organizationId, AdrId draftId, MemberId authorId, long expectedVersion, DateTimeOffset deadlineUtc, AdministrationEvent administrationEvent, CancellationToken cancellationToken = default)
    {
        await using var handle = await staging.AcquireLockAsync(Reference, TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
        if (!handle.IsAcquired) throw new InvalidOperationException("The draft Workbench is busy. Retry the operation.");
        var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
        var index = catalog.Drafts.FindIndex(d => d.OrganizationId == organizationId.Value && d.Id == draftId.Value);
        if (index < 0) return new(RecoveryWriteStatus.NotFound, null);
        var current = ToDomain(catalog.Drafts[index]);
        if (current.AuthorId != authorId) return new(RecoveryWriteStatus.Conflict, current);
        if (current.RecoveryDeadlineUtc is not null) return new(RecoveryWriteStatus.AlreadyApplied, current);
        if (current.Version != expectedVersion) return new(RecoveryWriteStatus.Conflict, current);
        var next = current.StartRecovery(deadlineUtc, administrationEvent.OccurredAtUtc);
        catalog.Drafts[index] = FromDomain(next);
        catalog.RecoveryEvents.Add(FromDomain(administrationEvent));
        await SaveAsync(catalog, cancellationToken).ConfigureAwait(false);
        return new(RecoveryWriteStatus.Applied, next);
    }

    public async Task<RecoveryWriteResult> CancelRecoveryAsync(OrganizationId organizationId, AdrId draftId, MemberId authorId, long expectedVersion, AdministrationEvent administrationEvent, CancellationToken cancellationToken = default)
    {
        await using var handle = await staging.AcquireLockAsync(Reference, TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
        if (!handle.IsAcquired) throw new InvalidOperationException("The draft Workbench is busy. Retry the operation.");
        var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
        var index = catalog.Drafts.FindIndex(d => d.OrganizationId == organizationId.Value && d.Id == draftId.Value);
        if (index < 0) return new(RecoveryWriteStatus.NotFound, null);
        var current = ToDomain(catalog.Drafts[index]);
        if (current.AuthorId != authorId) return new(RecoveryWriteStatus.Conflict, current);
        if (current.RecoveryDeadlineUtc is null) return new(RecoveryWriteStatus.AlreadyApplied, current);
        if (current.Version != expectedVersion) return new(RecoveryWriteStatus.Conflict, current);
        var next = current.CancelRecovery(administrationEvent.OccurredAtUtc);
        catalog.Drafts[index] = FromDomain(next);
        catalog.RecoveryEvents.Add(FromDomain(administrationEvent));
        await SaveAsync(catalog, cancellationToken).ConfigureAwait(false);
        return new(RecoveryWriteStatus.Applied, next);
    }

    public async Task<IReadOnlyList<RecoveryEligibleDraft>> ListEligibleAsync(OrganizationId organizationId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return catalog.Drafts
            .Where(d => d.OrganizationId == organizationId.Value && d.RecoveryDeadlineUtc is not null && d.RecoveryDeadlineUtc > now)
            .OrderBy(d => d.RecoveryDeadlineUtc).ThenBy(d => d.Id)
            .Select(d => new RecoveryEligibleDraft(new AdrId(d.Id), new DraftTitle(d.Title), new MemberId(d.AuthorId), d.RecoveryDeadlineUtc!.Value, d.Version))
            .ToArray();
    }

    public async Task<ReassignDraftResult> ReassignAsync(OrganizationId organizationId, AdrId draftId, MemberId formerAuthorId, MemberId newAuthorId, long expectedVersion, DateTimeOffset now, AdministrationEvent administrationEvent, OperationId operationId, CancellationToken cancellationToken = default)
    {
        await using var handle = await staging.AcquireLockAsync(Reference, TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
        if (!handle.IsAcquired) throw new InvalidOperationException("The draft Workbench is busy. Retry the operation.");
        var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
        var prior = catalog.ReassignmentOperations.FirstOrDefault(o => o.Id == operationId.Value);
        if (prior is not null)
        {
            var same = prior.OrganizationId == organizationId.Value && prior.DraftId == draftId.Value && prior.FormerAuthorId == formerAuthorId.Value && prior.NewAuthorId == newAuthorId.Value && prior.ExpectedVersion == expectedVersion;
            return same ? new(ReassignDraftStatus.AlreadyApplied, ToDomain(prior.Draft)) : new(ReassignDraftStatus.OperationMismatch, null);
        }
        var index = catalog.Drafts.FindIndex(d => d.OrganizationId == organizationId.Value && d.Id == draftId.Value);
        if (index < 0) return new(ReassignDraftStatus.NotFound, null);
        var current = ToDomain(catalog.Drafts[index]);
        if (current.AuthorId != formerAuthorId || current.Version != expectedVersion || current.RecoveryDeadlineUtc is null) return new(ReassignDraftStatus.Conflict, current);
        if (current.IsExpired(now)) return new(ReassignDraftStatus.Expired, current);
        var reassigned = current.Reassign(newAuthorId, now);
        catalog.Drafts[index] = FromDomain(reassigned);
        catalog.RecoveryEvents.Add(FromDomain(administrationEvent));
        catalog.ReassignmentOperations.Add(new(operationId.Value, organizationId.Value, draftId.Value, formerAuthorId.Value, newAuthorId.Value, expectedVersion, FromDomain(reassigned)));
        await SaveAsync(catalog, cancellationToken).ConfigureAwait(false);
        return new(ReassignDraftStatus.Reassigned, reassigned);
    }

    public async Task<IReadOnlyList<AdministrationEvent>> ListRecoveryEventsAsync(OrganizationId organizationId, CancellationToken cancellationToken = default)
    {
        var catalog = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return catalog.RecoveryEvents.Where(e => e.OrganizationId == organizationId.Value)
            .OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Id).Select(ToDomain).ToArray();
    }

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

    private static DraftRecord FromDomain(AdrDraft d) => new(d.Id.Value, d.OrganizationId.Value, d.AuthorId.Value, d.Content.Title.Value, d.Content.Context, d.Content.Decision, d.Content.Consequences, d.CreatedAtUtc, d.ModifiedAtUtc, d.Version, d.IntendedSupersessionTargetId?.Value, d.RecoveryDeadlineUtc);
    private static AdrDraft ToDomain(DraftRecord d) => AdrDraft.Restore(new AdrId(d.Id), new OrganizationId(d.OrganizationId), new MemberId(d.AuthorId), new DraftContent(d.Title, d.Context, d.Decision, d.Consequences), d.CreatedAtUtc, d.ModifiedAtUtc, d.Version, ToAdrId(d.IntendedSupersessionTargetId), d.RecoveryDeadlineUtc);
    private static AdrId? ToAdrId(Guid? value) => value is null ? null : new AdrId(value.Value);
    private static ProposalRecord FromDomain(AdrProposal p) => new(p.Id.Value, p.OrganizationId.Value, p.AuthorId.Value, p.ProposerId.Value, p.Content.Title.Value, p.Content.Context, p.Content.Decision, p.Content.Consequences, p.CreatedAtUtc, p.ProposedAtUtc, p.SourceDraftVersion, p.FinalDecision is null ? null : new(p.FinalDecision.Outcome, p.FinalDecision.DeciderId.Value, p.FinalDecision.DecidedAtUtc, p.FinalDecision.Note), p.IntendedSupersessionTargetId?.Value, p.Supersedes?.TargetId.Value, p.Supersedes?.SupersededAtUtc, p.SupersededBy?.ReplacementId.Value, p.SupersededBy?.SupersededAtUtc);
    private static AdrProposal ToDomain(ProposalRecord p) => new(new AdrId(p.Id), new OrganizationId(p.OrganizationId), new MemberId(p.AuthorId), new MemberId(p.ProposerId), new ProposalContent(new DraftTitle(p.Title), p.Context, p.Decision, p.Consequences), p.CreatedAtUtc, p.ProposedAtUtc, p.SourceDraftVersion, p.FinalDecision is null ? null : new(p.FinalDecision.Outcome, new MemberId(p.FinalDecision.DeciderId), p.FinalDecision.DecidedAtUtc, p.FinalDecision.Note), ToAdrId(p.IntendedSupersessionTargetId), p.SupersedesTargetId is null || p.SupersedesAtUtc is null ? null : new(new AdrId(p.SupersedesTargetId.Value), p.SupersedesAtUtc.Value), p.SupersededByReplacementId is null || p.SupersededByAtUtc is null ? null : new(new AdrId(p.SupersededByReplacementId.Value), p.SupersededByAtUtc.Value));
    private static EventRecord FromDomain(AdministrationEvent value) => new(value.Id, value.OrganizationId.Value, value.Type, value.OccurredAtUtc, value.Source, value.ActorId?.Value, value.PreviousValue, value.NewValue, value.SubjectId?.Value, value.DraftId?.Value);
    private static AdministrationEvent ToDomain(EventRecord value) => new(value.Id, new(value.OrganizationId), value.Type, value.OccurredAtUtc, value.Source, value.ActorId is null ? null : new(value.ActorId), value.PreviousValue, value.NewValue, value.SubjectId is null ? null : new(value.SubjectId), value.DraftId is null ? null : new(value.DraftId.Value));
    public sealed class Catalog { public List<DraftRecord> Drafts { get; set; } = []; public List<OperationRecord> Operations { get; set; } = []; public List<ProposalRecord> Proposals { get; set; } = []; public List<ProposalOperationRecord> ProposalOperations { get; set; } = []; public List<DecisionOperationRecord> DecisionOperations { get; set; } = []; public List<EventRecord> RecoveryEvents { get; set; } = []; public List<ReassignmentOperationRecord> ReassignmentOperations { get; set; } = []; }
    public sealed record DraftRecord(Guid Id, string OrganizationId, string AuthorId, string Title, string Context, string Decision, string Consequences, DateTimeOffset CreatedAtUtc, DateTimeOffset ModifiedAtUtc, long Version, Guid? IntendedSupersessionTargetId = null, DateTimeOffset? RecoveryDeadlineUtc = null);
    public sealed record OperationRecord(Guid Id, string Kind, DraftRecord Draft, long? ExpectedVersion);
    public sealed record ProposalRecord(Guid Id, string OrganizationId, string AuthorId, string ProposerId, string Title, string Context, string Decision, string Consequences, DateTimeOffset CreatedAtUtc, DateTimeOffset ProposedAtUtc, long SourceDraftVersion, DecisionRecord? FinalDecision = null, Guid? IntendedSupersessionTargetId = null, Guid? SupersedesTargetId = null, DateTimeOffset? SupersedesAtUtc = null, Guid? SupersededByReplacementId = null, DateTimeOffset? SupersededByAtUtc = null);
    public sealed record DecisionRecord(DecisionOutcome Outcome, string DeciderId, DateTimeOffset DecidedAtUtc, string Note);
    public sealed record ProposalOperationRecord(Guid Id, string OrganizationId, string AuthorId, Guid DraftId, long ExpectedVersion, ProposalRecord Proposal);
    public sealed record DecisionOperationRecord(Guid Id, string OrganizationId, Guid ProposalId, DateTimeOffset ExpectedProposedAtUtc, string DeciderId, DecisionOutcome Outcome, string Note, ProposalRecord Record);
    public sealed record EventRecord(Guid Id, string OrganizationId, AdministrationEventType Type, DateTimeOffset OccurredAtUtc, string Source, string? ActorId, string? PreviousValue, string? NewValue, string? SubjectId, Guid? DraftId);
    public sealed record ReassignmentOperationRecord(Guid Id, string OrganizationId, Guid DraftId, string FormerAuthorId, string NewAuthorId, long ExpectedVersion, DraftRecord Draft);
}
