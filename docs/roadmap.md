# Delivery Roadmap

ADR Campus is being delivered in small, usable releases on the way to the first complete decision workflow described by the user stories. Version numbers describe implementation milestones; they do not replace or redefine the product stories.

## Completed Foundation

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

## Planned Workflow Releases

### v0.3 — Draft a Decision

Implement the private draft journey described by [01-draft-a-decision.md](stories/01-draft-a-decision.md): create, revise, list, and preview a draft while preserving its stable identity, authorship, and timestamps.

### v0.4 — Propose, Review, and Decide

Implement proposal and maintainer decision transitions from [02-propose-a-decision.md](stories/02-propose-a-decision.md) and [03-review-and-decide.md](stories/03-review-and-decide.md), including validation, immutable proposed content, idempotent operations, acceptance, and rejection.

### v0.5 — Discover and Understand Decisions

Implement the authorization-aware current, proposed, historical, and all-record views defined by [04-discover-and-understand-decisions.md](stories/04-discover-and-understand-decisions.md), including search, filtering, sorting, pagination, and lifecycle history.

### v0.6 — Supersede a Decision

Implement replacement decisions, atomic supersession, and navigable decision history as defined by [05-supersede-a-decision.md](stories/05-supersede-a-decision.md).

### v0.7 — Complete Organization Administration

Complete the remaining application-owned administration behavior from [06-administer-the-organization.md](stories/06-administer-the-organization.md), including membership projections, draft recovery, organization settings, administration history, expiry, and maintenance processing.

## First Complete Release

The first complete release is reached when the six documented journeys work together end to end and satisfy the outcome in [stories/overview.md](stories/overview.md). Planned version boundaries may be adjusted when implementation reveals a safer or more coherent slice, but story acceptance criteria remain authoritative.
