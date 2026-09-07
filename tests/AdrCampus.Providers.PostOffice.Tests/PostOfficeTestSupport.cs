using AethericForge.Runtime.Abstractions.Interfaces.Authorities;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Providers;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Services;
using AethericForge.Runtime.Abstractions.Interfaces.Staging.Providers;
using AethericForge.Runtime.Abstractions.Interfaces.Staging.Services;
using AethericForge.Runtime.Abstractions.Interfaces.Workbench.Services;
using AethericForge.Runtime.Institutions.Abstractions.Builders;
using AethericForge.Runtime.Institutions.PostOffice;
using AethericForge.Runtime.Institutions.Workbench;
using AethericForge.Runtime.Models.Authorities;
using AethericForge.Runtime.Providers.Post.InMemory;
using AethericForge.Runtime.Providers.Staging.InMemory;
using AethericForge.Runtime.Services.Post;
using AethericForge.Runtime.Services.Staging;
using AethericForge.Runtime.Services.Workbench;
using Microsoft.Extensions.DependencyInjection;

namespace AdrCampus.Providers.PostOffice.Tests;

internal static class PostOfficeTestSupport
{
    /// <summary>
    /// Builds a real Post Office institution backed by a fresh Workbench (see
    /// AethericForge.Runtime.Services.Post.PostExchange, which routes envelopes through the Workbench
    /// institution's WorkbenchService), matching the composition ForgeCampusExtensions performs.
    /// </summary>
    public static IPostOffice NewPostOffice()
    {
        var staging = new InMemoryStagingProvider("adr-campus-workbench");
        var workbenchService = new WorkbenchService();
        var artificer = new Artificer(new StagingService([staging]), new Team<IWorkbenchWorker>(Array.Empty<IWorkbenchWorker>()));

        var services = new ServiceCollection();
        services.AddSingleton<IStagingProvider>(staging);
        services.AddSingleton<IArtificer>(artificer);
        services.AddSingleton<IWorkbenchService>(workbenchService);
        var provider = services.BuildServiceProvider();

        var workbenchTemplate = InstitutionTemplateBuilder.Create()
            .WithDescriptor("Workbench", new Version(1, 0, 0), "test workbench")
            .Build();
        var workbench = ActivatorUtilities.CreateInstance<Workbench>(provider, new WorkbenchContext(workbenchTemplate, provider));

        var postProvider = new InMemoryPostProvider("adr-campus-maintenance");
        var postExchange = new PostExchange(workbench);
        var postService = new PostService([postProvider], postExchange, workbenchService);
        var postmaster = new Postmaster(new Team<IPostClerk>(Array.Empty<IPostClerk>()), postService);

        var postOfficeTemplate = InstitutionTemplateBuilder.Create()
            .WithDescriptor("PostOffice", new Version(1, 0, 0), "test post office")
            .Build();
        return new AethericForge.Runtime.Institutions.PostOffice.PostOffice(new PostOfficeContext(postOfficeTemplate, provider), postmaster);
    }
}
