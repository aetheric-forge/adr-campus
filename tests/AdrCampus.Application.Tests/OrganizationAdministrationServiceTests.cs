using AdrCampus.Application.Administration;
using AdrCampus.Application.Identity;
using AdrCampus.Core.Administration;
using AdrCampus.Core.Domain;

namespace AdrCampus.Application.Tests;

public sealed class OrganizationAdministrationServiceTests
{
    private static readonly OrganizationId Organization = new("aetheric-forge");
    private static readonly MemberId Maintainer = new("maintainer");
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);
    private static OrganizationBootstrapConfiguration Configuration(string group = "members") => new(Organization, "Aetheric Forge", "https://sso", group, "maintainers");

    [Fact]
    public async Task ValidBootstrapCreatesOneOrganizationAndEvent()
    {
        var repository = new StubRepository(); var service = Create(repository);
        var result = await service.BootstrapAsync(Configuration(), OperationId.New());
        Assert.Equal(BootstrapOrganizationStatus.Created, result.Status);
        Assert.Single(repository.Events);
        Assert.Equal(AdministrationEventType.OrganizationBootstrapped, repository.Events[0].Type);
    }

    [Fact]
    public async Task InvalidDirectoryDoesNotCreatePartialOrganization()
    {
        var repository = new StubRepository(); var service = Create(repository, verified: false);
        var result = await service.BootstrapAsync(Configuration(), OperationId.New());
        Assert.Equal(BootstrapOrganizationStatus.Invalid, result.Status);
        Assert.Null(repository.State); Assert.Empty(repository.Events);
    }

    [Fact]
    public async Task ExistingOrganizationRejectsChangedAuthorityConfiguration()
    {
        var repository = new StubRepository(); var service = Create(repository);
        await service.BootstrapAsync(Configuration(), OperationId.New());
        var result = await service.BootstrapAsync(Configuration("other-members"), OperationId.New());
        Assert.Equal(BootstrapOrganizationStatus.ConfigurationMismatch, result.Status);
        Assert.Single(repository.Events);
    }

    [Fact]
    public async Task MaintainerRenamesAndRetryReturnsOriginalState()
    {
        var repository = new StubRepository(); var service = Create(repository);
        var state = (await service.BootstrapAsync(Configuration(), OperationId.New())).State!;
        var operation = OperationId.New();
        var command = new RenameOrganizationCommand(Organization, Maintainer, state.Version, "  Forge Campus  ", operation);
        var first = await service.RenameAsync(command);
        var retry = await service.RenameAsync(command);
        Assert.Equal(RenameOrganizationStatus.Saved, first.Status);
        Assert.Equal(RenameOrganizationStatus.AlreadyApplied, retry.Status);
        Assert.Equal("Forge Campus", retry.State!.DisplayName.Value);
        Assert.Equal(2, repository.Events.Count);
    }

    [Fact]
    public async Task RenameRechecksMaintainerAndVersion()
    {
        var repository = new StubRepository(); var service = Create(repository);
        var state = (await service.BootstrapAsync(Configuration(), OperationId.New())).State!;
        var unauthorized = await Create(repository, maintainer: false).RenameAsync(new(Organization, Maintainer, state.Version, "Forge Campus", OperationId.New()));
        var conflict = await service.RenameAsync(new(Organization, Maintainer, state.Version + 1, "Forge Campus", OperationId.New()));
        Assert.Equal(RenameOrganizationStatus.Unauthorized, unauthorized.Status);
        Assert.Equal(RenameOrganizationStatus.Conflict, conflict.Status);
        Assert.Equal("Aetheric Forge", repository.State!.DisplayName.Value);
    }

    [Fact]
    public async Task MemberDisplayReadDoesNotReturnSsoMappings()
    {
        var repository = new StubRepository(); var service = Create(repository);
        await service.BootstrapAsync(Configuration(), OperationId.New());
        var result = await service.GetDisplayAsync(Organization, new("member"));
        Assert.Equal(GetOrganizationDisplayStatus.Success, result.Status);
        Assert.Equal("Aetheric Forge", result.DisplayName!.Value);
        Assert.DoesNotContain(result.GetType().GetProperties(), property => property.Name.Contains("Group", StringComparison.Ordinal));
    }

    private static OrganizationAdministrationService Create(StubRepository repository, bool verified = true, bool maintainer = true) => new(repository, new Verifier(verified), new Authority(maintainer), new FixedTimeProvider(Now));
    private sealed class Verifier(bool valid) : IOrganizationBootstrapVerifier { public Task<OrganizationBootstrapVerification> VerifyAsync(OrganizationBootstrapConfiguration configuration, CancellationToken cancellationToken = default) => Task.FromResult(valid ? OrganizationBootstrapVerification.Valid : OrganizationBootstrapVerification.Invalid("invalid")); }
    private sealed class Authority(bool maintainer) : IMemberAuthority
    {
        public Task<bool> IsActiveMemberAsync(OrganizationId organizationId, MemberId memberId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> IsActiveMaintainerAsync(OrganizationId organizationId, MemberId memberId, CancellationToken cancellationToken = default) => Task.FromResult(maintainer);
    }
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    private sealed class StubRepository : IOrganizationAdministrationRepository
    {
        private readonly Dictionary<OperationId, OrganizationAdministrationState> operations = [];
        public OrganizationAdministrationState? State { get; private set; }
        public List<AdministrationEvent> Events { get; } = [];
        public Task<OrganizationAdministrationState?> GetAsync(OrganizationId organizationId, CancellationToken cancellationToken = default) => Task.FromResult(State);
        public Task<OrganizationAdministrationWriteResult> BootstrapAsync(OrganizationAdministrationState state, AdministrationEvent administrationEvent, OperationId operationId, CancellationToken cancellationToken = default)
        {
            if (operations.TryGetValue(operationId, out var prior)) return Task.FromResult(new OrganizationAdministrationWriteResult(prior == state ? OrganizationAdministrationWriteStatus.AlreadyApplied : OrganizationAdministrationWriteStatus.OperationMismatch, prior));
            if (State is not null) return Task.FromResult(new OrganizationAdministrationWriteResult(State.HasSameAuthorityConfiguration(state.SsoAuthority, state.MemberGroupReference, state.MaintainerGroupReference) ? OrganizationAdministrationWriteStatus.AlreadyApplied : OrganizationAdministrationWriteStatus.ConfigurationMismatch, State));
            State = state; Events.Add(administrationEvent); operations[operationId] = state; return Task.FromResult(new OrganizationAdministrationWriteResult(OrganizationAdministrationWriteStatus.Created, state));
        }
        public Task<OrganizationAdministrationWriteResult> RenameAsync(OrganizationAdministrationState state, long expectedVersion, AdministrationEvent administrationEvent, OperationId operationId, CancellationToken cancellationToken = default)
        {
            if (operations.TryGetValue(operationId, out var prior)) return Task.FromResult(new OrganizationAdministrationWriteResult(prior == state ? OrganizationAdministrationWriteStatus.AlreadyApplied : OrganizationAdministrationWriteStatus.OperationMismatch, prior));
            if (State?.Version != expectedVersion) return Task.FromResult(new OrganizationAdministrationWriteResult(OrganizationAdministrationWriteStatus.Conflict, State));
            State = state; Events.Add(administrationEvent); operations[operationId] = state; return Task.FromResult(new OrganizationAdministrationWriteResult(OrganizationAdministrationWriteStatus.Saved, state));
        }
        public Task<IReadOnlyList<AdministrationEvent>> ListEventsAsync(OrganizationId organizationId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AdministrationEvent>>(Events);
    }
}
