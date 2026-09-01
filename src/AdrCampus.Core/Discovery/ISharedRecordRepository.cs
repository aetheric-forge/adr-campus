using AdrCampus.Core.Domain;

namespace AdrCampus.Core.Discovery;

public interface ISharedRecordRepository
{
    Task<AdrProposal?> GetSharedAsync(OrganizationId organizationId, AdrId id, CancellationToken cancellationToken = default);
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

public enum SharedRecordSort { Identifier, Title, Status, Author, RelevantDate }
public enum SortDirection { Ascending, Descending }

public sealed record SharedRecordItem(
    AdrId Id,
    DraftTitle Title,
    AdrLifecycleStatus Status,
    MemberId AuthorId,
    string AuthorDisplayName,
    MemberId ProposerId,
    string ProposerDisplayName,
    MemberId RelevantActorId,
    string RelevantActorDisplayName,
    string RelevantActorRole,
    DateTimeOffset RelevantAtUtc);
