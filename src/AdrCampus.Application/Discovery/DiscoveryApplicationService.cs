using AdrCampus.Application.Identity;
using AdrCampus.Core.Discovery;
using AdrCampus.Core.Domain;

namespace AdrCampus.Application.Discovery;

public sealed class DiscoveryApplicationService(ISharedRecordRepository records, IMemberAuthority members, IMemberDisplayNameDirectory names)
{
    public const int PageSize = 25;

    public async Task<DiscoveryQueryResult> BrowseAsync(DiscoveryQuery query, CancellationToken cancellationToken = default)
    {
        if (!IsValid(query)) return DiscoveryQueryResult.Invalid(query);
        var search = SearchPhraseValidator.Validate(query.Search);
        if (!search.IsValid) return DiscoveryQueryResult.Invalid(query with { Search = search.Phrase }, search.Errors);
        query = query with { Search = search.Phrase };
        if (!await members.IsActiveMemberAsync(query.OrganizationId, query.MemberId, cancellationToken).ConfigureAwait(false)) return DiscoveryQueryResult.Unauthorized(query);
        try
        {
            var shared = (await records.ListSharedAsync(query.OrganizationId, cancellationToken).ConfigureAwait(false)).Where(record => record.OrganizationId == query.OrganizationId).ToArray();
            var matching = shared.Where(record => Includes(query.View, record.Status));
            if (query.Statuses is { Count: > 0 }) matching = matching.Where(record => query.Statuses.Contains(record.Status));
            var matched = matching.ToArray();
            var identities = matched.SelectMany(record => record.FinalDecision is null ? new[] { record.AuthorId, record.ProposerId } : new[] { record.AuthorId, record.ProposerId, record.FinalDecision.DeciderId }).Distinct().ToArray();
            var resolved = identities.Length == 0 ? new MemberNameResolution(true, new Dictionary<string, string>()) : await names.ResolveAsync(query.OrganizationId, identities, cancellationToken).ConfigureAwait(false);
            if (!resolved.IsAvailable) return DiscoveryQueryResult.Unavailable(query);
            var candidates = matched.Select(record => new SearchCandidate(record, ToItem(record, resolved))).ToArray();
            if (search.Phrase.Length > 0) candidates = candidates.Where(candidate => Matches(candidate, search.Phrase)).ToArray();
            var ordered = query.Sort is null && search.Phrase.Length > 0
                ? candidates.OrderBy(candidate => Rank(candidate, search.Phrase)).ThenByDescending(candidate => candidate.Item.RelevantAtUtc).ThenBy(candidate => candidate.Item.Id.Value).Select(candidate => candidate.Item)
                : Order(candidates.Select(candidate => candidate.Item).ToArray(), query.Sort, query.Direction);
            var totalPages = Math.Max(1, (int)Math.Ceiling(candidates.Length / (double)PageSize));
            var page = Math.Min(query.Page, totalPages);
            var pageItems = ordered.Skip((page - 1) * PageSize).Take(PageSize).ToArray();
            return DiscoveryQueryResult.Success(query with { Page = page }, pageItems, shared.Length, candidates.Length, totalPages);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return DiscoveryQueryResult.Unavailable(query); }
    }

    public async Task<SuggestionQueryResult> SuggestAsync(DiscoveryQuery query, CancellationToken cancellationToken = default)
    {
        var result = await BrowseAsync(query with { Sort = null, Direction = SortDirection.Descending, Page = 1 }, cancellationToken).ConfigureAwait(false);
        return new(result.Status, result.Items.Take(8).ToArray(), result.TotalMatchingCount > 8, result.Errors);
    }

