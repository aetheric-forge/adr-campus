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
        if (!await members.IsActiveMemberAsync(query.OrganizationId, query.MemberId, cancellationToken).ConfigureAwait(false)) return DiscoveryQueryResult.Unauthorized(query);
        try
        {
            var shared = (await records.ListSharedAsync(query.OrganizationId, cancellationToken).ConfigureAwait(false)).Where(record => record.OrganizationId == query.OrganizationId).ToArray();
            var matching = shared.Where(record => Includes(query.View, record.Status));
            if (query.Statuses is { Count: > 0 }) matching = matching.Where(record => query.Statuses.Contains(record.Status));
            var matched = matching.ToArray();
            var identities = matched.SelectMany(record => record.FinalDecision is null ? new[] { record.AuthorId, record.ProposerId } : new[] { record.AuthorId, record.FinalDecision.DeciderId }).Distinct().ToArray();
            var resolved = identities.Length == 0 ? new MemberNameResolution(true, new Dictionary<string, string>()) : await names.ResolveAsync(query.OrganizationId, identities, cancellationToken).ConfigureAwait(false);
            if (!resolved.IsAvailable) return DiscoveryQueryResult.Unavailable(query);
            var items = matched.Select(record => ToItem(record, resolved)).ToArray();
            var ordered = Order(items, query.Sort, query.Direction);
            var totalPages = Math.Max(1, (int)Math.Ceiling(items.Length / (double)PageSize));
            var page = Math.Min(query.Page, totalPages);
            var pageItems = ordered.Skip((page - 1) * PageSize).Take(PageSize).ToArray();
            return DiscoveryQueryResult.Success(query with { Page = page }, pageItems, shared.Length, items.Length, totalPages);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return DiscoveryQueryResult.Unavailable(query); }
    }

    private static bool IsValid(DiscoveryQuery query) => Enum.IsDefined(query.View) && query.Page > 0 && (query.Sort is null || Enum.IsDefined(query.Sort.Value)) && Enum.IsDefined(query.Direction) && (query.Statuses is null || query.Statuses.All(IsSharedStatus)) && (query.View == SharedRecordView.All || query.Statuses is null or { Count: 0 });
    private static bool IsSharedStatus(AdrLifecycleStatus status) => status is AdrLifecycleStatus.Proposed or AdrLifecycleStatus.Accepted or AdrLifecycleStatus.Rejected or AdrLifecycleStatus.Superseded;
    private static bool Includes(SharedRecordView view, AdrLifecycleStatus status) => view switch { SharedRecordView.Current => status == AdrLifecycleStatus.Accepted, SharedRecordView.Proposed => status == AdrLifecycleStatus.Proposed, SharedRecordView.Historical => status is AdrLifecycleStatus.Rejected or AdrLifecycleStatus.Superseded, SharedRecordView.All => IsSharedStatus(status), _ => false };

    private static SharedRecordItem ToItem(AdrProposal record, MemberNameResolution names)
    {
        var decision = record.FinalDecision;
        return decision is null
            ? new(record.Id, record.Content.Title, record.Status, record.AuthorId, names.For(record.AuthorId), record.ProposerId, names.For(record.ProposerId), "Proposer", record.ProposedAtUtc)
            : new(record.Id, record.Content.Title, record.Status, record.AuthorId, names.For(record.AuthorId), decision.DeciderId, names.For(decision.DeciderId), "Decider", decision.DecidedAtUtc);
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
}

public sealed record DiscoveryQuery(OrganizationId OrganizationId, MemberId MemberId, SharedRecordView View, IReadOnlySet<AdrLifecycleStatus>? Statuses = null, SharedRecordSort? Sort = null, SortDirection Direction = SortDirection.Descending, int Page = 1);
public enum DiscoveryQueryStatus { Success, Unauthorized, Invalid, Unavailable }
public sealed record DiscoveryQueryResult(DiscoveryQueryStatus Status, DiscoveryQuery Query, IReadOnlyList<SharedRecordItem> Items, int TotalSharedCount, int TotalMatchingCount, int TotalPages)
{
    public bool IsSuccess => Status == DiscoveryQueryStatus.Success;
    public bool IsOrganizationEmpty => IsSuccess && TotalSharedCount == 0;
    public bool IsViewEmpty => IsSuccess && TotalSharedCount > 0 && TotalMatchingCount == 0;
    public bool HasPreviousPage => IsSuccess && Query.Page > 1;
    public bool HasNextPage => IsSuccess && Query.Page < TotalPages;
    public static DiscoveryQueryResult Success(DiscoveryQuery query, IReadOnlyList<SharedRecordItem> items, int totalSharedCount, int totalMatchingCount, int totalPages) => new(DiscoveryQueryStatus.Success, query, items, totalSharedCount, totalMatchingCount, totalPages);
    public static DiscoveryQueryResult Unauthorized(DiscoveryQuery query) => new(DiscoveryQueryStatus.Unauthorized, query, [], 0, 0, 0);
    public static DiscoveryQueryResult Invalid(DiscoveryQuery query) => new(DiscoveryQueryStatus.Invalid, query, [], 0, 0, 0);
    public static DiscoveryQueryResult Unavailable(DiscoveryQuery query) => new(DiscoveryQueryStatus.Unavailable, query, [], 0, 0, 0);
}
