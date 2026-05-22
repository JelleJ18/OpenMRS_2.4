using System;
using CommunicationModule.Core.Enums;

namespace CommunicationModule.Core.Events;

public sealed record NotificationJobStatusChangedEvent(Guid NotificationJobId, Guid AppointmentId, Guid OrganisationId, NotificationJobStatus OldStatus, NotificationJobStatus NewStatus, DateTime Timestamp, string? Reason) : IIntegrationEvent;
