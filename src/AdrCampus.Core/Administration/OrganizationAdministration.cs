using AdrCampus.Core.Domain;

namespace AdrCampus.Core.Administration;

public enum OrganizationNameValidationCode { Required, TooShort, TooLong, MissingLetterOrNumber, ContainsControlCharacter }

public sealed class OrganizationNameValidationException(OrganizationNameValidationCode code, string message) : ArgumentException(message)
{
    public OrganizationNameValidationCode Code { get; } = code;
}

public sealed record OrganizationDisplayName
{
    public OrganizationDisplayName(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0) throw Invalid(OrganizationNameValidationCode.Required, "An organization name is required.");
        if (normalized.Length < 3) throw Invalid(OrganizationNameValidationCode.TooShort, "The organization name must contain at least 3 characters.");
        if (normalized.Length > 100) throw Invalid(OrganizationNameValidationCode.TooLong, "The organization name cannot contain more than 100 characters.");
        if (!normalized.Any(char.IsLetterOrDigit)) throw Invalid(OrganizationNameValidationCode.MissingLetterOrNumber, "The organization name must contain at least one letter or number.");
        if (normalized.Any(char.IsControl)) throw Invalid(OrganizationNameValidationCode.ContainsControlCharacter, "The organization name cannot contain control characters.");
        Value = normalized;
    }

    public string Value { get; }
    public override string ToString() => Value;
    private static OrganizationNameValidationException Invalid(OrganizationNameValidationCode code, string message) => new(code, message);
}

public sealed record OrganizationAdministrationState
{
    private OrganizationAdministrationState(OrganizationId organizationId, OrganizationDisplayName displayName, string ssoAuthority, string memberGroupReference, string maintainerGroupReference, DateTimeOffset initializedAtUtc, DateTimeOffset modifiedAtUtc, long version)
    {
        OrganizationId = organizationId;
        DisplayName = displayName;
        SsoAuthority = Required(ssoAuthority, nameof(ssoAuthority));
        MemberGroupReference = Required(memberGroupReference, nameof(memberGroupReference));
        MaintainerGroupReference = Required(maintainerGroupReference, nameof(maintainerGroupReference));
        if (MemberGroupReference == MaintainerGroupReference) throw new ArgumentException("The member and maintainer groups must be distinct.");
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        if (modifiedAtUtc < initializedAtUtc) throw new ArgumentOutOfRangeException(nameof(modifiedAtUtc));
        InitializedAtUtc = initializedAtUtc; ModifiedAtUtc = modifiedAtUtc; Version = version;
    }

    public OrganizationId OrganizationId { get; }
    public OrganizationDisplayName DisplayName { get; }
    public string SsoAuthority { get; }
    public string MemberGroupReference { get; }
    public string MaintainerGroupReference { get; }
    public DateTimeOffset InitializedAtUtc { get; }
    public DateTimeOffset ModifiedAtUtc { get; }
    public long Version { get; }

    public static OrganizationAdministrationState Bootstrap(OrganizationId id, OrganizationDisplayName name, string authority, string memberGroup, string maintainerGroup, DateTimeOffset now) => new(id, name, authority, memberGroup, maintainerGroup, now, now, 1);
    public static OrganizationAdministrationState Restore(OrganizationId id, OrganizationDisplayName name, string authority, string memberGroup, string maintainerGroup, DateTimeOffset initialized, DateTimeOffset modified, long version) => new(id, name, authority, memberGroup, maintainerGroup, initialized, modified, version);
    public OrganizationAdministrationState Rename(OrganizationDisplayName name, long expectedVersion, DateTimeOffset now)
    {
        if (expectedVersion != Version) throw new OrganizationAdministrationConcurrencyException(expectedVersion, Version);
        if (now < ModifiedAtUtc) throw new ArgumentOutOfRangeException(nameof(now));
        return new(OrganizationId, name, SsoAuthority, MemberGroupReference, MaintainerGroupReference, InitializedAtUtc, now, checked(Version + 1));
    }
    public bool HasSameAuthorityConfiguration(string authority, string memberGroup, string maintainerGroup) => SsoAuthority == authority.Trim() && MemberGroupReference == memberGroup.Trim() && MaintainerGroupReference == maintainerGroup.Trim();
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A configured value is required.", name) : value.Trim();
}

public sealed class OrganizationAdministrationConcurrencyException(long expectedVersion, long currentVersion) : InvalidOperationException($"The organization is at version {currentVersion}; version {expectedVersion} cannot overwrite it.")
{
    public long ExpectedVersion { get; } = expectedVersion;
    public long CurrentVersion { get; } = currentVersion;
}

public enum AdministrationEventType { OrganizationBootstrapped, OrganizationRenamed, MemberAdded, MemberRemoved, MaintainerGranted, MaintainerRevoked, MemberDisplayNameChanged, DraftRecoveryStarted, DraftRecoveryCancelled, DraftReassigned, DraftExpired }
public sealed record AdministrationEvent(Guid Id, OrganizationId OrganizationId, AdministrationEventType Type, DateTimeOffset OccurredAtUtc, string Source, MemberId? ActorId = null, string? PreviousValue = null, string? NewValue = null, MemberId? SubjectId = null, AdrId? DraftId = null);
