using AdrCampus.Providers.Archive;
using AdrCampus.Providers.Library;
using AdrCampus.Providers.PostOffice;
using AethericForge.Runtime.Abstractions.Interfaces.Archive.Primitives;
using AethericForge.Runtime.Abstractions.Interfaces.Archive.Providers;
using AethericForge.Runtime.Abstractions.Interfaces.Archive.Serialization;
using AethericForge.Runtime.Abstractions.Interfaces.Archive.Services;
using AethericForge.Runtime.Abstractions.Interfaces.Authorities;
using AethericForge.Runtime.Abstractions.Interfaces.Identity.Lifecycle;
using AethericForge.Runtime.Abstractions.Interfaces.Identity.Provisioning;
using AethericForge.Runtime.Abstractions.Interfaces.Identity.Services;
using AethericForge.Runtime.Abstractions.Interfaces.Knowledge.Providers;
using AethericForge.Runtime.Abstractions.Interfaces.Knowledge.Services;
using AethericForge.Runtime.Abstractions.Interfaces.Library.Services;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Primitives;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Providers;
using AethericForge.Runtime.Abstractions.Interfaces.Post.Services;
using AethericForge.Runtime.Abstractions.Interfaces.Staging.Providers;
using AethericForge.Runtime.Abstractions.Interfaces.Staging.Services;
using AethericForge.Runtime.Abstractions.Interfaces.Workbench.Services;
using AethericForge.Runtime.Institutions.Abstractions.Builders;
using AethericForge.Runtime.Institutions.Abstractions.Composition;
using AethericForge.Runtime.Institutions.Abstractions.Models;
using AethericForge.Runtime.Institutions.Abstractions.Primitives;
using AethericForge.Runtime.Institutions.Archive;
using AethericForge.Runtime.Institutions.Campus;
using AethericForge.Runtime.Institutions.Library;
using AethericForge.Runtime.Institutions.PostOffice;
using AethericForge.Runtime.Institutions.Registry;
using AethericForge.Runtime.Institutions.Workbench;
using AethericForge.Runtime.Models.Archive.Serialization;
using AethericForge.Runtime.Models.Authorities;
using AethericForge.Runtime.Models.Post;
using AethericForge.Runtime.Providers.Archive.MongoDb;
using AethericForge.Runtime.Providers.Identity.Keycloak;
using AethericForge.Runtime.Providers.Knowledge.MongoDb;
using AethericForge.Runtime.Providers.Post.InMemory;
using AethericForge.Runtime.Providers.Staging.InMemory;
using AethericForge.Runtime.Providers.Staging.Redis;
using AethericForge.Runtime.Services.Archive;
using AethericForge.Runtime.Services.Identity;
using AethericForge.Runtime.Services.Identity.Lifecycle;
using AethericForge.Runtime.Services.Knowledge;
using AethericForge.Runtime.Services.Library;
using AethericForge.Runtime.Services.Post;
using AethericForge.Runtime.Services.Registry;
using AethericForge.Runtime.Services.Staging;
using AethericForge.Runtime.Services.Workbench;
using MongoDB.Driver;
using StackExchange.Redis;

namespace AdrCampus.Web.Hosting;

/// <summary>
/// Composes the ADR Campus onto a real <see cref="ICampus"/> made up of the Registry, Library, Workbench,
/// Archive, and Post Office institutions, mirroring the pattern used by the ParallelYou host
/// (ParallelYou.Web/Hosting/ForgeCampusExtensions.cs). ADR Campus does not need ParallelYou's "Person"
/// concept. Archive and Library are MongoDB-backed, like aetheric-web/parallel-you; Post Office
/// (maintenance command custody) stays in-memory since it models point-to-point envelope dispatch, not
/// durable knowledge - a message broker is a bigger and separate infra decision from this one.
/// </summary>
public static class ForgeCampusExtensions
{
    private const string WorkbenchStage = "adr-campus-workbench";
    private const string ArchiveStore = "MongoDb";
    private const string ArchiveCollection = "archive";
    private const string KnowledgeScheme = "adr-campus";
    private const string KnowledgeCollection = "knowledge";
    private const string MaintenanceDomain = "adr-campus-maintenance";

