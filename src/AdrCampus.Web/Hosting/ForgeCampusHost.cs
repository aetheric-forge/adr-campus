using AethericForge.Runtime.Institutions.Campus;

namespace AdrCampus.Web.Hosting;

public sealed class ForgeCampusHost(
    ICampus campus,
    ILogger<ForgeCampusHost> logger) : IHostedService
{
    public bool IsRunning { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await campus.InitializeAsync(cancellationToken);
        await campus.StartAsync(cancellationToken);
        IsRunning = true;

        logger.LogInformation(
            "Institution {InstitutionName} {InstitutionVersion} started",
            campus.Context.Template.Descriptor.Name,
            campus.Context.Template.Descriptor.Version);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!IsRunning)
        {
            return;
        }

        IsRunning = false;
        await campus.StopAsync(cancellationToken);

        logger.LogInformation(
            "Institution {InstitutionName} stopped",
            campus.Context.Template.Descriptor.Name);
    }
}
