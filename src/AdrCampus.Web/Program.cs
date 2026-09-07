using AdrCampus.Application.Administration;
using AdrCampus.Application.Drafts;
using AdrCampus.Application.Identity;
using AdrCampus.Application.Maintenance;
using AdrCampus.Application.Membership;
using AdrCampus.Application.Proposals;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Maintenance;
using AdrCampus.Providers.Drafts.InMemory;
using AdrCampus.Web.Components;
using AdrCampus.Web.Drafts;
using AdrCampus.Web.Hosting;
using AdrCampus.Web.Identity;
using AdrCampus.Web.Maintenance;
using AdrCampus.Web.Members;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);
ValidateOrganizationDirectoryConfiguration(builder.Configuration);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddForgeCampusAuthentication(builder.Configuration);
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(IdentityPolicies.ActiveMember, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new ActiveMemberRequirement());
    });
    options.AddPolicy(IdentityPolicies.ActiveMaintainer, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new ActiveMaintainerRequirement());
    });
});
builder.Services.AddScoped<IAuthorizationHandler, ActiveMemberAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, ActiveMaintainerAuthorizationHandler>();

builder.Services.AddForgeCampus(builder.Configuration);

builder.Services.AddHttpClient(MemberRosterService.HttpClientName);
builder.Services.AddScoped<MemberRosterService>();
builder.Services.AddScoped<IOrganizationBootstrapVerifier, KeycloakOrganizationBootstrapVerifier>();
builder.Services.AddSingleton<OrganizationBootstrapHealth>();
builder.Services.AddScoped<OrganizationDisplayState>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddSingleton<IMaintenanceWorker, ExpiredDraftPurgeWorker>();
builder.Services.AddHostedService<MaintenanceDispatchService>();
builder.Services.AddScoped<IMemberAuthority, KeycloakMemberAuthority>();
builder.Services.AddScoped<AdrCampus.Application.Identity.IMemberDisplayNameDirectory, KeycloakMemberDisplayNameDirectory>();
builder.Services.AddScoped<IDirectoryRosterSource, KeycloakDirectoryRosterSource>();
builder.Services.AddScoped<DraftApplicationService>();
builder.Services.AddScoped<ProposalApplicationService>();
builder.Services.AddScoped<AdrCampus.Application.Discovery.DiscoveryApplicationService>();
builder.Services.AddScoped<OrganizationAdministrationService>();
builder.Services.AddScoped<DraftRecoveryApplicationService>();
builder.Services.AddScoped<IDraftRecoveryCoordinator>(services => services.GetRequiredService<DraftRecoveryApplicationService>());
builder.Services.AddScoped<MaintenanceApplicationService>();
builder.Services.AddScoped<AdministrationHistoryService>();
builder.Services.AddScoped<MembershipObservationService>();
builder.Services.AddSingleton(new CurrentOrganization(
    new OrganizationId(builder.Configuration["Organization:Id"]!)));
builder.Services.AddHostedService<MembershipSyncBackgroundService>();
builder.Services.AddSingleton(new OrganizationBootstrapConfiguration(
    new OrganizationId(builder.Configuration["Organization:Id"]!),
    builder.Configuration["Organization:DisplayName"]!,
    builder.Configuration["Keycloak:Authority"]!,
    builder.Configuration["Organization:MemberGroupId"]!,
    builder.Configuration["Organization:MaintainerGroupId"]!));

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var configuration = scope.ServiceProvider.GetRequiredService<OrganizationBootstrapConfiguration>();
    var administration = scope.ServiceProvider.GetRequiredService<OrganizationAdministrationService>();
    var bootstrap = await administration.BootstrapAsync(configuration, OperationId.New());
    scope.ServiceProvider.GetRequiredService<OrganizationBootstrapHealth>().Record(bootstrap);
    if (!bootstrap.IsSuccess)
        app.Logger.LogError("Organization bootstrap was not completed: {BootstrapStatus} {BootstrapError}", bootstrap.Status, bootstrap.ErrorMessage);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapForgeCampusAuthentication();
app.MapForgeCampusDiagnostics();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .RequireAuthorization(IdentityPolicies.ActiveMember)
    .AddInteractiveServerRenderMode();

app.Run();

static void ValidateOrganizationDirectoryConfiguration(IConfiguration configuration)
{
    var memberGroup = configuration["Organization:MemberGroupId"]?.Trim();
    var maintainerGroup = configuration["Organization:MaintainerGroupId"]?.Trim();
    var organizationId = configuration["Organization:Id"]?.Trim();
    var organizationName = configuration["Organization:DisplayName"]?.Trim();
    var authority = configuration["Keycloak:Authority"]?.Trim();
    if (string.IsNullOrWhiteSpace(organizationId))
    {
        throw new InvalidOperationException("Configuration value 'Organization:Id' is required.");
    }
    if (string.IsNullOrWhiteSpace(memberGroup))
    {
        throw new InvalidOperationException("Configuration value 'Organization:MemberGroupId' is required.");
    }
    if (string.IsNullOrWhiteSpace(organizationName))
    {
        throw new InvalidOperationException("Configuration value 'Organization:DisplayName' is required.");
    }
    if (string.IsNullOrWhiteSpace(authority))
    {
        throw new InvalidOperationException("Configuration value 'Keycloak:Authority' is required.");
    }
    if (string.IsNullOrWhiteSpace(maintainerGroup))
    {
        throw new InvalidOperationException("Configuration value 'Organization:MaintainerGroupId' is required.");
    }
    if (string.Equals(memberGroup, maintainerGroup, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "The member and maintainer groups must be configured as distinct Keycloak groups.");
    }
}
