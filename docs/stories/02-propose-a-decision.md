# Propose a Decision

## User Story

As the author of a complete ADR draft, I want to propose it to my organization so that its maintainers can consider it as an architectural decision.

## Value

A private draft becomes useful to the organization when it is deliberately submitted for consideration. Proposal establishes a clear boundary between an author's working material and a stable record that the organization may review, accept, or reject.

## Actor

The primary actor is the active organization member who is currently identified as the draft's author.

A maintainer may propose a draft they authored because a maintainer has all member capabilities. Maintainer status does not cause the proposal to be accepted automatically.

## Preconditions

- The organization exists.
- The actor is authenticated and is an active member of the organization.
- The ADR exists, belongs to the organization, and has status `Draft`.
- The actor is the current author of the draft.
- The draft has not expired.

## Primary Scenario

1. The author opens a draft they believe is ready for review.
2. The author chooses to propose the ADR.
3. ADR Campus validates the title, context, decision, and consequences.
4. ADR Campus presents the ADR as it will appear to the organization, explains that its content will become read-only, and asks the author to confirm the proposal.
5. The author reviews the preview and confirms.
6. ADR Campus changes the ADR's status from `Draft` to `Proposed` as a single operation.
7. ADR Campus records the author as the proposer and records the proposal time.
8. ADR Campus makes the proposed ADR visible to all active members of the organization.
9. ADR Campus confirms to the author that the ADR was proposed and shows the persisted proposal.

## Proposal Requirements

An ADR is complete enough to propose when:

- its title satisfies the title rules defined by the drafting journey;
- its context contains between 1 and 4,000 characters and includes at least one letter or number;
- its decision contains between 1 and 4,000 characters and includes at least one letter or number; and
- its consequences contain between 1 and 4,000 characters and include at least one letter or number.

ADR Campus removes leading and trailing whitespace from each section before evaluating these requirements and persisting the proposal. Narrative sections may contain ordinary text and line breaks, but may not contain other control characters. The application does not judge whether the reasoning is persuasive; that judgment belongs to the maintainers who review it.

## Visibility

A proposed ADR is part of the organization's shared decision record. It is visible to every active organization member, including its author and the maintainers.

Its `Proposed` status must be clear wherever it appears. A proposal is under consideration and must not be represented as an accepted decision or as part of the organization's current architecture.

People who are not active organization members cannot access the proposal through this journey.

## Content Stability

Proposal freezes the ADR content presented for review. Its title, context, decision, consequences, identifier, and author cannot be changed while it has `Proposed` status.

This prevents reviewers from making a decision about content that changes during consideration. Withdrawal, amendment, and resubmission workflows are outside the first release. If a proposal is rejected, later reconsideration begins with a new draft and retains the rejected proposal as history.

## Authorization and Lifecycle Rules

- Only the current active author may propose a draft.
- A maintainer cannot propose another member's draft merely because they are a maintainer.
- A departed member's draft must be reassigned before its new author can propose it.
- Only an ADR in `Draft` status may become `Proposed`.
- Proposal changes the existing ADR; it does not create a second ADR or change its stable identifier.
- Proposal records the current author as proposer and records one proposal time.
- Proposal makes the ADR visible to all active organization members.
- Proposal makes the ADR content read-only.
- Proposal does not accept or reject the ADR.
- Proposal does not imply endorsement by a maintainer or by the organization.
- The client supplies a unique operation identifier with the proposal command and reuses it when retrying that operation.

## Alternate and Failure Scenarios

### Required content is incomplete

If any required section is empty after surrounding whitespace is removed, ADR Campus identifies each incomplete section and does not propose the ADR. The ADR remains a private, editable draft.

### Narrative content is not valid

If context, decision, or consequences exceeds 4,000 characters, contains no letter or number, or contains a control character other than a line break, ADR Campus identifies each invalid section and its applicable constraint. It does not propose the ADR, and the ADR remains a private, editable draft.

### The author does not confirm

If the author leaves or cancels the confirmation, ADR Campus does not propose the ADR. Its content and `Draft` status remain unchanged.

### Someone other than the author attempts to propose the draft

ADR Campus refuses the operation, reveals no private draft content to an unauthorized person, and preserves the draft unchanged.

### The author is no longer an active member

ADR Campus refuses the operation and preserves the draft unchanged. A maintainer may use the recovery and reassignment process defined by the drafting journey while its recovery window remains open.

### The draft changed after the preview was prepared

ADR Campus does not propose content different from the content the author confirmed. It informs the author that the draft changed and requires a new validation and preview.

