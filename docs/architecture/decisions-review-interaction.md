# Package-owned proposal-review interaction

Status: implementation slice for review

## Outcome

The Decisions package now describes proposal-review actions, content, fields, confirmation and result
messages. The website renders those descriptions and supplies navigation. Changes to the package's
review labels, text-field descriptions or confirmation content no longer require changing the review
page. Authoritative validation and authorization still run through the existing Recorder boundary.

## Responsibilities

| Layer | Responsibility |
| --- | --- |
| `contracts/src/AethericContracts.Interactions` | Domain-neutral provider/session interfaces and presentation records. |
| `AdrCampus.Plugin/ProposalReviewInteractionProvider` | Accept/reject availability; proposal and supersession content; note requirements; confirmation snapshots; mapping ADR results to messages. |
| `AdrCampus.Interactions.Blazor` | Encoded rendering, fields, validation display, generic confirmation/edit/retry state, and action links. No reference to ADR domain or application projects. |
| `AdrCampus.Web` | Provider selection, authenticated circuit adapters, existing URLs, safe return navigation, and the surrounding page layout. |

The record page asks the provider for its review actions. The package disables acceptance when the
frozen replacement target is no longer accepted and supplies the explanation. Direct invocation still
checks authority and repository state; disabled buttons are only a presentation aid.

The review page is a routing adapter. It supplies the subject and opaque action ID to the registered
`decisions.review` provider and renders an `InteractionPresenter` using `InteractionPanel`.
It no longer selects decision outcomes, defines note fields or maps ADR write statuses to messages.

## Confirmation behavior

Preparation validates and normalizes the note and captures the exact proposal timestamp, outcome,
operation ID and note in the server session. The package also supplies the complete proposal content
for review; the previous review page displayed only outcome and note at confirmation.

Confirmation uses an opaque session token. Retrying it uses the original operation ID and immutable
command. Unknown commit results preserve the token and disable edits until retry resolves the result.
The session enforces this even when called directly. Known authorization failures clear protected data;
conflicts and validation failures show package-owned explanations.

Identity is resolved again at execution through the existing trusted caller adapter. A confirmation
token does not replace authorization. The application and provider retain their existing persistence,
immutability, concurrency and idempotency rules.

## Contracts checkout and dependency

ADR Campus pins `aetheric-forge/aetheric-contracts` under `contracts/`, alongside its existing `runtime/`
submodule. Initialize both before building:

```sh
git submodule update --init --recursive
```

The new contracts assembly is independent of the existing membership-application assembly. Decisions
does not acquire a dependency on membership storage or the contracts repository's MongoDB driver.
The [shared contract notes](../../contracts/docs/interactions.md) define the consumer expectations.

Review and merge the shared-contract change before completing the ADR Campus change. If the contracts
change is squash-merged or rebased, update the submodule pin to the accepted commit. The implementation
branch initially points to the companion feature commit; it must be published before another checkout
can initialize that revision from GitHub.

## Verification and limits

Package tests cover action availability, required rejection reasons, normalized confirmation snapshots,
invalid and replaced tokens, revoked authority, conflicts, stale replacement targets, and response loss
after persistence. Generic renderer tests use a booking interaction to prove that labels, validation,
actions and confirmation are rendered without ADR-specific branches, and that plain text is encoded.

The full ADR Campus suite and website build verify integration. These are automated component and
service tests; live browser sign-in and review against deployment infrastructure remain a separate
acceptance step.

The renderer supports text sections and text fields followed by explicit confirmation. The [executable definition](executable-decisions-definition.md) now generates YAML and validates review
references. Dynamic assembly loading, multiple office instances, richer controls and durable interaction
sessions remain later work. The rest of the record detail page and the other ADR journeys retain their
existing presentation code.

For Talent Campus, the reusable seam is the interaction provider/session contract. A Talent package
can describe its own review operation and use the same renderer without importing ADR business types.
Its authoritative operations and provisioning side effects must define their own retry guarantees;
this slice does not assume that ADR's persistence model is sufficient for them.
