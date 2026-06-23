using System;

namespace CommunicationModule.Core.Events;

public sealed record AppointmentReceivedEvent(Guid AppointmentId, Guid OrganisationId, DateTime AppointmentDateTime, string Location) : IIntegrationEvent;
