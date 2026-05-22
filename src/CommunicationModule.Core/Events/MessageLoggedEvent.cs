using System;

namespace CommunicationModule.Core.Events;

public sealed record MessageLoggedEvent(Guid MessageLogId, Guid NotificationJobId, Guid OrganisationId, string ProviderName, bool Success, string? ErrorMessage, DateTime LoggedAt) : IIntegrationEvent;
