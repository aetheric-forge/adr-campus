namespace AdrCampus.Plugin;

/// <summary>
/// The package-specific rules an <see cref="OfficeDefinition"/> delegates to: its expected
/// descriptor id, which parent contracts its composition requires, and how to validate an
/// execution reference against its own closed dispatch table. Kept separate from
/// <see cref="OfficeDefinition"/> itself so the declarative schema/YAML-generation machinery
/// can be reused by a future office package (e.g. Talent) without inheriting Decisions'
/// specific id, bindings, or operations.
/// </summary>
public interface IOfficePackage
{
    string Id { get; }

    IReadOnlyCollection<string> RequiredParentContracts { get; }

    /// <summary>
    /// Validates an execution reference against this package's own closed dispatch table.
    /// Definition strings must never be used to load arbitrary types.
    /// </summary>
    void ValidateExecution(ExecutionReference execution);
}
