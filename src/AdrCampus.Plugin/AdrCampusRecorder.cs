using AethericForge.Runtime.Abstractions.Interfaces.Authorities;
using AethericForge.Runtime.Abstractions.Interfaces.Decisions.Services;
using AethericForge.Runtime.Models.Authorities;

namespace AdrCampus.Plugin;

/// <summary>
/// The Recorder Authority for an ADR Campus Decisions Office. It carries no operations of its own yet -
/// ADR Campus's existing drafting, proposal, and review behavior stays in its own Application services,
/// which serve the Blazor UI directly rather than round-tripping through this institutional capability.
/// This exists so a Decisions Institution can be constructed at all; a cross-institution operational
/// surface is a later addition once a concrete consumer (e.g. Talent) needs one.
/// </summary>
public sealed class AdrCampusRecorder : IRecorder
{
    public ITeam<IDecisionsClerk> Team { get; } = new Team<IDecisionsClerk>([]);
}
