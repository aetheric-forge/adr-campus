using AethericForge.Runtime.Abstractions.Interfaces.Identity.Authentication;
using AethericForge.Runtime.Institutions.Campus;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace AdrCampus.Web.Hosting;

/// <summary>
/// Replaces the standalone AddAuthentication/AddCookie/AddOpenIdConnect block that used to live directly in
/// Program.cs. The cookie/OIDC settings (cookie name, paths, PKCE, claim mapping) are preserved exactly as
/// they were; the only behavioral addition is registering the authenticated principal with the Registry
/// institution via <see cref="ICampus.Registry"/>'s Registrar, following the ParallelYou host pattern
/// (ParallelYou.Web/Hosting/ForgeAuthenticationExtensions.cs). ADR Campus's own notion of "active member"
/// (<see cref="AdrCampus.Application.Identity.IMemberAuthority"/>, backed by Keycloak group membership) is
/// unrelated to this and is untouched.
/// </summary>
public static class ForgeAuthenticationExtensions
{
    public static IServiceCollection AddForgeCampusAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services
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
                var keycloak = configuration.GetSection("Keycloak");
                options.Authority = $"{keycloak["Authority"]}/realms/{keycloak["Realm"]}";
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
                options.Events.OnTokenValidated = RegisterPrincipalAsync;
            });

        // Authorization policies (ActiveMember/ActiveMaintainer) are registered by the caller in
        // Program.cs, since they gate on ADR Campus's own IMemberAuthority rather than anything the
        // Registry institution knows about.

        return services;
    }

    public static IEndpointRouteBuilder MapForgeCampusAuthentication(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/account/login", (string? returnUrl) =>
            Results.Challenge(
                new AuthenticationProperties { RedirectUri = LocalReturnUrl(returnUrl) },
                [OpenIdConnectDefaults.AuthenticationScheme]))
            .AllowAnonymous();

        endpoints.MapPost("/account/logout", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            return Results.SignOut(
                new AuthenticationProperties { RedirectUri = "/" },
                [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
        })
            .RequireAuthorization();

        endpoints.MapGet("/account/access-denied", () => Results.Content(
            "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Access denied</title></head>" +
            "<body><main><h1>Access denied</h1><p>Your Keycloak identity is not an active ADR Campus member.</p>" +
            "<a href=\"/account/login\">Sign in with another account</a></main></body></html>",
            "text/html"))
            .AllowAnonymous();

        return endpoints;
    }

    private static async Task RegisterPrincipalAsync(TokenValidatedContext context)
    {
        var accessToken = context.TokenEndpointResponse?.AccessToken;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            context.Fail("Keycloak did not return an access token.");
            return;
        }

        var campus = context.HttpContext.RequestServices.GetRequiredService<ICampus>();
        var principalIdentity = await campus.Registry.Registrar.AuthenticateAsync(
            IdentityScheme.OpenIdConnect,
            new Dictionary<string, string> { ["token"] = accessToken },
            context.HttpContext.RequestAborted);

        if (principalIdentity is null || !principalIdentity.IsAuthenticated)
        {
            context.Fail("ADR Campus Registry rejected the Keycloak principal.");
        }
    }

    private static string LocalReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) &&
        returnUrl.StartsWith("/", StringComparison.Ordinal) &&
        !returnUrl.StartsWith("//", StringComparison.Ordinal)
            ? returnUrl
            : "/";
}
