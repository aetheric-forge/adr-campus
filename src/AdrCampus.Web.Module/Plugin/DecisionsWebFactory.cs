using System.Runtime.Loader;
using AdrCampus.Application.Administration;
using AdrCampus.Application.Discovery;
using AdrCampus.Application.Drafts;
using AdrCampus.Application.Identity;
using AdrCampus.Application.Maintenance;
using AdrCampus.Application.Membership;
using AdrCampus.Core.Administration;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Maintenance;
using AdrCampus.Core.Membership;
using AdrCampus.Plugin;
using AdrCampus.Providers.Archive;
using AdrCampus.Providers.PostOffice;
using AdrCampus.Shell.Drafts;
using AdrCampus.Shell.Identity;
using AdrCampus.Shell.Maintenance;
using AdrCampus.Shell.Members;
using AdrCampus.Shell.Module.Hosting;
using AethericForge.Runtime.Abstractions.Interfaces.Archive.Providers;
using AethericForge.Runtime.Abstractions.Interfaces.Archive.Services;
using AethericForge.Runtime.Abstractions.Interfaces.Institutions;
using AethericForge.Runtime.Abstractions.Interfaces.Institutions.Plugins;
using AethericForge.Runtime.Abstractions.Interfaces.Knowledge.Providers;
using AethericForge.Runtime.Institutions.Abstractions.Primitives;
using AethericForge.Runtime.Institutions.PostOffice;
using AethericForge.Runtime.Providers.Archive.S3;
using AethericForge.Runtime.Providers.Knowledge.MongoDb;
using Amazon.Runtime;
using Amazon.S3;
using Forge.Primitives.MongoDb;
using Microsoft.AspNetCore.Authorization;
using MongoDB.Driver;

[assembly: InstitutionPluginPackage(typeof(AdrCampus.Shell.Module.Plugin.DecisionsWebPackage))]
namespace AdrCampus.Shell.Module.Plugin;

public sealed class DecisionsWebPackage : IInstitutionPluginPackage
{
    public IReadOnlyCollection<IInstitutionFactory> GetFactories() => [];
    public IReadOnlyCollection<IOrganizationFactory> GetOrganizationFactories() => [new DecisionsWebFactory()];
}

/// <summary>Hosts the existing office under its owning institution; never creates a sovereign Decisions campus.</summary>
public sealed class DecisionsWebFactory : IOrganizationFactory
{
    private readonly AssemblyLoadContext _context = AssemblyLoadContext.GetLoadContext(typeof(DecisionsWebFactory).Assembly)!;
    private readonly DecisionsOrganizationFactory _factory = new();
    public string OrganizationId => _factory.OrganizationId;
    public IInstitutionTemplate Template => _factory.Template;
    public IInstitutionManifest Manifest => _factory.Manifest;
    public IOrganization Create(IInstitution owner, IServiceProvider services) => _factory.Create(owner, services);

