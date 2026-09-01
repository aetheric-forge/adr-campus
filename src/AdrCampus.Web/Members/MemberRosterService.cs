using AethericForge.Runtime.Abstractions.Interfaces.Identity.Directory;
using AethericForge.Runtime.Models.Identity.Directory;
using AethericForge.Runtime.Providers.Identity.Keycloak;

namespace AdrCampus.Web.Members;

public sealed class MemberRosterService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<MemberRosterService> logger)
{
    public const string HttpClientName = "KeycloakDirectory";

    public async Task<bool> IsActiveMemberAsync(
        string subjectId,
        CancellationToken cancellationToken = default)
    {
        var membership = await GetMembershipAsync(subjectId, cancellationToken).ConfigureAwait(false);
        return membership.IsAvailable && membership.IsMember;
    }

    public async Task<CurrentMembership> GetMembershipAsync(
        string subjectId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            return CurrentMembership.NotAMember;
        }

        var roster = await GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!roster.IsAvailable)
        {
            return CurrentMembership.Unavailable;
        }

        var member = roster.Members.FirstOrDefault(member =>
            string.Equals(member.SubjectId, subjectId, StringComparison.Ordinal));
        return member is null
            ? CurrentMembership.NotAMember
            : new CurrentMembership(true, true, member.IsMaintainer);
    }

    public async Task<MemberRosterResult> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var keycloak = configuration.GetSection("Keycloak").Get<KeycloakOptions>() ?? new KeycloakOptions();
            var groups = configuration.GetSection("Organization").Get<OrganizationDirectoryOptions>()
                ?? new OrganizationDirectoryOptions();
            var memberGroupId = Required(groups.MemberGroupId, "Organization:MemberGroupId");
            var maintainerGroupId = Required(groups.MaintainerGroupId, "Organization:MaintainerGroupId");

            using var directory = new KeycloakExternalIdentityDirectory(
                httpClientFactory.CreateClient(HttpClientName),
                keycloak);
            var memberTask = GetGroupMembersAsync(directory, memberGroupId, cancellationToken);
            var maintainerTask = GetGroupMembersAsync(directory, maintainerGroupId, cancellationToken);
            await Task.WhenAll(memberTask, maintainerTask).ConfigureAwait(false);

            var members = await memberTask.ConfigureAwait(false);
            var maintainers = await maintainerTask.ConfigureAwait(false);
            if (members.Status != ExternalDirectoryStatus.Success ||
                maintainers.Status != ExternalDirectoryStatus.Success)
            {
                var failure = members.Status != ExternalDirectoryStatus.Success ? members : maintainers;
                logger.LogWarning(
                    "The current member roster could not be loaded. Directory status: {DirectoryStatus}",
                    failure.Status);
                return MemberRosterResult.Unavailable(MessageFor(failure.Status));
            }

            var maintainerIds = maintainers.Value!
                .Where(identity => identity.IsEnabled)
                .Select(identity => identity.Reference.SubjectId)
                .ToHashSet(StringComparer.Ordinal);
            var activeMembers = members.Value!
                .Where(identity => identity.IsEnabled)
                .GroupBy(identity => identity.Reference.SubjectId, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
            var memberIds = activeMembers
                .Select(identity => identity.Reference.SubjectId)
                .ToHashSet(StringComparer.Ordinal);
            var orphanedMaintainerCount = maintainerIds.Count(subjectId => !memberIds.Contains(subjectId));
            if (orphanedMaintainerCount > 0)
            {
                logger.LogError(
                    "The maintainer group contains {InvalidMaintainerCount} enabled identities that are not active members.",
                    orphanedMaintainerCount);
                return MemberRosterResult.Unavailable(
                    "The organization role configuration is invalid. Every maintainer must also be an active member.");
            }

            var roster = activeMembers
                .Select(identity => new MemberRosterItem(
                    identity.Reference.SubjectId,
                    identity.DisplayName ?? "Unnamed member",
                    maintainerIds.Contains(identity.Reference.SubjectId)))
                .OrderBy(member => member.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(member => member.SubjectId, StringComparer.Ordinal)
                .ToArray();

            var observedAt = members.ObservedAtUtc > maintainers.ObservedAtUtc
                ? members.ObservedAtUtc
                : maintainers.ObservedAtUtc;
            return MemberRosterResult.Success(roster, observedAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            logger.LogWarning(exception, "The member roster directory is not configured correctly.");
            return MemberRosterResult.Unavailable(
                "The member directory is not configured. Ask a deployment operator to check the SSO settings.");
        }
    }

    private static string Required(string value, string configurationKey)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Configuration value '{configurationKey}' is required.");
        }
        return value.Trim();
    }

    private static async Task<IExternalDirectoryResult<IReadOnlyCollection<IExternalIdentity>>> GetGroupMembersAsync(
        KeycloakExternalIdentityDirectory directory,
        string configuredGroup,
        CancellationToken cancellationToken)
    {
        var configuredReference = new ExternalGroupReference(
            directory.Provider,
            directory.Realm,
            configuredGroup);
        var members = await directory.GetGroupMembersAsync(configuredReference, cancellationToken)
            .ConfigureAwait(false);
        if (members.Status != ExternalDirectoryStatus.NotFound)
        {
            return members;
        }

        var resolved = await directory.ResolveGroupAsync(configuredGroup, cancellationToken)
            .ConfigureAwait(false);
        return resolved.Status == ExternalDirectoryStatus.Success
            ? await directory.GetGroupMembersAsync(resolved.Value!, cancellationToken).ConfigureAwait(false)
            : ExternalDirectoryResult<IReadOnlyCollection<IExternalIdentity>>.Failure(
                resolved.Status,
                resolved.ObservedAtUtc,
                resolved.FailureReason);
    }

    private static string MessageFor(ExternalDirectoryStatus status) => status switch
    {
        ExternalDirectoryStatus.Misconfigured or ExternalDirectoryStatus.Untrusted =>
            "The member directory configuration could not be verified. Ask a deployment operator to check the SSO settings.",
        _ => "The current roster is temporarily unavailable. No partial or stale member information is being shown."
    };

    private sealed class OrganizationDirectoryOptions
    {
        public string MemberGroupId { get; set; } = string.Empty;
        public string MaintainerGroupId { get; set; } = string.Empty;
    }
}

public sealed record MemberRosterItem(string SubjectId, string DisplayName, bool IsMaintainer);

public sealed record CurrentMembership(bool IsAvailable, bool IsMember, bool IsMaintainer)
{
    public static CurrentMembership Unavailable { get; } = new(false, false, false);
    public static CurrentMembership NotAMember { get; } = new(true, false, false);
}

public sealed record MemberRosterResult(
    bool IsAvailable,
    IReadOnlyCollection<MemberRosterItem> Members,
    DateTimeOffset? ObservedAtUtc,
    string? ErrorMessage)
{
    public static MemberRosterResult Success(
        IReadOnlyCollection<MemberRosterItem> members,
        DateTimeOffset observedAtUtc) => new(true, members, observedAtUtc, null);

    public static MemberRosterResult Unavailable(string errorMessage) =>
        new(false, Array.Empty<MemberRosterItem>(), null, errorMessage);
}
