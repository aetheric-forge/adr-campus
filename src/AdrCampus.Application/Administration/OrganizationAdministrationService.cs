using AdrCampus.Application.Identity;
using AdrCampus.Core.Administration;
using AdrCampus.Core.Domain;

namespace AdrCampus.Application.Administration;

public interface IOrganizationBootstrapVerifier
{
    Task<OrganizationBootstrapVerification> VerifyAsync(OrganizationBootstrapConfiguration configuration, CancellationToken cancellationToken = default);
}

public sealed record OrganizationBootstrapConfiguration(OrganizationId OrganizationId, string DisplayName, string SsoAuthority, string MemberGroupReference, string MaintainerGroupReference);
public sealed record OrganizationBootstrapVerification(bool IsValid, string? ErrorMessage)
{
    public static OrganizationBootstrapVerification Valid { get; } = new(true, null);
    public static OrganizationBootstrapVerification Invalid(string message) => new(false, message);
}

public sealed class OrganizationAdministrationService(IOrganizationAdministrationRepository repository, IOrganizationBootstrapVerifier bootstrapVerifier, IMemberAuthority memberAuthority, TimeProvider timeProvider)
{
    public async Task<BootstrapOrganizationResult> BootstrapAsync(OrganizationBootstrapConfiguration configuration, OperationId operationId, CancellationToken cancellationToken = default)
    {
        OrganizationDisplayName name;
        try { name = new(configuration.DisplayName); }
        catch (OrganizationNameValidationException exception) { return BootstrapOrganizationResult.Invalid(exception.Message); }
        var existing = await repository.GetAsync(configuration.OrganizationId, cancellationToken);
        if (existing is not null)
            return existing.HasSameAuthorityConfiguration(configuration.SsoAuthority, configuration.MemberGroupReference, configuration.MaintainerGroupReference) ? BootstrapOrganizationResult.AlreadyInitialized(existing) : BootstrapOrganizationResult.ConfigurationMismatch(existing);
        var verification = await bootstrapVerifier.VerifyAsync(configuration, cancellationToken);
        if (!verification.IsValid) return BootstrapOrganizationResult.Invalid(verification.ErrorMessage ?? "The SSO configuration could not be verified.");
        var now = timeProvider.GetUtcNow();
        var state = OrganizationAdministrationState.Bootstrap(configuration.OrganizationId, name, configuration.SsoAuthority, configuration.MemberGroupReference, configuration.MaintainerGroupReference, now);
        var evt = new AdministrationEvent(Guid.NewGuid(), configuration.OrganizationId, AdministrationEventType.OrganizationBootstrapped, now, "SSO configuration", NewValue: name.Value);
        var write = await repository.BootstrapAsync(state, evt, operationId, cancellationToken);
        return write.Status switch
        {
            OrganizationAdministrationWriteStatus.Created => BootstrapOrganizationResult.Created(write.State!),
            OrganizationAdministrationWriteStatus.AlreadyApplied => BootstrapOrganizationResult.AlreadyInitialized(write.State!),
            OrganizationAdministrationWriteStatus.ConfigurationMismatch => BootstrapOrganizationResult.ConfigurationMismatch(write.State),
            OrganizationAdministrationWriteStatus.OperationMismatch => BootstrapOrganizationResult.OperationMismatch(),
            _ => BootstrapOrganizationResult.Conflict(write.State)
        };
    }

    public async Task<GetOrganizationAdministrationResult> GetAsync(OrganizationId organizationId, MemberId memberId, CancellationToken cancellationToken = default)
    {
        if (!await memberAuthority.IsActiveMaintainerAsync(organizationId, memberId, cancellationToken)) return GetOrganizationAdministrationResult.Unauthorized();
        var state = await repository.GetAsync(organizationId, cancellationToken);
        return state is null ? GetOrganizationAdministrationResult.NotInitialized() : GetOrganizationAdministrationResult.Success(state);
    }

    public async Task<GetOrganizationDisplayResult> GetDisplayAsync(OrganizationId organizationId, MemberId memberId, CancellationToken cancellationToken = default)
    {
        if (!await memberAuthority.IsActiveMemberAsync(organizationId, memberId, cancellationToken)) return GetOrganizationDisplayResult.Unauthorized();
        var state = await repository.GetAsync(organizationId, cancellationToken);
        return state is null ? GetOrganizationDisplayResult.NotInitialized() : GetOrganizationDisplayResult.Success(state.DisplayName);
    }

    public async Task<RenameOrganizationResult> RenameAsync(RenameOrganizationCommand command, CancellationToken cancellationToken = default)
    {
        if (!await memberAuthority.IsActiveMaintainerAsync(command.OrganizationId, command.MaintainerId, cancellationToken)) return RenameOrganizationResult.Unauthorized();
        var current = await repository.GetAsync(command.OrganizationId, cancellationToken);
        if (current is null) return RenameOrganizationResult.NotInitialized();
        OrganizationDisplayName name;
        try { name = new(command.DisplayName); }
        catch (OrganizationNameValidationException exception) { return RenameOrganizationResult.Invalid(exception.Code, exception.Message, current); }
        if (name == current.DisplayName && current.Version == command.ExpectedVersion) return RenameOrganizationResult.Unchanged(current);
        var now = timeProvider.GetUtcNow();
        var renamed = OrganizationAdministrationState.Restore(
            current.OrganizationId, name, current.SsoAuthority, current.MemberGroupReference,
            current.MaintainerGroupReference, current.InitializedAtUtc, now, checked(command.ExpectedVersion + 1));
        var evt = new AdministrationEvent(Guid.NewGuid(), command.OrganizationId, AdministrationEventType.OrganizationRenamed, now, "ADR Campus", command.MaintainerId, current.DisplayName.Value, name.Value);
        var write = await repository.RenameAsync(renamed, command.ExpectedVersion, evt, command.OperationId, cancellationToken);
        return write.Status switch
        {
            OrganizationAdministrationWriteStatus.Saved => RenameOrganizationResult.Saved(write.State!),
            OrganizationAdministrationWriteStatus.AlreadyApplied => RenameOrganizationResult.AlreadyApplied(write.State!),
            OrganizationAdministrationWriteStatus.OperationMismatch => RenameOrganizationResult.OperationMismatch(current),
            _ => RenameOrganizationResult.Conflict(write.State ?? current)
        };
    }
}

