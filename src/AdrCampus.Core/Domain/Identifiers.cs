namespace AdrCampus.Core.Domain;

public readonly record struct AdrId
{
    public AdrId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An ADR identifier cannot be empty.", nameof(value));
        }
        Value = value;
    }

    public Guid Value { get; }
    public static AdrId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public sealed record OrganizationId
{
    public OrganizationId(string value)
    {
        Value = Required(value, "An organization identifier is required.");
    }

    public string Value { get; }
    public override string ToString() => Value;

    private static string Required(string value, string message) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(message, nameof(value))
            : value.Trim();
}

public sealed record MemberId
{
    public MemberId(string value)
    {
        Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A stable member identifier is required.", nameof(value))
            : value.Trim();
    }

    public string Value { get; }
    public override string ToString() => Value;
}

public readonly record struct OperationId
{
    public OperationId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An operation identifier cannot be empty.", nameof(value));
        }
        Value = value;
    }

    public Guid Value { get; }
    public static OperationId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}
