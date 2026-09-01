using AdrCampus.Core.Domain;

namespace AdrCampus.Core.Discovery;

public interface ISharedRecordRepository
{
    Task<IReadOnlyList<AdrProposal>> ListSharedAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default);
}

public enum SharedRecordView
{
    Current,
    Proposed,
    Historical,
    All
}

public sealed record SharedRecordItem(
    AdrId Id,
    DraftTitle Title,
    AdrLifecycleStatus Status,
    MemberId AuthorId,
    MemberId RelevantActorId,
    string RelevantActorRole,
    DateTimeOffset RelevantAtUtc);
