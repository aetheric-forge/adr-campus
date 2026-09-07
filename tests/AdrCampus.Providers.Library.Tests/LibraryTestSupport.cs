using AethericForge.Runtime.Abstractions.Interfaces.Authorities;
using AethericForge.Runtime.Abstractions.Interfaces.Knowledge.Providers;
using AethericForge.Runtime.Abstractions.Interfaces.Knowledge.Services;
using AethericForge.Runtime.Abstractions.Interfaces.Library.Services;
using AethericForge.Runtime.Institutions.Abstractions.Builders;
using AethericForge.Runtime.Institutions.Library;
using AethericForge.Runtime.Models.Authorities;
using AethericForge.Runtime.Providers.Knowledge.InMemory;
using AethericForge.Runtime.Services.Knowledge;
using AethericForge.Runtime.Services.Library;
using Microsoft.Extensions.DependencyInjection;

namespace AdrCampus.Providers.Library.Tests;

internal static class LibraryTestSupport
{
    /// <summary>
    /// Builds a real Library institution backed by a fresh in-memory Knowledge provider, matching the
    /// composition ForgeCampusExtensions performs in the host, so LibraryProposalRepository sees the same
    /// ILibraryContext.Knowledge surface it uses in production.
    /// </summary>
    public static ILibrary NewLibrary()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IKnowledgeProvider>(new InMemoryKnowledgeProvider("adr-campus"));
        services.AddSingleton<ITeam<ICuratorClerk>>(new Team<ICuratorClerk>(Array.Empty<ICuratorClerk>()));
        services.AddSingleton<IKnowledgeService, KnowledgeService>();
        services.AddSingleton<ITeam<ILibraryClerk>>(new Team<ILibraryClerk>(Array.Empty<ILibraryClerk>()));
        services.AddSingleton<ILibrarian, Librarian>();
        services.AddSingleton<ICurator, Curator>();
        var provider = services.BuildServiceProvider();

        var template = InstitutionTemplateBuilder.Create()
            .WithDescriptor("Library", new Version(1, 0, 0), "test library")
            .Build();
        var context = new LibraryContext(template, provider);
        return ActivatorUtilities.CreateInstance<AethericForge.Runtime.Institutions.Library.Library>(provider, context);
    }
}
