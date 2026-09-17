namespace AdrCampus.Plugin;

public static class DecisionsDefinition
{
    // Authoritative package definition. Regenerate institution/decisions-institution.yaml after edits.
    public static OfficeDefinition Current { get; } = new(
        new("decisions", "Decisions", "1.0.0", "An ADR-style decision-record office: drafts, proposes, reviews, records, and supersedes the architectural decisions made within its owning Institution."),
        [
            new("drafting", "Drafting", "Preparing and revising an architectural decision before it is proposed for review.", Capabilities: ["draft-decision", "revise-draft"]),
            new("proposal-review", "Proposal & Review", "Submitting a draft for organizational consideration and deciding its outcome.", Capabilities: ["propose-decision", "review-and-decide"]),
            new("discovery", "Discovery", "Finding and understanding the office's current and historical decision record.", Capabilities: ["discover-decisions"]),
            new("supersession", "Supersession", "Replacing an accepted decision with a later one while preserving its history.", Capabilities: ["supersede-decision"]),
            new("administration", "Administration", "Mapping the office onto its authoritative membership source and its ADR-specific upkeep.", Capabilities: ["administer-office"]),
        ],
        [
            new("decisions-office", "Decisions Office", "The operational body that carries out drafting, review, and record-keeping for this Institution's architectural decisions. See runtime/docs/specs/institution.md §5 - an Organization here derives its authority from the owning Institution rather than existing sovereignly."),
        ],
        [
            new("member", "Member", "Can read the office's decision record, create a draft, revise a draft they authored, and propose that draft for review.", Capabilities: ["draft-decision", "revise-draft", "propose-decision", "discover-decisions"]),
            new("maintainer", "Maintainer", "Has all Member capabilities and can additionally accept or reject a proposed decision, and administer the office's ADR-specific consequences of membership changes (e.g. recovering drafts whose authors have left).", Capabilities: ["draft-decision", "revise-draft", "propose-decision", "discover-decisions", "review-and-decide", "supersede-decision", "administer-office"]),
        ],
        [
            new("draft-decision", "Draft Decision", "Create a new decision draft, capturing its context, decision, and consequences."),
            new("revise-draft", "Revise Draft", "Revise a draft's content prior to it being proposed."),
            new("propose-decision", "Propose Decision", "Submit a complete draft for organizational review."),
            new("review-and-decide", "Review and Decide", "Accept or reject a proposed decision, recording who decided and why.", Execution: new(DecisionsOperations.ReviewId, 1, ProposalReviewInteractionProvider.ProviderId)),
            new("discover-decisions", "Discover Decisions", "Browse and search the office's current and historical decision record."),
            new("supersede-decision", "Supersede Decision", "Propose and, on acceptance, link a replacement for a previously accepted decision."),
            new("administer-office", "Administer Office", "Map the office onto its authoritative member/maintainer source and perform ADR-specific maintenance (e.g. expired-draft recovery)."),
        ],
        [
            new("draft-workspace", "Draft Workspace", "Draft staging currently supplied by the owning Institution; dedicated office workspace provisioning is deferred.", Type: "staging", Ownership: "parent"),
            new("decision-record", "Decision Record", "The durable, shared record of proposed, accepted, rejected, and superseded decisions.", Type: "knowledge", Ownership: "parent"),
        ],
        [
            new("draft-to-proposal", "Draft to Proposal", "A Member drafts, optionally revises, and then proposes a decision for review."),
            new("proposal-review", "Proposal Review", "A Maintainer reviews a proposed decision and accepts or rejects it.", Execution: new(DecisionsOperations.ReviewId, 1, ProposalReviewInteractionProvider.ProviderId)),
            new("supersession", "Supersession", "A later accepted decision is linked as the replacement for an earlier accepted decision, which moves to Superseded without being rewritten."),
        ],
        [
            new("active-member-only", "Active Member Only", "Only an active Member of the owning Institution may draft, revise, or propose a decision."),
            new("active-maintainer-only", "Active Maintainer Only", "Only an active Maintainer of the owning Institution may accept or reject a proposal."),
            new("immutable-accepted-record", "Immutable Accepted Record", "An accepted decision is never rewritten to alter history; material change happens only through a new decision that supersedes it."),
        ],
        [
            new("ILibrary", "Parent knowledge store for shared records.", true),
            new("IWorkbench", "Parent draft staging required by the review repositories.", true),
            new("IArchive", "Used by remaining host-owned administration composition.", false),
            new("IPostOffice", "Used by remaining host-owned maintenance composition.", false),
            new("IRegistrar", "Identity backing supplied through trusted host adapters.", false)
        ],
        DecisionsOperations.Package);
}
