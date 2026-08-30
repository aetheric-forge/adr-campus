# Review and Decide

## User Story

As an organization maintainer, I want to review a proposed ADR and accept or reject it so that the organization's architectural decision record clearly reflects what it has and has not adopted.

## Value

A proposal becomes authoritative only through an explicit act by someone entrusted to decide on behalf of the organization. Recording both the outcome and the person responsible makes the decision record trustworthy, while preserving rejected proposals shows what the organization considered without confusing them with its current architecture.

## Actor

The primary actor is an authenticated active member who currently holds the maintainer role in the organization.

For the first release, a maintainer may decide a proposal they authored. This permits a basic organization with a single maintainer to complete the workflow. The decision record makes self-approval visible through its proposer and decider metadata.

## Preconditions

- The organization exists.
- The actor is authenticated and is an active member of the organization.
- The actor currently holds the maintainer role.
- The ADR exists, belongs to the organization, and has status `Proposed`.

The proposal remains eligible for a decision if its author is no longer an active organization member.

## Primary Scenario

1. The maintainer opens the organization's proposed ADRs.
2. The maintainer selects a proposal to review.
3. ADR Campus displays the exact title, context, decision, and consequences submitted by the author, together with the ADR's identifier, author, proposer, and proposal time.
4. ADR Campus makes the `Proposed` status clear and offers the maintainer the actions to accept or reject it.
5. The maintainer selects an outcome.
6. For acceptance, the maintainer may enter an explanatory note. For rejection, the maintainer enters a required reason.
7. ADR Campus presents the selected outcome and note and asks the maintainer to confirm that the decision is final.
8. The maintainer confirms.
9. ADR Campus changes the ADR's status from `Proposed` to `Accepted` or `Rejected` as a single operation.
10. ADR Campus records the maintainer as the decider, records the decision time, and records the explanatory note when present.
11. ADR Campus confirms the persisted outcome and displays the decided ADR.

## Decision Note

An acceptance note is optional. When present, it can record clarification or the maintainer's rationale without changing the content of the proposal.

A rejection reason is required so that the historical record explains why the organization did not adopt the proposal.

A decision note or rejection reason:

- contains no more than 1,000 characters after surrounding whitespace is removed;
- when present or required, contains at least one letter or number;
- may contain ordinary text and line breaks; and
- may not contain other control characters.

Decision notes are metadata attached to the outcome. They do not alter the proposed context, decision, or consequences.

## Outcomes

### Accepted

An accepted ADR is an architectural decision of the organization. It becomes part of the organization's current architectural record and remains visible to every active member.

Its accepted content and decision metadata are read-only. A later material change requires a new ADR that supersedes it.

### Rejected

A rejected ADR is a historical proposal that the organization considered but did not adopt. It remains in the shared decision record and visible to every active member, but it is not part of the organization's current architecture.

Its rejected content and decision metadata are read-only. Later reconsideration begins with a new draft rather than changing the rejected record.

## Authorization and Lifecycle Rules

- Only an active organization maintainer may accept or reject a proposal.
- A maintainer may decide a proposal they authored.
- Any one active maintainer is sufficient to decide a proposal in the first release.
- Only an ADR in `Proposed` status may become `Accepted` or `Rejected`.
- Acceptance and rejection are mutually exclusive and final.
- The first successfully persisted decision determines the outcome.
- A decision records exactly one decider and one decision time.
- Rejection records exactly one non-empty reason.
- Acceptance may record one optional note.
- The client supplies a unique operation identifier with each decision command and reuses it when retrying that operation.
- A decision does not change the ADR's identifier, author, proposer, proposal time, or proposed content.
- A decided ADR remains visible to active organization members and inaccessible to non-members.
- Loss of membership by the proposal's author does not remove or invalidate the shared proposal.

## Alternate and Failure Scenarios

### A member who is not a maintainer attempts to decide

ADR Campus refuses the operation and preserves the proposal unchanged.

### The maintainer's authority has changed

If the actor no longer holds the maintainer role when they confirm, ADR Campus refuses the operation and preserves the proposal unchanged, even if the review page was opened while they were a maintainer.

### A rejection reason is missing or invalid

ADR Campus identifies the applicable constraint and does not reject the ADR. The proposal remains available for review.

### An optional acceptance note is invalid

ADR Campus identifies the applicable constraint and does not accept the ADR. The proposal remains available for review. The maintainer may correct the note or remove it.

### The maintainer does not confirm

ADR Campus does not record a decision. The ADR remains `Proposed` and its content and metadata remain unchanged.

### The proposal changed after review began

ADR Campus does not decide content different from the content the maintainer reviewed. It informs the maintainer that the proposal record changed and requires them to review the current persisted record before confirming again.

### Another maintainer decides first

If the ADR is no longer `Proposed` when the maintainer confirms, ADR Campus does not record another decision or overwrite the existing outcome. It shows the maintainer the persisted outcome, decider, decision time, and note when present.

