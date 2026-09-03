namespace AdrCampus.Core.Domain;

public enum AdrLifecycleStatus
{
    Draft,
    Proposed,
    Accepted,
    Rejected,
    Superseded
}

public sealed record DraftContent
{
    public DraftContent(string title, string? context = null, string? decision = null, string? consequences = null)
    {
        Title = new DraftTitle(title);
        Context = context ?? string.Empty;
        Decision = decision ?? string.Empty;
        Consequences = consequences ?? string.Empty;
    }

    public DraftTitle Title { get; }
    public string Context { get; }
    public string Decision { get; }
    public string Consequences { get; }
}

public sealed record AdrDraft
{
    private AdrDraft(
        AdrId id,
        OrganizationId organizationId,
        MemberId authorId,
        DraftContent content,
        AdrId? intendedSupersessionTargetId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset modifiedAtUtc,
        long version,
        DateTimeOffset? recoveryDeadlineUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        AuthorId = authorId;
        Content = content;
        IntendedSupersessionTargetId = intendedSupersessionTargetId;
        CreatedAtUtc = createdAtUtc;
        ModifiedAtUtc = modifiedAtUtc;
        Version = version;
        RecoveryDeadlineUtc = recoveryDeadlineUtc;
    }

    public AdrId Id { get; }
    public OrganizationId OrganizationId { get; }
    public MemberId AuthorId { get; }
    public DraftContent Content { get; }
    public AdrId? IntendedSupersessionTargetId { get; }
    public AdrLifecycleStatus Status => AdrLifecycleStatus.Draft;
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset ModifiedAtUtc { get; }
    public long Version { get; }
    public DateTimeOffset? RecoveryDeadlineUtc { get; }

    public static AdrDraft Create(
        AdrId id,
        OrganizationId organizationId,
        MemberId authorId,
        DraftContent content,
        DateTimeOffset nowUtc,
        AdrId? intendedSupersessionTargetId = null)
    {
        ArgumentNullException.ThrowIfNull(organizationId);
        ArgumentNullException.ThrowIfNull(authorId);
        ArgumentNullException.ThrowIfNull(content);
        if (intendedSupersessionTargetId == id)
        {
            throw new ArgumentException("An ADR cannot target itself for supersession.", nameof(intendedSupersessionTargetId));
        }
        return new AdrDraft(id, organizationId, authorId, content, intendedSupersessionTargetId, nowUtc, nowUtc, 1, null);
    }

    public static AdrDraft Restore(AdrId id, OrganizationId organizationId, MemberId authorId, DraftContent content, DateTimeOffset createdAtUtc, DateTimeOffset modifiedAtUtc, long version, AdrId? intendedSupersessionTargetId = null, DateTimeOffset? recoveryDeadlineUtc = null)
    {
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        if (modifiedAtUtc < createdAtUtc) throw new ArgumentOutOfRangeException(nameof(modifiedAtUtc));
        if (intendedSupersessionTargetId == id) throw new ArgumentException("An ADR cannot target itself for supersession.", nameof(intendedSupersessionTargetId));
        return new AdrDraft(id, organizationId, authorId, content, intendedSupersessionTargetId, createdAtUtc, modifiedAtUtc, version, recoveryDeadlineUtc);
    }

    public AdrDraft Revise(DraftContent content, long expectedVersion, DateTimeOffset nowUtc, AdrId? intendedSupersessionTargetId = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (expectedVersion != Version)
        {
            throw new DraftConcurrencyException(Id, expectedVersion, Version);
        }
        if (nowUtc < ModifiedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(nowUtc), "Modification time cannot move backwards.");
        }
        if (intendedSupersessionTargetId == Id)
        {
            throw new ArgumentException("An ADR cannot target itself for supersession.", nameof(intendedSupersessionTargetId));
        }

        return new AdrDraft(
            Id,
            OrganizationId,
            AuthorId,
            content,
            intendedSupersessionTargetId,
            CreatedAtUtc,
            nowUtc,
            checked(Version + 1),
            RecoveryDeadlineUtc);
    }

    public AdrDraft StartRecovery(DateTimeOffset deadlineUtc, DateTimeOffset now)
    {
        if (RecoveryDeadlineUtc is not null)
        {
            throw new InvalidOperationException($"Draft '{Id}' is already in recovery.");
        }
        return new AdrDraft(Id, OrganizationId, AuthorId, Content, IntendedSupersessionTargetId, CreatedAtUtc, now, checked(Version + 1), deadlineUtc);
    }

    public AdrDraft CancelRecovery(DateTimeOffset now)
    {
        if (RecoveryDeadlineUtc is null)
        {
            return this;
        }
        return new AdrDraft(Id, OrganizationId, AuthorId, Content, IntendedSupersessionTargetId, CreatedAtUtc, now, checked(Version + 1), null);
    }

    public AdrDraft Reassign(MemberId newAuthorId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(newAuthorId);
        if (RecoveryDeadlineUtc is null)
        {
            throw new InvalidOperationException($"Draft '{Id}' is not in recovery.");
        }
        return new AdrDraft(Id, OrganizationId, newAuthorId, Content, IntendedSupersessionTargetId, CreatedAtUtc, now, checked(Version + 1), null);
    }

    public bool IsExpired(DateTimeOffset now) => RecoveryDeadlineUtc is not null && now >= RecoveryDeadlineUtc;
}

public sealed class DraftConcurrencyException(AdrId draftId, long expectedVersion, long currentVersion)
    : InvalidOperationException(
        $"Draft '{draftId}' is at version {currentVersion}; version {expectedVersion} cannot overwrite it.")
{
    public AdrId DraftId { get; } = draftId;
    public long ExpectedVersion { get; } = expectedVersion;
    public long CurrentVersion { get; } = currentVersion;
}