    public async Task<SharedDetailResult> GetDetailAsync(OrganizationId organizationId, MemberId memberId, AdrId id, CancellationToken cancellationToken = default)
    {
        if (!await members.IsActiveMemberAsync(organizationId, memberId, cancellationToken).ConfigureAwait(false)) return SharedDetailResult.Unauthorized();
        try
        {
            var record = await records.GetSharedAsync(organizationId, id, cancellationToken).ConfigureAwait(false);
            if (record is null || record.OrganizationId != organizationId) return SharedDetailResult.NotFound();
            AdrProposal? supersedingReplacement = null;
            if (record.SupersededBy is not null)
            {
                supersedingReplacement = await records.GetSharedAsync(organizationId, record.SupersededBy.ReplacementId, cancellationToken).ConfigureAwait(false);
                if (supersedingReplacement?.OrganizationId != organizationId) supersedingReplacement = null;
            }
            var actorIds = new[] { record.AuthorId, record.ProposerId, record.FinalDecision?.DeciderId, supersedingReplacement?.FinalDecision?.DeciderId }.Where(actor => actor is not null).Cast<MemberId>().Distinct().ToArray();
            var resolved = await names.ResolveAsync(organizationId, actorIds, cancellationToken).ConfigureAwait(false);
            if (!resolved.IsAvailable) return SharedDetailResult.Unavailable();
            var author = Attribution(record.AuthorId, resolved); var proposer = Attribution(record.ProposerId, resolved);
            MemberAttribution? decider = record.FinalDecision is null ? null : Attribution(record.FinalDecision.DeciderId, resolved);
            var history = new List<LifecycleHistoryItem>
            {
                new(LifecycleEventType.Created, "ADR created", author, record.CreatedAtUtc, null),
                new(LifecycleEventType.Proposed, "Proposed for organization review", proposer, record.ProposedAtUtc, null)
            };
            if (record.FinalDecision is not null)
            {
                var type = record.FinalDecision.Outcome == DecisionOutcome.Accepted ? LifecycleEventType.Accepted : LifecycleEventType.Rejected;
                history.Add(new(type, record.FinalDecision.Outcome == DecisionOutcome.Accepted ? "Accepted as a current decision" : "Rejected", decider!, record.FinalDecision.DecidedAtUtc, record.FinalDecision.Note));
            }
            if (record.SupersededBy is not null && supersedingReplacement?.FinalDecision is not null)
            {
                var supersedingDecider = Attribution(supersedingReplacement.FinalDecision.DeciderId, resolved);
                history.Add(new(LifecycleEventType.Superseded, $"Superseded by {supersedingReplacement.Content.Title.Value}", supersedingDecider, record.SupersededBy.SupersededAtUtc, null));
            }
            SharedRecordReference? intendedTarget = null;
            if (record.IntendedSupersessionTargetId is not null)
            {
                var target = await records.GetSharedAsync(organizationId, record.IntendedSupersessionTargetId.Value, cancellationToken).ConfigureAwait(false);
                if (target is not null && target.OrganizationId == organizationId)
                    intendedTarget = new(target.Id, target.Content.Title, target.Status);
            }
            var proposedReplacements = (await records.ListSharedAsync(organizationId, cancellationToken).ConfigureAwait(false))
                .Where(candidate => candidate.OrganizationId == organizationId && candidate.Status == AdrLifecycleStatus.Proposed && candidate.IntendedSupersessionTargetId == record.Id)
                .OrderByDescending(candidate => candidate.ProposedAtUtc)
                .ThenBy(candidate => candidate.Id.Value)
                .Select(candidate => new SharedRecordReference(candidate.Id, candidate.Content.Title, candidate.Status))
                .ToArray();
            return SharedDetailResult.Success(new(record, author, proposer, decider, history.OrderBy(item => item.OccurredAtUtc).ToArray(), [], intendedTarget, proposedReplacements));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return SharedDetailResult.Unavailable(); }
    }

    private static MemberAttribution Attribution(MemberId id, MemberNameResolution names) => names.Names.TryGetValue(id.Value, out var displayName) ? new(id, displayName, true) : new(id, $"Former member ({ShortId(id.Value)})", false);
    private static string ShortId(string value) => value.Length <= 8 ? value : value[..8];

