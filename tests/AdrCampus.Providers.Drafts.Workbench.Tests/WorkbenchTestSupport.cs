using AethericForge.Runtime.Abstractions.Interfaces.Authorities;
using AethericForge.Runtime.Abstractions.Interfaces.Staging.Providers;
using AethericForge.Runtime.Abstractions.Interfaces.Staging.Services;
using AethericForge.Runtime.Abstractions.Interfaces.Workbench.Services;
using AethericForge.Runtime.Models.Authorities;
using AethericForge.Runtime.Providers.Staging.InMemory;
using AethericForge.Runtime.Services.Staging;
using AethericForge.Runtime.Services.Workbench;

namespace AdrCampus.Providers.Drafts.Workbench.Tests;

internal static class WorkbenchTestSupport
{
    /// <summary>
    /// WorkbenchDraftRepository always addresses the "adr-campus-workbench" stage, so every staging
    /// provider used in these tests must be registered under that name.
    /// </summary>
    public const string Stage = "adr-campus-workbench";

    public static InMemoryStagingProvider NewStagingProvider() => new(Stage);

    public static IArtificer Artificer(IStagingProvider staging) =>
        new Artificer(new StagingService([staging]), new Team<IWorkbenchWorker>([]));
}
