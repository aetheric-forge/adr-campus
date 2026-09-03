using AdrCampus.Core.Domain;

namespace AdrCampus.Core.Maintenance;

public enum MaintenanceJob { PurgeExpiredDrafts }

public sealed record MaintenanceCommand(Guid Id, OrganizationId OrganizationId, MaintenanceJob Job, DateTimeOffset RequestedAtUtc, string Source);

public enum MaintenanceRunStatus { Completed, Partial, Failed }

public sealed record MaintenanceRunOutcome(Guid CommandId, MaintenanceRunStatus Status, int ProcessedCount, int RemainingCount, DateTimeOffset OccurredAtUtc, string? FailureReason = null);

public sealed record MaintenanceRunRecord(MaintenanceCommand Command, MaintenanceRunOutcome? Outcome, bool IsCollected);
