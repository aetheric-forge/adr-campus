# Delivery Roadmap

ADR Campus is being delivered in small, usable releases on the way to the first complete decision workflow described by the user stories. Version numbers describe implementation milestones; they do not replace or redefine the product stories.

## Completed Releases

### v0.2 — Identity and Application Security

Version 0.2 establishes the trusted organizational boundary required by every later ADR operation:

- Keycloak OpenID Connect sign-in and sign-out;
- Runtime-backed external identity and group-directory integration;
- configured member and maintainer groups;
- an SSO-derived active-member roster;
- fail-closed member and maintainer authorization;
- maintainer-only route protection;
- role-aware account and navigation presentation; and
- a presentation-only **View as member** mode for maintainers.

The detailed release record is in [releases/v0.2.md](releases/v0.2.md).

### v0.3 — Draft a Decision

Version 0.3 implements the private drafting journey: creation, revision, author-private listing and viewing, optimistic concurrency, preview, and Redis-backed Workbench persistence.

### v0.4 — Propose, Review, and Decide

Version 0.4 implements proposal and maintainer decision transitions, including validation, immutable proposed content, idempotent operations, acceptance, rejection, and durable shared records.

The detailed release record is in [releases/v0.4.md](releases/v0.4.md).

### v0.5 — Discover and Understand Decisions

Version 0.5 implements the authorization-aware Current, Proposed, Historical, and All shared-record views, including search, filtering, sorting, pagination, lifecycle history, and result-context preservation.

The detailed release record is in [releases/v0.5.md](releases/v0.5.md).

### v0.6 — Supersede a Decision

Version 0.6 implements replacement drafting, immutable intended-target metadata, atomic and idempotent supersession, deterministic handling of competing replacements, and navigable lifecycle history across Current and Historical discovery.

The detailed release record is in [releases/v0.6.md](releases/v0.6.md), with implementation sequencing and QA coverage in [backlog/v0.6.md](backlog/v0.6.md).

## Active Release

### v0.7 — Complete Organization Administration

Complete the remaining application-owned administration behavior from [06-administer-the-organization.md](stories/06-administer-the-organization.md), including membership projections, draft recovery, organization settings, administration history, expiry, and maintenance processing.

## First Complete Release

The first complete release is reached when the six documented journeys work together end to end and satisfy the outcome in [stories/overview.md](stories/overview.md). Planned version boundaries may be adjusted when implementation reveals a safer or more coherent slice, but story acceptance criteria remain authoritative.