### The ADR is no longer a draft

If the ADR has already been proposed or otherwise left `Draft` status, ADR Campus does not perform another transition or record another proposal event. It shows the actor the current persisted status when permitted.

### Proposal fails

If ADR Campus cannot persist the proposal, it does not report success or expose a partially proposed record. The ADR remains in its last successfully persisted state so the author can safely determine whether to retry.

### The author retries after an uncertain outcome

ADR Campus ensures that repeated submission of the same proposal does not create a duplicate ADR, duplicate proposal event, or second lifecycle transition.

## Acceptance Criteria

### Propose a complete draft

Given a complete ADR in `Draft` status and its authenticated active author,
when the author previews and confirms the proposal,
then ADR Campus changes the existing ADR's status to `Proposed`, records its author as proposer, records one proposal time, preserves its stable identifier, and makes it visible to all active organization members.

### Require complete content

Given an ADR in `Draft` status,
when its active author attempts to propose it with an empty title, context, decision, or consequences after surrounding whitespace is removed,
then ADR Campus identifies every incomplete section, keeps the ADR in `Draft` status, and does not create a proposal event.

### Enforce narrative constraints

Given an ADR in `Draft` status,
when its active author attempts to propose it with context, decision, or consequences that exceeds 4,000 characters, contains no letter or number, or contains a control character other than a line break,
then ADR Campus identifies every invalid section and its applicable constraint, keeps the ADR in `Draft` status, and does not create a proposal event.

### Normalize narrative content

Given an otherwise complete ADR whose context, decision, or consequences contains leading or trailing whitespace,
when its active author successfully proposes it,
then ADR Campus removes that surrounding whitespace from each affected section before persisting the proposal.

### Require confirmation

Given a complete draft displayed in the proposal preview,
when its author does not confirm the proposal,
then ADR Campus preserves the draft's content, visibility, and `Draft` status.

### Confirm the exact content

Given that a draft changes after its proposal preview is prepared,
when the author confirms the stale preview,
then ADR Campus does not propose the ADR and requires the author to review the current content before confirming again.

### Restrict proposal to the author

Given an ADR in `Draft` status,
when anyone other than its authenticated active author attempts to propose it,
then ADR Campus refuses the operation, persists no lifecycle change, and reveals no private content to an unauthorized person.

### Preserve ADR identity

Given a draft with a stable identifier,
when it is successfully proposed,
then the proposal retains the same identifier, author, creation time, and content that the author confirmed.

### Freeze proposed content

Given an ADR in `Proposed` status,
when any member or maintainer attempts to change its title, context, decision, consequences, identifier, or author,
then ADR Campus refuses the change and preserves the proposal unchanged.

### Share the proposal with members

Given an ADR in `Proposed` status,
when any active member of its organization views the shared decision record,
then the member can find and read the ADR and can clearly distinguish it from an accepted decision.

### Keep the proposal private to the organization

Given an ADR in `Proposed` status,
when a person who is not an active member of its organization attempts to access it,
then ADR Campus refuses access and reveals no proposal content.

### Make proposal atomic

Given a draft whose proposal cannot be fully persisted,
when the proposal operation fails,
then ADR Campus does not report success and leaves no partially proposed or partially shared ADR.

### Make proposal idempotent

Given an ADR that has already been successfully proposed,
when a proposal request with the same client-generated operation identifier is repeated,
then ADR Campus creates no duplicate ADR or proposal event and preserves the original proposal time.

### Keep authority unambiguous

Given an ADR in `Proposed` status,
when it is displayed anywhere in ADR Campus,
then its status is apparent and it is not represented as an accepted organizational decision.

## Out of Scope

This journey does not define:

- discussion, comments, or requested changes;
- notification of maintainers;
- assignment of a particular reviewer;
- voting, quorum, or multi-stage approval;
- amendment or withdrawal of a proposal;
- editing proposed content;
- acceptance or rejection;
- reconsideration of a rejected proposal; or
- supersession of an accepted ADR.

## Architectural Implications

The architecture must support:

- validation rules that differ by lifecycle transition;
- authorization based on active membership and current authorship;
- a preview tied to the exact draft revision being confirmed;
- an atomic and idempotent `Draft` to `Proposed` transition;
- client-generated operation identifiers used to recognize retries;
- immutable proposed content;
- durable proposer and proposal-time metadata;
- organization-wide member visibility without public exposure; and
- a clear distinction between proposed and accepted records.

These implications identify required system behavior. They do not prescribe interface, persistence, transaction, or concurrency mechanisms.
