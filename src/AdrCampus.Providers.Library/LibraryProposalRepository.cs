using System.Collections.Concurrent;
using System.Text.Json;
using AdrCampus.Core.Discovery;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Drafts;
using AdrCampus.Core.Proposals;
using AethericForge.Runtime.Abstractions.Interfaces.Identity.Authentication;
using AethericForge.Runtime.Abstractions.Interfaces.Identity.Claims;
using AethericForge.Runtime.Abstractions.Interfaces.Identity.Lifecycle;
using AethericForge.Runtime.Abstractions.Interfaces.Identity.Subjects;
using AethericForge.Runtime.Abstractions.Interfaces.Knowledge.Authorities;
using AethericForge.Runtime.Abstractions.Interfaces.Knowledge.References;
using AethericForge.Runtime.Institutions.Library;
using AethericForge.Runtime.Models.Knowledge.Authorities;
using AethericForge.Runtime.Models.Knowledge.Primitives;
using AethericForge.Runtime.Models.Knowledge.References;
using AethericForge.Runtime.Models.Knowledge.Representations;

namespace AdrCampus.Providers.Library;

/// <summary>
/// Stores shared/discoverable ADR proposals and decisions as a Library knowledge artifact. Proposing a
/// draft is a real publication into Library: the draft is fetched from Workbench (<see cref="IDraftRepository"/>),
/// validated, written to the Library catalog, and then removed from the author's private Workbench drafts.
/// The Knowledge contracts do not expose a distributed lock, so writes are serialized with an in-process
/// semaphore keyed by organization; this is a weaker guarantee than the Redis-backed lock the previous
/// single-blob implementation used, but is adequate for a single-process host and preserves every
/// idempotency/conflict rule from the original implementation.
/// </summary>
public sealed class LibraryProposalRepository(ILibrary library, IDraftRepository drafts) : IProposalRepository, ISharedRecordRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);

    public async Task<ProposalWriteResult> ProposeAsync(OrganizationId organizationId, MemberId authorId, AdrId draftId, long expectedDraftVersion, OperationId operationId, DateTimeOffset proposedAtUtc, CancellationToken cancellationToken = default)
    {
        var gate = GetLock(organizationId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var catalog = await ReadAsync(organizationId, cancellationToken).ConfigureAwait(false);
            var prior = catalog.ProposalOperations.FirstOrDefault(o => o.Id == operationId.Value);
            if (prior is not null)
            {
                var same = prior.OrganizationId == organizationId.Value && prior.AuthorId == authorId.Value && prior.DraftId == draftId.Value && prior.ExpectedVersion == expectedDraftVersion;
                return same ? new(ProposalWriteStatus.AlreadyApplied, ToDomain(prior.Proposal), []) : new(ProposalWriteStatus.OperationMismatch, null, []);
            }
            var draft = await drafts.GetByAuthorAsync(organizationId, authorId, draftId, cancellationToken).ConfigureAwait(false);
            if (draft is null) return new(ProposalWriteStatus.UnauthorizedOrNotFound, null, []);
            if (draft.IsExpired(proposedAtUtc)) return new(ProposalWriteStatus.UnauthorizedOrNotFound, null, []);
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
            catalog.Proposals.Add(record);
            catalog.ProposalOperations.Add(new(operationId.Value, organizationId.Value, authorId.Value, draftId.Value, expectedDraftVersion, record));
            await SaveAsync(organizationId, catalog, cancellationToken).ConfigureAwait(false);
            await drafts.RemoveAsync(organizationId, authorId, draftId, expectedDraftVersion, cancellationToken).ConfigureAwait(false);
            return new(ProposalWriteStatus.Proposed, proposal, []);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<AdrProposal?> GetAsync(OrganizationId organizationId, AdrId id, CancellationToken cancellationToken = default)
    {
        var catalog = await ReadAsync(organizationId, cancellationToken).ConfigureAwait(false);
        var record = catalog.Proposals.FirstOrDefault(p => p.OrganizationId == organizationId.Value && p.Id == id.Value);
        return record is null ? null : ToDomain(record);
    }

    public async Task<IReadOnlyList<ProposalSummary>> ListAsync(OrganizationId organizationId, CancellationToken cancellationToken = default)
    {
        var catalog = await ReadAsync(organizationId, cancellationToken).ConfigureAwait(false);
        return catalog.Proposals.Where(p => p.OrganizationId == organizationId.Value && p.FinalDecision is null).OrderByDescending(p => p.ProposedAtUtc).ThenBy(p => p.Id)
            .Select(p => new ProposalSummary(new AdrId(p.Id), new DraftTitle(p.Title), new MemberId(p.AuthorId), new MemberId(p.ProposerId), p.ProposedAtUtc)).ToArray();
    }

    public async Task<DecisionWriteResult> DecideAsync(OrganizationId organizationId, AdrId proposalId, DateTimeOffset expectedProposedAtUtc, MemberId deciderId, DecisionOutcome outcome, string note, OperationId operationId, DateTimeOffset decidedAtUtc, CancellationToken cancellationToken = default)
    {
        var gate = GetLock(organizationId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var catalog = await ReadAsync(organizationId, cancellationToken).ConfigureAwait(false);
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
            await SaveAsync(organizationId, catalog, cancellationToken).ConfigureAwait(false);
            return new(DecisionWriteStatus.Decided, decided, []);
        }
        finally
        {
            gate.Release();
        }
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
        var catalog = await ReadAsync(organizationId, cancellationToken).ConfigureAwait(false);
        return catalog.Proposals.Where(p => p.OrganizationId == organizationId.Value && p.FinalDecision?.Outcome == outcome)
            .OrderByDescending(p => p.FinalDecision!.DecidedAtUtc).ThenBy(p => p.Id)
            .Select(p => { var record = ToDomain(p); return new DecidedSummary(record.Id, record.Content.Title, record.AuthorId, record.FinalDecision!); }).ToArray();
    }

    public async Task<IReadOnlyList<AdrProposal>> ListSharedAsync(OrganizationId organizationId, CancellationToken cancellationToken = default)
    {
        var catalog = await ReadAsync(organizationId, cancellationToken).ConfigureAwait(false);
        return catalog.Proposals
            .Where(record => record.OrganizationId == organizationId.Value)
            .Select(ToDomain)
            .ToArray();
    }

    public Task<AdrProposal?> GetSharedAsync(OrganizationId organizationId, AdrId id, CancellationToken cancellationToken = default) => GetAsync(organizationId, id, cancellationToken);

    private static SemaphoreSlim GetLock(OrganizationId organizationId) =>
        Locks.GetOrAdd(organizationId.Value, static _ => new SemaphoreSlim(1, 1));

    private static IKnowledgeAuthority CatalogAuthority(OrganizationId organizationId) =>
        new KnowledgeAuthority(new CatalogSubject(organizationId.Value), "adr-campus/proposals");

    private static IAuthoritativeReference CatalogReference(OrganizationId organizationId) =>
        new AuthoritativeReference("adr-campus", "ProposalCatalog", organizationId.Value, "1.0.0", CatalogAuthority(organizationId), "current");

    private async Task<Catalog> ReadAsync(OrganizationId organizationId, CancellationToken cancellationToken)
    {
        var artifact = await ((AethericForge.Runtime.Institutions.Library.ILibraryContext)library.Context).Knowledge.ResolveReferenceAsync(CatalogReference(organizationId), cancellationToken).ConfigureAwait(false);
        var representation = artifact?.Representations.FirstOrDefault();
        if (representation is null) return new Catalog();
        await using var stream = await representation.OpenStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<Catalog>(stream, Json, cancellationToken).ConfigureAwait(false) ?? new Catalog();
    }

    private async Task SaveAsync(OrganizationId organizationId, Catalog catalog, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(catalog, Json);
        var representation = new KnowledgeRepresentation("application/json", bytes.Length, _ => Task.FromResult<Stream>(new MemoryStream(bytes)));
        var descriptor = new KnowledgeDescriptor($"ADR Proposal Catalog: {organizationId.Value}");
        var authority = CatalogAuthority(organizationId);
        var artifact = await ((AethericForge.Runtime.Institutions.Library.ILibraryContext)library.Context).Knowledge.PublishArtifactAsync(descriptor, [representation], authority: authority, cancellationToken: cancellationToken).ConfigureAwait(false);
        await ((AethericForge.Runtime.Institutions.Library.ILibraryContext)library.Context).Knowledge.SetAuthoritativeReferenceAsync(CatalogReference(organizationId), artifact.Reference, cancellationToken).ConfigureAwait(false);
    }

    private static ProposalRecord FromDomain(AdrProposal p) => new(p.Id.Value, p.OrganizationId.Value, p.AuthorId.Value, p.ProposerId.Value, p.Content.Title.Value, p.Content.Context, p.Content.Decision, p.Content.Consequences, p.CreatedAtUtc, p.ProposedAtUtc, p.SourceDraftVersion, p.FinalDecision is null ? null : new(p.FinalDecision.Outcome, p.FinalDecision.DeciderId.Value, p.FinalDecision.DecidedAtUtc, p.FinalDecision.Note), p.IntendedSupersessionTargetId?.Value, p.Supersedes?.TargetId.Value, p.Supersedes?.SupersededAtUtc, p.SupersededBy?.ReplacementId.Value, p.SupersededBy?.SupersededAtUtc);
    private static AdrProposal ToDomain(ProposalRecord p) => new(new AdrId(p.Id), new OrganizationId(p.OrganizationId), new MemberId(p.AuthorId), new MemberId(p.ProposerId), new ProposalContent(new DraftTitle(p.Title), p.Context, p.Decision, p.Consequences), p.CreatedAtUtc, p.ProposedAtUtc, p.SourceDraftVersion, p.FinalDecision is null ? null : new(p.FinalDecision.Outcome, new MemberId(p.FinalDecision.DeciderId), p.FinalDecision.DecidedAtUtc, p.FinalDecision.Note), ToAdrId(p.IntendedSupersessionTargetId), p.SupersedesTargetId is null || p.SupersedesAtUtc is null ? null : new(new AdrId(p.SupersedesTargetId.Value), p.SupersedesAtUtc.Value), p.SupersededByReplacementId is null || p.SupersededByAtUtc is null ? null : new(new AdrId(p.SupersededByReplacementId.Value), p.SupersededByAtUtc.Value));
    private static AdrId? ToAdrId(Guid? value) => value is null ? null : new AdrId(value.Value);

    public sealed class Catalog { public List<ProposalRecord> Proposals { get; set; } = []; public List<ProposalOperationRecord> ProposalOperations { get; set; } = []; public List<DecisionOperationRecord> DecisionOperations { get; set; } = []; }
    public sealed record ProposalRecord(Guid Id, string OrganizationId, string AuthorId, string ProposerId, string Title, string Context, string Decision, string Consequences, DateTimeOffset CreatedAtUtc, DateTimeOffset ProposedAtUtc, long SourceDraftVersion, DecisionRecord? FinalDecision = null, Guid? IntendedSupersessionTargetId = null, Guid? SupersedesTargetId = null, DateTimeOffset? SupersedesAtUtc = null, Guid? SupersededByReplacementId = null, DateTimeOffset? SupersededByAtUtc = null);
    public sealed record DecisionRecord(DecisionOutcome Outcome, string DeciderId, DateTimeOffset DecidedAtUtc, string Note);
    public sealed record ProposalOperationRecord(Guid Id, string OrganizationId, string AuthorId, Guid DraftId, long ExpectedVersion, ProposalRecord Proposal);
    public sealed record DecisionOperationRecord(Guid Id, string OrganizationId, Guid ProposalId, DateTimeOffset ExpectedProposedAtUtc, string DeciderId, DecisionOutcome Outcome, string Note, ProposalRecord Record);

    private sealed record CatalogSubject(string SubjectId) : IIdentitySubject
    {
        public IdentityScheme Scheme => IdentityScheme.Service;
        public string? DisplayName => "ADR Campus Proposal Catalog";
        public IdentityState State => IdentityState.Active;
        public IReadOnlyCollection<IIdentityClaim> Claims => Array.Empty<IIdentityClaim>();
    }
}
