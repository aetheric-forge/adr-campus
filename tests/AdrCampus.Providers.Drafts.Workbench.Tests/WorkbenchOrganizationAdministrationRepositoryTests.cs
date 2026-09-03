using AdrCampus.Core.Administration;
using AdrCampus.Core.Domain;
using AdrCampus.Providers.Drafts.Workbench;
using AethericForge.Runtime.Providers.Staging.InMemory;
using AethericForge.Runtime.Abstractions.Interfaces.Staging.Primitives;
using AethericForge.Runtime.Abstractions.Interfaces.Staging.Providers;

namespace AdrCampus.Providers.Drafts.Workbench.Tests;

public sealed class WorkbenchOrganizationAdministrationRepositoryTests
{
    private static readonly OrganizationId Organization = new("aetheric-forge");
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);
    private static OrganizationAdministrationState State() => OrganizationAdministrationState.Bootstrap(Organization, new("Aetheric Forge"), "https://sso", "members", "maintainers", Now);
    private static AdministrationEvent Event(AdministrationEventType type, DateTimeOffset at, string? previous = null, string? next = null) => new(Guid.NewGuid(), Organization, type, at, "test", new("maintainer"), previous, next);

    [Fact]
    public async Task BootstrapAndEventSurviveRecomposition()
    {
        var staging = new InMemoryStagingProvider("administration"); var repository = new WorkbenchOrganizationAdministrationRepository(staging); var state = State();
        await repository.BootstrapAsync(state, Event(AdministrationEventType.OrganizationBootstrapped, Now), OperationId.New());
        var recomposed = new WorkbenchOrganizationAdministrationRepository(staging);
        Assert.Equal(state, await recomposed.GetAsync(Organization));
        Assert.Single(await recomposed.ListEventsAsync(Organization));
    }

    [Fact]
    public async Task RenameIsAtomicVersionedAndIdempotent()
    {
        var staging = new InMemoryStagingProvider("administration"); var repository = new WorkbenchOrganizationAdministrationRepository(staging); var state = State();
        await repository.BootstrapAsync(state, Event(AdministrationEventType.OrganizationBootstrapped, Now), OperationId.New());
        var renamed = state.Rename(new("Forge Campus"), 1, Now.AddMinutes(1)); var operation = OperationId.New(); var evt = Event(AdministrationEventType.OrganizationRenamed, Now.AddMinutes(1), state.DisplayName.Value, renamed.DisplayName.Value);
        var saved = await repository.RenameAsync(renamed, 1, evt, operation);
        var retry = await new WorkbenchOrganizationAdministrationRepository(staging).RenameAsync(renamed, 1, evt, operation);
        Assert.Equal(OrganizationAdministrationWriteStatus.Saved, saved.Status);
        Assert.Equal(OrganizationAdministrationWriteStatus.AlreadyApplied, retry.Status);
        Assert.Equal(2, (await repository.ListEventsAsync(Organization)).Count);
    }

    [Fact]
    public async Task BootstrapRefusesChangedSsoMapping()
    {
        var repository = new WorkbenchOrganizationAdministrationRepository(new InMemoryStagingProvider("administration")); var state = State();
        await repository.BootstrapAsync(state, Event(AdministrationEventType.OrganizationBootstrapped, Now), OperationId.New());
        var changed = OrganizationAdministrationState.Bootstrap(Organization, state.DisplayName, "https://sso", "other", "maintainers", Now);
        var result = await repository.BootstrapAsync(changed, Event(AdministrationEventType.OrganizationBootstrapped, Now), OperationId.New());
        Assert.Equal(OrganizationAdministrationWriteStatus.ConfigurationMismatch, result.Status);
    }

    [Fact]
    public async Task FailedRenamePersistsNeitherStateNorEvent()
    {
        var staging = new FailNextPutStagingProvider(new InMemoryStagingProvider("administration")); var repository = new WorkbenchOrganizationAdministrationRepository(staging); var state = State();
        await repository.BootstrapAsync(state, Event(AdministrationEventType.OrganizationBootstrapped, Now), OperationId.New());
        var renamed = state.Rename(new("Forge Campus"), 1, Now.AddMinutes(1)); staging.FailNextPut = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.RenameAsync(renamed, 1, Event(AdministrationEventType.OrganizationRenamed, Now.AddMinutes(1)), OperationId.New()));
        Assert.Equal(state, await repository.GetAsync(Organization));
        Assert.Single(await repository.ListEventsAsync(Organization));
    }

    private sealed class FailNextPutStagingProvider(IStagingProvider inner) : IStagingProvider
    {
        public bool FailNextPut { get; set; }
        public string Stage => inner.Stage;
        public Task<IStagingReference> PutAsync(string key, Stream content, IStagingMetadata? metadata = null, CancellationToken ct = default) { if (FailNextPut) { FailNextPut = false; throw new InvalidOperationException("Injected persistence failure."); } return inner.PutAsync(key, content, metadata, ct); }
        public Task<Stream> OpenReadAsync(IStagingReference reference, CancellationToken ct = default) => inner.OpenReadAsync(reference, ct);
        public Task<IStagingMetadata?> StatAsync(IStagingReference reference, CancellationToken ct = default) => inner.StatAsync(reference, ct);
        public Task<bool> ExistsAsync(IStagingReference reference, CancellationToken ct = default) => inner.ExistsAsync(reference, ct);
        public Task<bool> DeleteAsync(IStagingReference reference, CancellationToken ct = default) => inner.DeleteAsync(reference, ct);
        public Task<IStagingObject?> GetAsync(IStagingReference reference, CancellationToken ct = default) => inner.GetAsync(reference, ct);
        public Task PinAsync(IStagingReference reference, CancellationToken ct = default) => inner.PinAsync(reference, ct);
        public Task UnpinAsync(IStagingReference reference, CancellationToken ct = default) => inner.UnpinAsync(reference, ct);
        public Task<IStagingLock> AcquireLockAsync(IStagingReference reference, TimeSpan? timeout = null, CancellationToken ct = default) => inner.AcquireLockAsync(reference, timeout, ct);
    }
}
