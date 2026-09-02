using AdrCampus.Application.Administration;
using AdrCampus.Application.Drafts;
using AdrCampus.Application.Identity;
using AdrCampus.Application.Proposals;
using AdrCampus.Core.Administration;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Drafts;
using AdrCampus.Core.Discovery;
using AdrCampus.Core.Proposals;
using AdrCampus.Providers.Drafts.InMemory;
using AdrCampus.Providers.Drafts.Workbench;
using AethericForge.Runtime.Abstractions.Interfaces.Staging.Providers;
using AethericForge.Runtime.Providers.Staging.InMemory;
using AethericForge.Runtime.Providers.Staging.Redis;
using StackExchange.Redis;
using AdrCampus.Web.Components;
using AdrCampus.Web.Drafts;
using AdrCampus.Web.Identity;
using AdrCampus.Web.Members;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
ValidateOrganizationDirectoryConfiguration(builder.Configuration);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "__Host-AdrCampus";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.LoginPath = "/account/login";
        options.AccessDeniedPath = "/account/access-denied";
    })
    .AddOpenIdConnect(options =>
    {
        var keycloak = builder.Configuration.GetSection("Keycloak");
        options.Authority = keycloak["Authority"];
        options.ClientId = keycloak["ClientId"];
        options.ClientSecret = keycloak["ClientSecret"];
        options.ResponseType = "code";
        options.UsePkce = true;
        options.MapInboundClaims = false;
        options.GetClaimsFromUserInfoEndpoint = true;
        // Keycloak requires the original ID token as id_token_hint for RP-initiated logout.
        // The authentication ticket is protected and stored in the secure, HTTP-only cookie.
        options.SaveTokens = true;
        options.CallbackPath = "/signin-oidc";
        options.SignedOutCallbackPath = "/signout-callback-oidc";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "name",
            RoleClaimType = "role"
        };
    });
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
builder.Services.AddHttpClient(MemberRosterService.HttpClientName);
builder.Services.AddScoped<MemberRosterService>();
builder.Services.AddScoped<IOrganizationBootstrapVerifier, KeycloakOrganizationBootstrapVerifier>();
builder.Services.AddSingleton<OrganizationBootstrapHealth>();
builder.Services.AddScoped<OrganizationDisplayState>();
builder.Services.AddSingleton(TimeProvider.System);
var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddSingleton<IStagingProvider>(_ => new InMemoryStagingProvider("adr-campus-workbench"));
}
else
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnection));
    builder.Services.AddSingleton<IStagingProvider>(services => new RedisStagingProvider(services.GetRequiredService<IConnectionMultiplexer>(), "adr-campus-workbench"));
}
builder.Services.AddSingleton<IDraftRepository, WorkbenchDraftRepository>();
builder.Services.AddSingleton<IProposalRepository>(services => (WorkbenchDraftRepository)services.GetRequiredService<IDraftRepository>());
builder.Services.AddSingleton<ISharedRecordRepository>(services => (WorkbenchDraftRepository)services.GetRequiredService<IDraftRepository>());
builder.Services.AddSingleton<IOrganizationAdministrationRepository, WorkbenchOrganizationAdministrationRepository>();
builder.Services.AddScoped<IMemberAuthority, KeycloakMemberAuthority>();
builder.Services.AddScoped<AdrCampus.Application.Identity.IMemberDisplayNameDirectory, KeycloakMemberDisplayNameDirectory>();
builder.Services.AddScoped<DraftApplicationService>();
builder.Services.AddScoped<ProposalApplicationService>();
builder.Services.AddScoped<AdrCampus.Application.Discovery.DiscoveryApplicationService>();
builder.Services.AddScoped<OrganizationAdministrationService>();
builder.Services.AddSingleton(new CurrentOrganization(
    new OrganizationId(builder.Configuration["Organization:Id"]!)));
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

app.MapGet("/account/login", (string? returnUrl) =>
    Results.Challenge(
        new AuthenticationProperties { RedirectUri = LocalReturnUrl(returnUrl) },
        [OpenIdConnectDefaults.AuthenticationScheme]))
    .AllowAnonymous();
app.MapPost("/account/logout", async (HttpContext context, IAntiforgery antiforgery) =>
{
    await antiforgery.ValidateRequestAsync(context);
    return Results.SignOut(
        new AuthenticationProperties { RedirectUri = "/" },
        [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
})
    .RequireAuthorization();
app.MapGet("/account/access-denied", () => Results.Content(
    "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Access denied</title></head>" +
    "<body><main><h1>Access denied</h1><p>Your Keycloak identity is not an active ADR Campus member.</p>" +
    "<a href=\"/account/login\">Sign in with another account</a></main></body></html>",
    "text/html"))
    .AllowAnonymous();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .RequireAuthorization(IdentityPolicies.ActiveMember)
    .AddInteractiveServerRenderMode();

app.Run();

static string LocalReturnUrl(string? returnUrl) =>
    !string.IsNullOrWhiteSpace(returnUrl) &&
    returnUrl.StartsWith("/", StringComparison.Ordinal) &&
    !returnUrl.StartsWith("//", StringComparison.Ordinal)
        ? returnUrl
        : "/";

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
