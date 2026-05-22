using CommunicationModule.Core.Enums;

namespace CommunicationModule.Dashboard.Models;

public record DashboardStats(
    int TotalSentToday,
    int TotalFailedToday,
    int TotalPendingJobs,
    List<ErrorSummary> RecentErrors);

public record ErrorSummary(Guid LogId, string ProviderName, string? ErrorMessage, DateTime LoggedAt);

public record MessageLogItem(
    Guid Id,
    Guid NotificationJobId,
    Guid OrganisationId,
    string ProviderName,
    bool Success,
    string? ErrorMessage,
    DateTime LoggedAt);

public record PagedResult<T>(List<T> Items, int Total, int Page, int PageSize);

public record NotificationJobItem(
    Guid Id,
    Guid AppointmentId,
    NotificationJobType Type,
    NotificationJobStatus Status,
    DateTime ScheduledFor,
    int RetryCount,
    DateTime? SentAt);

public record OrganisationItem(Guid Id, string Name, List<ProviderItem> Providers);

public record ProviderItem(Guid Id, string ProviderName, bool IsActive, DateTime CreatedAt);