public enum BootstrapOrganizationStatus { Created, AlreadyInitialized, Invalid, Conflict, ConfigurationMismatch, OperationMismatch }
public sealed record BootstrapOrganizationResult(BootstrapOrganizationStatus Status, OrganizationAdministrationState? State, string? ErrorMessage)
{
    public bool IsSuccess => Status is BootstrapOrganizationStatus.Created or BootstrapOrganizationStatus.AlreadyInitialized;
    public static BootstrapOrganizationResult Created(OrganizationAdministrationState state) => new(BootstrapOrganizationStatus.Created, state, null);
    public static BootstrapOrganizationResult AlreadyInitialized(OrganizationAdministrationState state) => new(BootstrapOrganizationStatus.AlreadyInitialized, state, null);
    public static BootstrapOrganizationResult Invalid(string message) => new(BootstrapOrganizationStatus.Invalid, null, message);
    public static BootstrapOrganizationResult Conflict(OrganizationAdministrationState? state) => new(BootstrapOrganizationStatus.Conflict, state, "Organization initialization conflicted with another operation.");
    public static BootstrapOrganizationResult ConfigurationMismatch(OrganizationAdministrationState? state) => new(BootstrapOrganizationStatus.ConfigurationMismatch, state, "The persisted organization uses different SSO authority or group mappings.");
    public static BootstrapOrganizationResult OperationMismatch() => new(BootstrapOrganizationStatus.OperationMismatch, null, "This operation identifier was already used for different work.");
}
public enum GetOrganizationAdministrationStatus { Success, NotInitialized, Unauthorized }
public sealed record GetOrganizationAdministrationResult(GetOrganizationAdministrationStatus Status, OrganizationAdministrationState? State)
{
    public static GetOrganizationAdministrationResult Success(OrganizationAdministrationState state) => new(GetOrganizationAdministrationStatus.Success, state);
    public static GetOrganizationAdministrationResult NotInitialized() => new(GetOrganizationAdministrationStatus.NotInitialized, null);
    public static GetOrganizationAdministrationResult Unauthorized() => new(GetOrganizationAdministrationStatus.Unauthorized, null);
}
public enum GetOrganizationDisplayStatus { Success, NotInitialized, Unauthorized }
public sealed record GetOrganizationDisplayResult(GetOrganizationDisplayStatus Status, OrganizationDisplayName? DisplayName)
{
    public static GetOrganizationDisplayResult Success(OrganizationDisplayName name) => new(GetOrganizationDisplayStatus.Success, name);
    public static GetOrganizationDisplayResult NotInitialized() => new(GetOrganizationDisplayStatus.NotInitialized, null);
    public static GetOrganizationDisplayResult Unauthorized() => new(GetOrganizationDisplayStatus.Unauthorized, null);
}
public sealed record RenameOrganizationCommand(OrganizationId OrganizationId, MemberId MaintainerId, long ExpectedVersion, string DisplayName, OperationId OperationId);
public enum RenameOrganizationStatus { Saved, AlreadyApplied, Unchanged, Invalid, Unauthorized, NotInitialized, Conflict, OperationMismatch }
public sealed record RenameOrganizationResult(RenameOrganizationStatus Status, OrganizationAdministrationState? State, OrganizationNameValidationCode? ValidationCode, string? ErrorMessage)
{
    public bool IsSuccess => Status is RenameOrganizationStatus.Saved or RenameOrganizationStatus.AlreadyApplied or RenameOrganizationStatus.Unchanged;
    public static RenameOrganizationResult Saved(OrganizationAdministrationState state) => new(RenameOrganizationStatus.Saved, state, null, null);
    public static RenameOrganizationResult AlreadyApplied(OrganizationAdministrationState state) => new(RenameOrganizationStatus.AlreadyApplied, state, null, null);
    public static RenameOrganizationResult Unchanged(OrganizationAdministrationState state) => new(RenameOrganizationStatus.Unchanged, state, null, null);
    public static RenameOrganizationResult Invalid(OrganizationNameValidationCode code, string message, OrganizationAdministrationState state) => new(RenameOrganizationStatus.Invalid, state, code, message);
    public static RenameOrganizationResult Unauthorized() => new(RenameOrganizationStatus.Unauthorized, null, null, "Current Maintainer authority could not be established.");
    public static RenameOrganizationResult NotInitialized() => new(RenameOrganizationStatus.NotInitialized, null, null, "The organization has not been initialized.");
    public static RenameOrganizationResult Conflict(OrganizationAdministrationState state) => new(RenameOrganizationStatus.Conflict, state, null, "The organization changed after this page was opened. Reload before trying again.");
    public static RenameOrganizationResult OperationMismatch(OrganizationAdministrationState state) => new(RenameOrganizationStatus.OperationMismatch, state, null, "This operation identifier was already used for different work.");
}
