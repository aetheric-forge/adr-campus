# ADR Campus

ADR Campus is a small application for maintaining one organization's architectural decision record. It gives architectural decisions a durable home and a small, understandable process through which they can be proposed, reviewed, accepted, and eventually superseded.

The [product goal, roles, and lifecycle](docs/stories/overview.md) are authoritative for what the application does. The [delivery roadmap](docs/roadmap.md) tracks how it was built, release by release. The [architecture overview](docs/architecture/overview.md) explains how the application is composed.

## Running it locally

ADR Campus requires:

- a Keycloak realm with a configured client, and member/maintainer groups; and
- a Redis instance for durable Workbench storage (optional — the app falls back to in-memory staging when `ConnectionStrings:Redis` is unset, which does not survive a restart).

Configure these with [.NET user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) against `src/AdrCampus.Web`:

```sh
cd src/AdrCampus.Web
dotnet user-secrets set "Keycloak:Authority" "https://<host>/realms/<realm>"
dotnet user-secrets set "Keycloak:Realm" "<realm>"
dotnet user-secrets set "Keycloak:ClientId" "<client-id>"
dotnet user-secrets set "Keycloak:ClientSecret" "<client-secret>"
dotnet user-secrets set "Organization:MemberGroupId" "<group-id-or-exact-name>"
dotnet user-secrets set "Organization:MaintainerGroupId" "<group-id-or-exact-name>"
dotnet user-secrets set "ConnectionStrings:Redis" "<host>:<port>[,password=...]"
```

`Organization:MemberGroupId` and `Organization:MaintainerGroupId` accept either a Keycloak group ID or an exact, unique group display name.

Then, from the repository root:

```sh
dotnet run --project src/AdrCampus.Web
```

The organization bootstraps automatically on startup from the configured groups. At least one enabled identity must belong to both the member and maintainer groups, or bootstrap fails and the application reports the condition rather than granting default authority.

## Building and testing

```sh
dotnet build
dotnet test
```

## Repository layout

- `src/AdrCampus.Core` — the ADR domain model and provider-facing contracts.
- `src/AdrCampus.Application` — application services: commands, queries, and authorization.
- `src/AdrCampus.Providers.*` — storage provider implementations (Workbench-backed and in-memory).
- `src/AdrCampus.Web` — the Blazor web host: composition root, authentication, and UI.
- `runtime/` — the Aetheric Forge Runtime this application is built on.
- `docs/` — product stories, architecture, and the delivery roadmap.
- `tests/` — automated tests for the above, mirroring the `src/` layout.
