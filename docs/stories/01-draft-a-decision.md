# Draft a Decision

## User Story

As an organization member, I want to create and revise an ADR draft so that I can develop the reasoning for an architectural decision before asking the organization to consider it.

## Value

Architectural reasoning is often incomplete when it is first written down. Drafting gives an author a durable workspace in which to clarify the context, state the proposed decision, and consider its consequences without presenting unfinished work as an organizational position.

## Actor

The primary actor is an authenticated member of the organization.

A maintainer may also draft an ADR because a maintainer has all member capabilities.

## Preconditions

- The organization exists.
- The actor is authenticated.
- The actor is an active member of the organization.

## Primary Scenario

1. The member chooses to create an ADR.
2. ADR Campus presents a new draft containing fields for a title, context, decision, and consequences.
3. The member gives the draft a title and records as much of the decision as is presently known.
4. The member saves the draft.
5. ADR Campus assigns the ADR a stable identifier, records the member as its author, and records its status as `Draft`.
6. ADR Campus confirms that the draft was saved and shows the persisted content.
7. The author returns to the draft later.
8. The author revises one or more editable fields and saves the draft again.
9. ADR Campus preserves the identifier, authorship, and `Draft` status while recording the revised content and latest modification time.
10. The author previews the draft as it will appear when proposed and returns to editing without changing its lifecycle status.

The author may repeat the revision steps until the draft is ready to be proposed.

## Draft Content

A draft contains:

- a title identifying the architectural question or decision;
- the context and forces that make a decision necessary;
- the proposed decision;
- the expected positive and negative consequences; and
- system-managed identity, authorship, status, and timestamps.

Only the title is required to create the draft. Context, decision, and consequences may be incomplete while the ADR remains in `Draft` status. The requirements for proposing a draft belong to the proposal journey.

A title is normalized by removing leading and trailing whitespace. The normalized title must contain between 5 and 160 characters, must contain at least one letter or number, and must not contain control characters. Titles are not required to be unique because distinct decisions may reasonably have similar names.

## Visibility

A draft is personal working material. While its author is an active member, it is visible only to its author and is not part of the organization's shared decision record.

If the author ceases to be an active member, a maintainer may access the draft solely to reassign it during a 30-day recovery window. Other members cannot access it. A successful reassignment identifies a new active member as the author and records the previous author, new author, responsible maintainer, and time of reassignment in the ADR's history.

If no maintainer reassigns the draft within 30 days after its author ceases to be an active member, the draft enters an expired state and is treated as though it does not exist anywhere in the web application. It can no longer be listed, viewed, edited, reassigned, previewed, or proposed. A background maintenance task periodically purges expired drafts and their content from storage. Expired draft content is not part of the organization's decision record.

Maintainers recover drafts through a dedicated recovery view that is separate from shared ADR discovery. The view lists only drafts whose authors are no longer active and whose recovery windows remain open. It exposes the draft identifier, title, former author, expiration time, and the controls needed to select an active member and complete reassignment. Access to the draft's full content remains limited to the recovery operation and is not exposed through browse, search, result counts, or ordinary direct-record access.

The application must not describe or visually present a draft as an accepted organizational decision.

## Authorization and Lifecycle Rules

- Any active organization member may create a draft.
- The member who creates the draft becomes its author.
- Only the active author may view or revise the draft during ordinary drafting.
- Creating or revising a draft does not change its status.
- Draft content may be revised freely until the ADR is proposed.
- An ADR identifier and author do not change when its draft content is revised.
- A person who is no longer an active member cannot create or revise drafts in the organization.
- A maintainer may access a departed member's draft only during its recovery window and only for recovery and reassignment.
- A draft may be reassigned only to an active organization member.
- Reassignment must be recorded in the ADR's history and must not change its identifier or `Draft` status.
- An unreassigned draft expires 30 days after its author ceases to be an active member.
- Expiration makes a draft immediately inaccessible even if background purging has not yet removed its stored content.
- A background maintenance task periodically purges expired drafts.
- Drafting does not grant the author authority to accept an ADR on behalf of the organization.

## Alternate and Failure Scenarios

### The title is missing

If the member attempts to create a draft without a title, ADR Campus explains that a title is required and does not create the ADR. Content already entered remains available for correction.

### The title is not valid

If the normalized title is shorter than 5 characters, longer than 160 characters, contains no letter or number, or contains a control character, ADR Campus explains the applicable constraint and does not create or save the invalid revision. Content already entered remains available for correction.

### A non-member attempts to create a draft

ADR Campus refuses the operation and does not create an ADR.

### Someone other than the author attempts to access a draft

While the author remains active, ADR Campus does not reveal the draft or its content to another member or maintainer and does not permit it to be changed.

### The author is no longer an active member

ADR Campus refuses the revision and preserves the last successfully saved version of the draft.

A maintainer may access the draft during the 30-day recovery window and reassign it to an active member. ADR Campus records the reassignment in the ADR's history. Once reassigned, the new author may view and revise the draft and the former author may not.

### The recovery window expires

