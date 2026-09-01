using AdrCampus.Application.Identity;
using AdrCampus.Core.Discovery;
using AdrCampus.Core.Domain;

namespace AdrCampus.Application.Discovery;

public sealed class DiscoveryApplicationService(
    ISharedRecordRepository records,
    IMemberAuthority members)
{
    public async Task<DiscoveryQueryResult> BrowseAsync(
        DiscoveryQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(query.View))
        {
            return DiscoveryQueryResult.Invalid(query.View);
        }

        if (!await members.IsActiveMemberAsync(query.OrganizationId, query.MemberId, cancellationToken).ConfigureAwait(false))
        {
            return DiscoveryQueryResult.Unauthorized();
        }

        try
        {
            var shared = (await records.ListSharedAsync(query.OrganizationId, cancellationToken).ConfigureAwait(false))
                .Where(record => record.OrganizationId == query.OrganizationId)
                .ToArray();
            var items = shared
                .Where(record => Includes(query.View, record.Status))
                .Select(ToItem)
                .OrderByDescending(item => item.RelevantAtUtc)
                .ThenBy(item => item.Id.Value)
                .ToArray();
            return DiscoveryQueryResult.Success(query.View, items, shared.Length);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return DiscoveryQueryResult.Unavailable(query.View);
        }
    }

    private static bool Includes(SharedRecordView view, AdrLifecycleStatus status) => view switch
    {
        SharedRecordView.Current => status == AdrLifecycleStatus.Accepted,
        SharedRecordView.Proposed => status == AdrLifecycleStatus.Proposed,
        SharedRecordView.Historical => status is AdrLifecycleStatus.Rejected or AdrLifecycleStatus.Superseded,
        SharedRecordView.All => status is AdrLifecycleStatus.Proposed or AdrLifecycleStatus.Accepted or AdrLifecycleStatus.Rejected or AdrLifecycleStatus.Superseded,
        _ => false
    };

    private static SharedRecordItem ToItem(AdrProposal record)
    {
        var decision = record.FinalDecision;
        return decision is null
            ? new(record.Id, record.Content.Title, record.Status, record.AuthorId, record.ProposerId, "Proposer", record.ProposedAtUtc)
            : new(record.Id, record.Content.Title, record.Status, record.AuthorId, decision.DeciderId, "Decider", decision.DecidedAtUtc);
    }
}

public sealed record DiscoveryQuery(
    OrganizationId OrganizationId,
    MemberId MemberId,
    SharedRecordView View);

public enum DiscoveryQueryStatus
{
    Success,
    Unauthorized,
    Invalid,
    Unavailable
}

public sealed record DiscoveryQueryResult(
    DiscoveryQueryStatus Status,
    SharedRecordView View,
    IReadOnlyList<SharedRecordItem> Items,
    int TotalSharedCount)
{
    public bool IsSuccess => Status == DiscoveryQueryStatus.Success;
    public bool IsOrganizationEmpty => IsSuccess && TotalSharedCount == 0;
    public bool IsViewEmpty => IsSuccess && TotalSharedCount > 0 && Items.Count == 0;
    public static DiscoveryQueryResult Success(SharedRecordView view, IReadOnlyList<SharedRecordItem> items, int totalSharedCount) => new(DiscoveryQueryStatus.Success, view, items, totalSharedCount);
    public static DiscoveryQueryResult Unauthorized() => new(DiscoveryQueryStatus.Unauthorized, SharedRecordView.Current, [], 0);
    public static DiscoveryQueryResult Invalid(SharedRecordView view) => new(DiscoveryQueryStatus.Invalid, view, [], 0);
    public static DiscoveryQueryResult Unavailable(SharedRecordView view) => new(DiscoveryQueryStatus.Unavailable, view, [], 0);
}