### The decision cannot be persisted

ADR Campus does not report success or expose a partial outcome. The ADR remains in its last successfully persisted state so the maintainer can determine whether to retry safely.

### The maintainer retries after an uncertain outcome

ADR Campus ensures that repeating the same decision request does not create another decision event or alter the original decision time.

## Acceptance Criteria

### Accept a proposal

Given an ADR in `Proposed` status and an authenticated active maintainer,
when the maintainer selects acceptance, provides no note or a valid optional note, and confirms,
then ADR Campus changes the ADR's status to `Accepted`, records the maintainer as its decider, records one decision time and the note when present, and includes the ADR in the organization's current architectural record.

### Reject a proposal

Given an ADR in `Proposed` status and an authenticated active maintainer,
when the maintainer selects rejection, provides a valid reason, and confirms,
then ADR Campus changes the ADR's status to `Rejected`, records the maintainer as its decider, records one decision time and the rejection reason, and does not include the ADR in the organization's current architectural record.

### Permit self-approval

Given a proposed ADR whose author is an active maintainer,
when that maintainer accepts or rejects their own proposal in accordance with this journey,
then ADR Campus records the decision and preserves both the proposer and decider metadata so their identities can be compared.

### Require a rejection reason

Given an active maintainer rejecting a proposal,
when the reason is empty after surrounding whitespace is removed,
then ADR Campus identifies that a reason is required, records no decision, and keeps the ADR in `Proposed` status.

### Validate a decision note

Given an active maintainer accepting or rejecting a proposal,
when the supplied note exceeds 1,000 characters, contains no letter or number, or contains a control character other than a line break,
then ADR Campus identifies the applicable constraint, records no decision, and keeps the ADR in `Proposed` status.

### Normalize a decision note

Given an otherwise valid decision note with leading or trailing whitespace,
when the maintainer confirms the decision,
then ADR Campus removes that surrounding whitespace before persisting the note.

### Restrict decisions to maintainers

Given an ADR in `Proposed` status,
when a person who is not currently an active maintainer attempts to accept or reject it,
then ADR Campus refuses the operation and persists no lifecycle or metadata change.

### Recheck authority at confirmation

Given a person who opened a proposal while they were an active maintainer but no longer holds that authority,
when they confirm a decision,
then ADR Campus refuses the operation and keeps the ADR in `Proposed` status.

### Preserve proposed content and identity

Given a proposed ADR,
when a maintainer successfully decides it,
then ADR Campus preserves its identifier, author, proposer, proposal time, title, context, decision, and consequences exactly as proposed.

### Make the decision final

Given an ADR in `Accepted` or `Rejected` status,
when any member or maintainer attempts to change its outcome, content, decision note, decider, or decision time,
then ADR Campus refuses the change and preserves the decided record unchanged.

### Resolve concurrent decisions

Given two maintainers attempting to decide the same proposal,
when one decision is persisted first,
then ADR Campus preserves that outcome as the only decision and informs the other maintainer that the proposal has already been decided.

### Make the decision atomic

Given a decision that cannot be fully persisted,
when the operation fails,
then ADR Campus does not report success and leaves no partially accepted or rejected ADR.

### Make the decision idempotent

Given an ADR that has already been decided by a successful request,
when a decision request with the same client-generated operation identifier is repeated,
then ADR Campus creates no duplicate decision event and preserves the original status, decider, decision time, and note.

### Preserve organization-only visibility

Given an ADR in `Accepted` or `Rejected` status,
when an active member of its organization accesses it, the member can read the complete record,
and when a non-member attempts to access it, ADR Campus refuses access and reveals no record content.

### Keep authority unambiguous

Given a decided ADR,
when it is displayed anywhere in ADR Campus,
then its outcome is apparent, its decider and decision time are available, and only an `Accepted` ADR is represented as a current organizational decision.

## Out of Scope

This journey does not define:

- comments, discussion, or requests for changes;
- notifications;
- reviewer assignment;
- voting, quorum, or multi-stage approval;
- mandatory separation between proposer and decider;
- withdrawal or amendment of a proposal;
- reversal or reopening of a decision;
- editing decided content or metadata;
- reconsideration of a rejected proposal; or
- supersession of an accepted ADR.

## Architectural Implications

The architecture must support:

- authorization based on current active membership and maintainer role;
- revalidation of authority when a decision is persisted;
- a decision confirmation tied to the exact proposal reviewed;
- atomic and idempotent `Proposed` to `Accepted` or `Rejected` transitions;
- client-generated operation identifiers used to recognize retries;
- deterministic resolution of concurrent decisions;
- immutable decided content and metadata;
- durable outcome, decider, decision-time, and note metadata;
- distinct treatment of accepted and rejected records; and
- preservation of organization-only visibility across lifecycle changes.

These implications identify required system behavior. They do not prescribe interface, persistence, transaction, or concurrency mechanisms.