    private static bool IsValid(DiscoveryQuery query) => Enum.IsDefined(query.View) && query.Page > 0 && (query.Sort is null || Enum.IsDefined(query.Sort.Value)) && Enum.IsDefined(query.Direction) && (query.Statuses is null || query.Statuses.All(IsSharedStatus)) && (query.View == SharedRecordView.All || query.Statuses is null or { Count: 0 });
    private static bool IsSharedStatus(AdrLifecycleStatus status) => status is AdrLifecycleStatus.Proposed or AdrLifecycleStatus.Accepted or AdrLifecycleStatus.Rejected or AdrLifecycleStatus.Superseded;
    private static bool Includes(SharedRecordView view, AdrLifecycleStatus status) => view switch { SharedRecordView.Current => status == AdrLifecycleStatus.Accepted, SharedRecordView.Proposed => status == AdrLifecycleStatus.Proposed, SharedRecordView.Historical => status is AdrLifecycleStatus.Rejected or AdrLifecycleStatus.Superseded, SharedRecordView.All => IsSharedStatus(status), _ => false };

    private static SharedRecordItem ToItem(AdrProposal record, MemberNameResolution names)
    {
        var decision = record.FinalDecision;
        return decision is null
            ? new(record.Id, record.Content.Title, record.Status, record.AuthorId, names.For(record.AuthorId), record.ProposerId, names.For(record.ProposerId), record.ProposerId, names.For(record.ProposerId), "Proposer", record.ProposedAtUtc)
            : new(record.Id, record.Content.Title, record.Status, record.AuthorId, names.For(record.AuthorId), record.ProposerId, names.For(record.ProposerId), decision.DeciderId, names.For(decision.DeciderId), "Decider", decision.DecidedAtUtc);
    }

    private static bool Matches(SearchCandidate candidate, string phrase)
    {
        var record = candidate.Record; var item = candidate.Item;
        return Contains(record.Id.Value.ToString("D"), phrase) || Contains(record.Id.Value.ToString("N"), phrase) || Contains(record.Content.Title.Value, phrase) || Contains(record.Content.Context, phrase) || Contains(record.Content.Decision, phrase) || Contains(record.Content.Consequences, phrase) || Contains(item.AuthorDisplayName, phrase) || Contains(item.ProposerDisplayName, phrase) || Contains(item.RelevantActorDisplayName, phrase) || Contains(record.FinalDecision?.Note, phrase) || DateMatches(item.RelevantAtUtc, phrase);
    }

    private static int Rank(SearchCandidate candidate, string phrase)
    {
        var id = candidate.Record.Id.Value;
        if (string.Equals(id.ToString("D"), phrase, StringComparison.OrdinalIgnoreCase) || string.Equals(id.ToString("N"), phrase, StringComparison.OrdinalIgnoreCase)) return 0;
        return Contains(candidate.Record.Content.Title.Value, phrase) ? 1 : 2;
    }

    private static bool Contains(string? value, string phrase) => value?.Contains(phrase, StringComparison.OrdinalIgnoreCase) == true;
    private static bool DateMatches(DateTimeOffset value, string phrase)
    {
        var local = value.ToLocalTime();
        return new[] { value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), local.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), local.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture), local.ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture) }.Any(formatted => Contains(formatted, phrase));
    }

    private static IOrderedEnumerable<SharedRecordItem> Order(IReadOnlyCollection<SharedRecordItem> items, SharedRecordSort? sort, SortDirection direction)
    {
        if (sort is null) return items.OrderByDescending(item => item.RelevantAtUtc).ThenBy(item => item.Id.Value);
        var descending = direction == SortDirection.Descending;
        IOrderedEnumerable<SharedRecordItem> ordered = sort switch
        {
            SharedRecordSort.Identifier => descending ? items.OrderByDescending(item => item.Id.Value) : items.OrderBy(item => item.Id.Value),
            SharedRecordSort.Title => descending ? items.OrderByDescending(item => item.Title.Value, StringComparer.OrdinalIgnoreCase) : items.OrderBy(item => item.Title.Value, StringComparer.OrdinalIgnoreCase),
            SharedRecordSort.Status => descending ? items.OrderByDescending(item => item.Status.ToString(), StringComparer.OrdinalIgnoreCase) : items.OrderBy(item => item.Status.ToString(), StringComparer.OrdinalIgnoreCase),
            SharedRecordSort.Author => descending ? items.OrderByDescending(item => item.AuthorDisplayName, StringComparer.OrdinalIgnoreCase) : items.OrderBy(item => item.AuthorDisplayName, StringComparer.OrdinalIgnoreCase),
            _ => descending ? items.OrderByDescending(item => item.RelevantAtUtc) : items.OrderBy(item => item.RelevantAtUtc)
        };
        return sort == SharedRecordSort.Identifier ? ordered : ordered.ThenBy(item => item.Id.Value);
    }

    private sealed record SearchCandidate(AdrProposal Record, SharedRecordItem Item);
}