If a departed member's draft is not reassigned within 30 days, ADR Campus expires it and removes it from every web application view. A later attempt to list, view, revise, reassign, preview, or propose the draft is refused and does not restore the draft. Background maintenance may purge it at any later time without changing its already-inaccessible behavior.

### Saving fails

ADR Campus tells the author that the changes were not saved. It does not present the failed changes as persisted, and it keeps the author's entered content available when reasonably possible so that the author can retry without retyping it.

### The draft changed after it was opened

ADR Campus does not silently overwrite a more recently persisted revision. It informs the author that the draft has changed and requires the conflict to be resolved before saving.

## Acceptance Criteria

### Create a draft

Given an authenticated active member of the organization,
when the member creates an ADR with a non-empty title,
then ADR Campus creates exactly one ADR with a stable identifier, identifies the member as its author, records its status as `Draft`, and records its creation and modification times.

### Save incomplete content

Given an active member creating a draft with a title,
when context, decision, or consequences are empty or incomplete,
then ADR Campus allows the draft to be saved without treating it as proposed or accepted.

### Require a title

Given an active member creating a draft,
when the title is empty or consists only of whitespace,
then ADR Campus does not create the ADR and explains how to correct the problem.

### Enforce title constraints

Given an active member creating or revising a draft,
when its normalized title is shorter than 5 characters, longer than 160 characters, contains no letter or number, or contains a control character,
then ADR Campus does not persist the invalid title and explains the applicable constraint.

### Normalize a title

Given an otherwise valid draft title with leading or trailing whitespace,
when the draft is saved,
then ADR Campus removes that surrounding whitespace before persisting and displaying the title.

### Revise a draft

Given an ADR in `Draft` status and its active author,
when the author changes its title, context, decision, or consequences and saves,
then ADR Campus persists the new content, updates the modification time, and preserves the ADR's identifier, author, creation time, and status.

### Preserve the last saved version

Given an existing draft,
when an attempted revision cannot be persisted,
then the last successfully saved version remains unchanged and ADR Campus tells the author that the revision was not saved.

### Restrict creation to members

Given a person who is not an active member of the organization,
when that person attempts to create a draft,
then ADR Campus refuses the operation and creates no ADR.

### Restrict access to the author

Given an ADR in `Draft` status whose author remains an active member,
when a member other than its author attempts to view or revise it,
then ADR Campus reveals no draft content and persists no change.

### Recover a departed member's draft

Given a draft whose author ceased to be an active member fewer than 30 days ago,
when a maintainer reassigns it to an active member,
then ADR Campus preserves the draft's identifier, content, creation time, and `Draft` status; identifies the new member as its author; and records the previous author, new author, responsible maintainer, and reassignment time in its history.

### Restrict recovery access

Given a draft whose author is no longer an active member and whose recovery window remains open,
when a non-maintainer attempts to access or reassign it,
then ADR Campus reveals no draft content and persists no change.

### List drafts eligible for recovery

Given one or more drafts whose authors are no longer active and whose recovery windows remain open,
when an active maintainer opens the dedicated recovery view,
then ADR Campus lists each eligible draft by identifier, title, former author, and expiration time and provides reassignment controls without adding the drafts to shared discovery.

### Expire an unreassigned draft

Given a draft whose author ceased to be an active member 30 days ago and which has not been reassigned,
when its recovery window ends,
then ADR Campus expires the draft and no longer permits it to be viewed, revised, reassigned, previewed, or proposed.

### Do not overwrite a newer revision

Given that a draft has changed since the author opened it,
when the author attempts to save an older version,
then ADR Campus preserves the newer persisted version and informs the author of the conflict.

### Purge an expired draft

Given a draft whose recovery window has expired,
when background maintenance processes expired drafts,
then ADR Campus permanently removes the draft and its content from storage without first making it accessible anywhere in the web application.

### Keep authority unambiguous

Given any saved draft,
when it is displayed to its author,
then its `Draft` status is apparent and it is not represented as a decision accepted by the organization.

### Preview the proposed ADR

Given a draft belonging to its active author,
when the author requests a preview,
then ADR Campus presents the draft in the form used for a proposed ADR, clearly labels the presentation as a preview, and does not save content or change the ADR's `Draft` status.

## Out of Scope

This journey does not define:

- proposing a draft for review;
- review, discussion, acceptance, or rejection;
- co-authors or collaborative editing;
- revision history within the draft phase;
- attachments or rich document publishing;
- templates or custom ADR fields;
- automatic saving;
- manual draft deletion or archival;
- recovery of an expired draft; or
- links that supersede earlier ADRs.

## Architectural Implications

The architecture must support:

- organization-scoped ADR identity;
- authentication and active-membership checks;
- author-only access to draft content;
- time-bounded maintainer access to departed members' drafts;
- auditable reassignment and draft expiration;
- a maintainer-only recovery read model that remains separate from shared discovery;
- background expiration processing and eventual purging of expired draft content;
- explicit ADR lifecycle state;
- durable creation and modification metadata;
- validation that distinguishes draft requirements from proposal requirements;
- persistence outcomes that do not falsely report success;
- detection of conflicting updates; and
- a read-only proposal preview that has no lifecycle side effects.

These implications identify required system behavior. They do not prescribe storage, interface, or concurrency mechanisms.
