using AdrCampus.Application.Administration;
using AdrCampus.Core.Domain;

namespace AdrCampus.Shell.Members;

public sealed class OrganizationBootstrapHostedService(
    IServiceScopeFactory scopeFactory,
    OrganizationBootstrapHealth health,
    ILogger<OrganizationBootstrapHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var configuration = scope.ServiceProvider.GetRequiredService<OrganizationBootstrapConfiguration>();
        var administration = scope.ServiceProvider.GetRequiredService<OrganizationAdministrationService>();
        var result = await administration.BootstrapAsync(
            configuration,
            OperationId.New(),
            cancellationToken).ConfigureAwait(false);

        health.Record(result);
        if (!result.IsSuccess)
        {
            logger.LogError(
                "Organization bootstrap was not completed: {BootstrapStatus} {BootstrapError}",
                result.Status,
                result.ErrorMessage);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