public sealed record DiscoveryQuery(OrganizationId OrganizationId, MemberId MemberId, SharedRecordView View, IReadOnlySet<AdrLifecycleStatus>? Statuses = null, SharedRecordSort? Sort = null, SortDirection Direction = SortDirection.Descending, int Page = 1, string? Search = null);
public enum DiscoveryQueryStatus { Success, Unauthorized, Invalid, Unavailable }
public sealed record DiscoveryQueryResult(DiscoveryQueryStatus Status, DiscoveryQuery Query, IReadOnlyList<SharedRecordItem> Items, int TotalSharedCount, int TotalMatchingCount, int TotalPages, IReadOnlyList<SearchValidationError> Errors)
{
    public bool IsSuccess => Status == DiscoveryQueryStatus.Success;
    public bool IsOrganizationEmpty => IsSuccess && TotalSharedCount == 0;
    public bool IsViewEmpty => IsSuccess && TotalSharedCount > 0 && TotalMatchingCount == 0;
    public bool HasPreviousPage => IsSuccess && Query.Page > 1;
    public bool HasNextPage => IsSuccess && Query.Page < TotalPages;
    public static DiscoveryQueryResult Success(DiscoveryQuery query, IReadOnlyList<SharedRecordItem> items, int totalSharedCount, int totalMatchingCount, int totalPages) => new(DiscoveryQueryStatus.Success, query, items, totalSharedCount, totalMatchingCount, totalPages, []);
    public static DiscoveryQueryResult Unauthorized(DiscoveryQuery query) => new(DiscoveryQueryStatus.Unauthorized, query, [], 0, 0, 0, []);
    public static DiscoveryQueryResult Invalid(DiscoveryQuery query, IReadOnlyList<SearchValidationError>? errors = null) => new(DiscoveryQueryStatus.Invalid, query, [], 0, 0, 0, errors ?? []);
    public static DiscoveryQueryResult Unavailable(DiscoveryQuery query) => new(DiscoveryQueryStatus.Unavailable, query, [], 0, 0, 0, []);
}
public sealed record SuggestionQueryResult(DiscoveryQueryStatus Status, IReadOnlyList<SharedRecordItem> Items, bool HasMore, IReadOnlyList<SearchValidationError> Errors);
public enum LifecycleEventType { Created, Proposed, Accepted, Rejected, AuthorReassigned, Superseded }
public sealed record MemberAttribution(MemberId Id, string DisplayName, bool IsCurrentMember);
public sealed record LifecycleHistoryItem(LifecycleEventType Type, string Label, MemberAttribution Actor, DateTimeOffset OccurredAtUtc, string? Note);
public sealed record SharedRecordRelationship(string Direction, AdrId RelatedId, DraftTitle RelatedTitle, DateTimeOffset OccurredAtUtc);
public sealed record SharedRecordReference(AdrId Id, DraftTitle Title, AdrLifecycleStatus Status);
public sealed record SharedRecordDetail(AdrProposal Record, MemberAttribution Author, MemberAttribution Proposer, MemberAttribution? Decider, IReadOnlyList<LifecycleHistoryItem> History, IReadOnlyList<SharedRecordRelationship> Relationships, SharedRecordReference? IntendedSupersessionTarget = null, IReadOnlyList<SharedRecordReference>? ProposedReplacements = null);
public enum SharedDetailStatus { Success, Unauthorized, NotFound, Unavailable }
public sealed record SharedDetailResult(SharedDetailStatus Status, SharedRecordDetail? Detail)
{
    public static SharedDetailResult Success(SharedRecordDetail detail) => new(SharedDetailStatus.Success, detail);
    public static SharedDetailResult Unauthorized() => new(SharedDetailStatus.Unauthorized, null);
    public static SharedDetailResult NotFound() => new(SharedDetailStatus.NotFound, null);
    public static SharedDetailResult Unavailable() => new(SharedDetailStatus.Unavailable, null);
}