    private static string BuildMongoUri(IConfiguration configuration)
    {
        var host = GetRequiredSetting(configuration, "MongoDb:Host");
        var username = GetRequiredSetting(configuration, "MongoDb:Username");
        var password = GetRequiredSetting(configuration, "MongoDb:Password");
        var databaseName = GetRequiredSetting(configuration, "MongoDb:DatabaseName");
        var authenticationDatabase = GetRequiredSetting(configuration, "MongoDb:AuthenticationDatabase");
        var port = configuration.GetValue<int?>("MongoDb:Port")
                   ?? throw new InvalidOperationException("MongoDb:Port is required.");

        var builder = new MongoUrlBuilder
        {
            Server = new MongoServerAddress(host, port),
            Username = username,
            Password = password,
            DatabaseName = databaseName,
            AuthenticationSource = authenticationDatabase,
            DirectConnection = configuration.GetValue("MongoDb:DirectConnection", true)
        };

        return builder.ToMongoUrl().ToString();
    }

    private static string GetRequiredSetting(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"{key} is required.");
    }

    public static IServiceCollection AddForgeCampus(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddInstitutionTemplate(builder =>
        {
            builder.WithDescriptor(
                    "AdrCampus",
                    new Version(1, 0, 0),
                    "The ADR Campus decision-record collaboration campus.")
                // Identity / Registry
                .With<IIdentityLifecycleService, IdentityLifecycleService>()
                .With<IIdentityService, IdentityService>()
                .With<HttpClient, HttpClient>()
                .With<KeycloakOptions>(sp =>
                {
                    var keycloak = sp.GetRequiredService<IConfiguration>().GetSection("Keycloak");
                    return new KeycloakOptions
                    {
                        Authority = keycloak["Authority"] ?? throw new InvalidOperationException("Keycloak:Authority is required"),
                        ClientId = keycloak["ClientId"] ?? throw new InvalidOperationException("Keycloak:ClientId is required"),
                        ClientSecret = keycloak["ClientSecret"] ?? throw new InvalidOperationException("Keycloak:ClientSecret is required"),
                        Realm = keycloak["Realm"] ?? throw new InvalidOperationException("Keycloak:Realm is required"),
                    };
                })
                .With<IIdentityProvider, KeycloakIdentityProvider>()
                .With<IRegistryService, RegistryService>()
                .With<ITeam<IRegistryClerk>>(_ => new Team<IRegistryClerk>(Array.Empty<IRegistryClerk>()))
                .With<IRegistrar, Registrar>()
                // Archive
                .With<IMongoClient>(sp => new MongoClient(BuildMongoUri(sp.GetRequiredService<IConfiguration>())))
                .With<IMongoDatabase>(sp => sp
                    .GetRequiredService<IMongoClient>()
                    .GetDatabase(GetRequiredSetting(sp.GetRequiredService<IConfiguration>(), "MongoDb:DatabaseName")))
                .With<IArchiveProvider>(sp => new MongoDbArchiveProvider(
                    sp.GetRequiredService<IMongoDatabase>(),
                    ArchiveStore,
                    ArchiveCollection))
                .With<IArchiveVault, ArchiveVault>()
                .With<IArchiveService, ArchiveService>()
                .With<IArchiveSerializer, JsonArchiveSerializer>()
                .With<ITeam<IArchiveClerk>>(_ => new Team<IArchiveClerk>(Array.Empty<IArchiveClerk>()))
                .With<IArchivist, Archivist>()
                // Knowledge / Library
                .With<IKnowledgeProvider>(sp => new MongoDbKnowledgeProvider(
                    sp.GetRequiredService<IMongoDatabase>(),
                    KnowledgeScheme,
                    KnowledgeCollection))
                .With<IKnowledgeService, KnowledgeService>()
                .With<ITeam<ICuratorClerk>>(_ => new Team<ICuratorClerk>(Array.Empty<ICuratorClerk>()))
                .With<ICurator, Curator>()
                .With<ILibraryService, LibraryService>()
                .With<ITeam<ILibraryClerk>>(_ => new Team<ILibraryClerk>(Array.Empty<ILibraryClerk>()))
                .With<ILibrarian, Librarian>()
                // Staging / Workbench
                .With<IStagingProvider>(sp =>
                {
                    var redisConnection = sp.GetRequiredService<IConfiguration>().GetConnectionString("Redis");
                    if (string.IsNullOrWhiteSpace(redisConnection))
                    {
                        return new InMemoryStagingProvider(WorkbenchStage);
                    }
                    var multiplexer = sp.GetRequiredService<IConnectionMultiplexer>();
                    return new RedisStagingProvider(multiplexer, WorkbenchStage);
                })
                .With<IStagingService, StagingService>()
                .With<IWorkbenchService, WorkbenchService>()
                .With<ITeam<IWorkbenchWorker>>(_ => new Team<IWorkbenchWorker>(Array.Empty<IWorkbenchWorker>()))
                .With<IArtificer, Artificer>()
                // Post Office
                .With<IPostProvider>(_ => new InMemoryPostProvider(MaintenanceDomain))
                .With<ITeam<IPostClerk>>(_ => new Team<IPostClerk>(Array.Empty<IPostClerk>()));
        });

        var redisConnection = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnection));
        }

        services.AddSingleton<ICampus>(serviceProvider =>
        {
            var campusTemplate = (InstitutionTemplate)serviceProvider.GetRequiredService<IInstitutionTemplate>();
            var campusContext = new CampusContext(campusTemplate, serviceProvider);
            var campus = new Campus(campusContext);

            var registryTemplate = campusTemplate with { Descriptor = new InstitutionDescriptor("Registry", campusTemplate.Descriptor.Version, "Registry institution") };
            campus.Register<IRegistry>(ActivatorUtilities.CreateInstance<Registry>(serviceProvider, new RegistryContext(registryTemplate, serviceProvider, campus)));

            var libraryTemplate = campusTemplate with { Descriptor = new InstitutionDescriptor("Library", campusTemplate.Descriptor.Version, "Library institution") };
            campus.Register<ILibrary>(ActivatorUtilities.CreateInstance<Library>(serviceProvider, new LibraryContext(libraryTemplate, serviceProvider, campus)));

            var archiveTemplate = campusTemplate with { Descriptor = new InstitutionDescriptor("Archive", campusTemplate.Descriptor.Version, "Archive institution") };
            campus.Register<IArchive>(ActivatorUtilities.CreateInstance<AethericForge.Runtime.Institutions.Archive.Archive>(serviceProvider, new ArchiveContext(archiveTemplate, serviceProvider, campus)));

            var workbenchTemplate = campusTemplate with { Descriptor = new InstitutionDescriptor("Workbench", campusTemplate.Descriptor.Version, "Workbench institution") };
            var workbench = ActivatorUtilities.CreateInstance<AethericForge.Runtime.Institutions.Workbench.Workbench>(serviceProvider, new WorkbenchContext(workbenchTemplate, serviceProvider, campus));
            campus.Register<IWorkbench>(workbench);

            // The Post Office's exchange is backed by the Workbench institution instance itself (see
            // AethericForge.Runtime.Services.Post.PostExchange), which is not resolvable purely from the
            // DI container since Workbench is built and registered by hand above. Postmaster/PostService/
            // PostExchange are therefore composed here rather than via `.With<>()`.
            var postOfficeTemplate = campusTemplate with { Descriptor = new InstitutionDescriptor("PostOffice", campusTemplate.Descriptor.Version, "Post Office institution") };
            var postExchange = new PostExchange(workbench);
            var postService = new PostService(
                serviceProvider.GetServices<IPostProvider>(),
                postExchange,
                serviceProvider.GetRequiredService<IWorkbenchService>());
            var postmaster = new Postmaster(new Team<IPostClerk>(Array.Empty<IPostClerk>()), postService);
            campus.Register<IPostOffice>(ActivatorUtilities.CreateInstance<AethericForge.Runtime.Institutions.PostOffice.PostOffice>(
                serviceProvider,
                new PostOfficeContext(postOfficeTemplate, serviceProvider, campus),
                postmaster));

            return campus;
        });

        services.AddSingleton<ForgeCampusHost>();
        services.AddHostedService<ForgeCampusHost>(serviceProvider => serviceProvider.GetRequiredService<ForgeCampusHost>());

        services.AddSingleton<AdrCampus.Core.Drafts.IDraftRepository, AdrCampus.Providers.Drafts.Workbench.WorkbenchDraftRepository>();
        services.AddSingleton<AdrCampus.Core.Drafts.IDraftRecoveryRepository>(sp => (AdrCampus.Providers.Drafts.Workbench.WorkbenchDraftRepository)sp.GetRequiredService<AdrCampus.Core.Drafts.IDraftRepository>());
        services.AddSingleton<AdrCampus.Core.Drafts.IExpiredDraftPurgeRepository>(sp => (AdrCampus.Providers.Drafts.Workbench.WorkbenchDraftRepository)sp.GetRequiredService<AdrCampus.Core.Drafts.IDraftRepository>());
        services.AddSingleton<LibraryProposalRepository>(sp => new LibraryProposalRepository(
            sp.GetRequiredService<ICampus>().Library,
            sp.GetRequiredService<AdrCampus.Core.Drafts.IDraftRepository>()));
        services.AddSingleton<AdrCampus.Core.Proposals.IProposalRepository>(sp => sp.GetRequiredService<LibraryProposalRepository>());
        services.AddSingleton<AdrCampus.Core.Discovery.ISharedRecordRepository>(sp => sp.GetRequiredService<LibraryProposalRepository>());
        services.AddSingleton<AdrCampus.Core.Administration.IOrganizationAdministrationRepository, ArchiveOrganizationAdministrationRepository>();
        services.AddSingleton<AdrCampus.Core.Membership.IMembershipRepository, ArchiveMembershipRepository>();
        services.AddSingleton<AdrCampus.Core.Maintenance.IMaintenancePostOffice>(sp => new PostOfficeMaintenanceDispatcher(sp.GetRequiredService<ICampus>().PostOffice));

        return services;
    }

    public static IEndpointRouteBuilder MapForgeCampusDiagnostics(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/status", (ICampus campus, ForgeCampusHost host) =>
            Results.Ok(new
            {
                institution = campus.Context.Template.Descriptor.Name,
                version = campus.Context.Template.Descriptor.Version.ToString(),
                isRoot = campus.Context.Parent is null,
                host.IsRunning,
                registrar = new
                {
                    name = campus.Registry.Context.Template.Descriptor.Name,
                    version = campus.Registry.Context.Template.Descriptor.Version.ToString()
                },
                postOffice = new
                {
                    name = campus.PostOffice.Context.Template.Descriptor.Name,
                    version = campus.PostOffice.Context.Template.Descriptor.Version.ToString()
                },
                archive = new
                {
                    name = campus.Archive.Context.Template.Descriptor.Name,
                    version = campus.Archive.Context.Template.Descriptor.Version.ToString()
                },
                library = new
                {
                    name = campus.Library.Context.Template.Descriptor.Name,
                    version = campus.Library.Context.Template.Descriptor.Version.ToString()
                }
            }));

        return endpoints;
    }
}
