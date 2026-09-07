using AdrCampus.Core.Administration;
using AdrCampus.Core.Domain;

namespace AdrCampus.Providers.Archive.Tests;

public sealed class ArchiveOrganizationAdministrationRepositoryTests
{
    private static readonly OrganizationId Organization = new("aetheric-forge");
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);
    private static OrganizationAdministrationState State() => OrganizationAdministrationState.Bootstrap(Organization, new("Aetheric Forge"), "https://sso", "members", "maintainers", Now);
    private static AdministrationEvent Event(AdministrationEventType type, DateTimeOffset at, string? previous = null, string? next = null) => new(Guid.NewGuid(), Organization, type, at, "test", new("maintainer"), previous, next);

    [Fact]
    public async Task BootstrapAndEventSurviveRecomposition()
    {
        var provider = ArchiveTestSupport.NewProvider();
        var repository = new ArchiveOrganizationAdministrationRepository(ArchiveTestSupport.Archivist(provider));
        var state = State();
        await repository.BootstrapAsync(state, Event(AdministrationEventType.OrganizationBootstrapped, Now), OperationId.New());
        var recomposed = new ArchiveOrganizationAdministrationRepository(ArchiveTestSupport.Archivist(provider));
        Assert.Equal(state, await recomposed.GetAsync(Organization));
        Assert.Single(await recomposed.ListEventsAsync(Organization));
    }

    [Fact]
    public async Task RenameIsAtomicVersionedAndIdempotent()
    {
        var provider = ArchiveTestSupport.NewProvider();
        var repository = new ArchiveOrganizationAdministrationRepository(ArchiveTestSupport.Archivist(provider));
        var state = State();
        await repository.BootstrapAsync(state, Event(AdministrationEventType.OrganizationBootstrapped, Now), OperationId.New());
        var renamed = state.Rename(new("Forge Campus"), 1, Now.AddMinutes(1));
        var operation = OperationId.New();
        var evt = Event(AdministrationEventType.OrganizationRenamed, Now.AddMinutes(1), state.DisplayName.Value, renamed.DisplayName.Value);
        var saved = await repository.RenameAsync(renamed, 1, evt, operation);
        var retry = await new ArchiveOrganizationAdministrationRepository(ArchiveTestSupport.Archivist(provider)).RenameAsync(renamed, 1, evt, operation);
        Assert.Equal(OrganizationAdministrationWriteStatus.Saved, saved.Status);
        Assert.Equal(OrganizationAdministrationWriteStatus.AlreadyApplied, retry.Status);
        Assert.Equal(2, (await repository.ListEventsAsync(Organization)).Count);
    }

    [Fact]
    public async Task BootstrapRefusesChangedSsoMapping()
    {
        var repository = new ArchiveOrganizationAdministrationRepository(ArchiveTestSupport.Archivist(ArchiveTestSupport.NewProvider()));
        var state = State();
        await repository.BootstrapAsync(state, Event(AdministrationEventType.OrganizationBootstrapped, Now), OperationId.New());
        var changed = OrganizationAdministrationState.Bootstrap(Organization, state.DisplayName, "https://sso", "other", "maintainers", Now);
        var result = await repository.BootstrapAsync(changed, Event(AdministrationEventType.OrganizationBootstrapped, Now), OperationId.New());
        Assert.Equal(OrganizationAdministrationWriteStatus.ConfigurationMismatch, result.Status);
    }

    [Fact]
    public async Task FailedRenamePersistsNeitherStateNorEvent()
    {
        var provider = new ArchiveTestSupport.FailNextPutArchiveProvider(ArchiveTestSupport.NewProvider());
        var repository = new ArchiveOrganizationAdministrationRepository(ArchiveTestSupport.Archivist(provider));
        var state = State();
        await repository.BootstrapAsync(state, Event(AdministrationEventType.OrganizationBootstrapped, Now), OperationId.New());
        var renamed = state.Rename(new("Forge Campus"), 1, Now.AddMinutes(1));
        provider.FailNextPut = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.RenameAsync(renamed, 1, Event(AdministrationEventType.OrganizationRenamed, Now.AddMinutes(1)), OperationId.New()));
        Assert.Equal(state, await repository.GetAsync(Organization));
        Assert.Single(await repository.ListEventsAsync(Organization));
    }
}