    public void ConfigureWebServices(IServiceCollection services, IConfiguration configuration,
        Func<IServiceProvider, IInstitution?> parent)
    {
        IInstitution Owner(IServiceProvider sp) => parent(sp)
            ?? throw new InvalidOperationException("Decisions requires its existing owning institution with Library and Workbench capabilities.");
        var organizationId = new OrganizationId(Required(configuration, "Organization:Id"));
        MongoBsonSetup.EnsureGuidRepresentationRegistered();
        var mongo = InstitutionServiceConfiguration.Resolve(configuration, "Decisions", "MongoDb").GetSection("MongoDb").Get<MongoOptions>()!;
        if (string.IsNullOrWhiteSpace(mongo.Host) || string.IsNullOrWhiteSpace(mongo.DatabaseName))
            throw new InvalidOperationException("Decisions MongoDb Host and DatabaseName are required.");
        services.AddKeyedSingleton<IMongoClient>("Decisions", (_, _) => new MongoClient(mongo.ToConnectionString()));
        services.AddKeyedSingleton<IMongoDatabase>("Decisions", (sp, _) => sp.GetRequiredKeyedService<IMongoClient>("Decisions").GetDatabase(mongo.DatabaseName));
        // Contribute the existing Decisions schemes to its owner's Library and Archive.
        services.AddKeyedSingleton<IKnowledgeProvider>("Library", (sp, _) => new MongoDbKnowledgeProvider(sp.GetRequiredKeyedService<IMongoDatabase>("Decisions"), "adr-campus", "knowledge"));
        var s3 = InstitutionServiceConfiguration.Resolve(configuration, "Decisions", "S3");
        _ = Required(s3, "S3:ServiceUrl");
        services.AddKeyedSingleton<IAmazonS3>("Decisions", (_, _) => new AmazonS3Client(
            new BasicAWSCredentials(s3["S3:AccessKey"], s3["S3:SecretKey"]), new AmazonS3Config
            {
                ServiceURL = Required(s3, "S3:ServiceUrl"), ForcePathStyle = s3.GetValue("S3:ForcePathStyle", true),
                AuthenticationRegion = s3.GetValue("S3:AuthenticationRegion", "us-east-1")
            }));
        services.AddKeyedSingleton<IArchiveProvider>("Archive", (sp, _) => new S3ArchiveProvider(sp.GetRequiredKeyedService<IAmazonS3>("Decisions"),
            "adr-campus", s3.GetValue("S3:BucketName", "forge-campus-archive")!, "adr-campus"));
        services.AddDecisionsOffice(organizationId, Owner);
        services.AddSingleton<IOrganizationAdministrationRepository>(sp => new ArchiveOrganizationAdministrationRepository(sp.GetRequiredKeyedService<IArchivist>("Archive")));
        services.AddSingleton<IMembershipRepository>(sp => new ArchiveMembershipRepository(sp.GetRequiredKeyedService<IArchivist>("Archive")));
        services.AddSingleton<IMaintenancePostOffice>(sp => new PostOfficeMaintenanceDispatcher(Owner(sp).Resolve<IPostOffice>()));
        services.AddScoped<DraftApplicationService>();
        services.AddScoped<DiscoveryApplicationService>();
        services.AddScoped<OrganizationAdministrationService>();
        services.AddScoped<DraftRecoveryApplicationService>();
        services.AddScoped<IDraftRecoveryCoordinator>(sp => sp.GetRequiredService<DraftRecoveryApplicationService>());
        services.AddScoped<MaintenanceApplicationService>();
        services.AddScoped<AdministrationHistoryService>();
        services.AddScoped<MembershipObservationService>();
        services.AddScoped<IProposalReviewCaller, AuthenticatedProposalReviewCaller>();
        services.AddScoped<IMemberAuthority, KeycloakMemberAuthority>();
        services.AddScoped<IMemberDisplayNameDirectory, KeycloakMemberDisplayNameDirectory>();
        services.AddScoped<IDirectoryRosterSource, KeycloakDirectoryRosterSource>();
        services.AddScoped<IOrganizationBootstrapVerifier, KeycloakOrganizationBootstrapVerifier>();
        services.AddSingleton(new CurrentOrganization(organizationId));
        var keycloak = configuration.GetSection("Decisions:Directory:Keycloak").Exists()
            ? new ConfigurationBuilder().AddInMemoryCollection(configuration.GetSection("Decisions:Directory:Keycloak")
                .AsEnumerable(true).Where(pair => pair.Value is not null).Select(pair => new KeyValuePair<string, string?>("Keycloak:" + pair.Key, pair.Value))).Build()
            : InstitutionServiceConfiguration.Resolve(configuration, "Registry", "Keycloak");
        var normalized = new ConfigurationBuilder().AddConfiguration(configuration).AddConfiguration(keycloak).Build();
        services.AddHttpClient(MemberRosterService.HttpClientName);
        services.AddSingleton(sp => new MemberRosterService(sp.GetRequiredService<IHttpClientFactory>(), normalized,
            sp.GetRequiredService<ILogger<MemberRosterService>>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(new OrganizationBootstrapConfiguration(organizationId,
            Required(configuration, "Organization:DisplayName"), Required(keycloak, "Keycloak:Authority"),
            Required(configuration, "Organization:MemberGroupId"), Required(configuration, "Organization:MaintainerGroupId")));
        services.AddSingleton<OrganizationBootstrapHealth>();
        services.AddScoped<OrganizationDisplayState>();
        services.AddAuthorizationBuilder()
            .AddPolicy(IdentityPolicies.ActiveMember, policy => { policy.RequireAuthenticatedUser(); policy.AddRequirements(new ActiveMemberRequirement()); })
            .AddPolicy(IdentityPolicies.ActiveMaintainer, policy => { policy.RequireAuthenticatedUser(); policy.AddRequirements(new ActiveMaintainerRequirement()); });
        services.AddScoped<IAuthorizationHandler, ActiveMemberAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, ActiveMaintainerAuthorizationHandler>();
        services.AddSingleton<IMaintenanceWorker, ExpiredDraftPurgeWorker>();
        if (configuration.GetValue("Decisions:BackgroundServicesEnabled", true))
        {
            services.AddHostedService<OrganizationBootstrapHostedService>();
            services.AddHostedService<MembershipSyncBackgroundService>();
            services.AddHostedService<MaintenanceDispatchService>();
        }
    }

    private static string Required(IConfiguration configuration, string key) =>
        !string.IsNullOrWhiteSpace(configuration[key]) ? configuration[key]! : throw new InvalidOperationException(key + " is required.");
}
