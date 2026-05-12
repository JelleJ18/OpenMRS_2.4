using CommunicationModule.Core.Enums;

namespace CommunicationModule.Core.Models;

public class Appointment
{
    public Guid Id { get; set; }

    // ID as it comes from OpenMRS via FHIR
    public string FhirAppointmentId { get; set; } = string.Empty;

    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;

    // Stored encrypted — never plain text
    public string EncryptedPatientPhone { get; set; } = string.Empty;

    public DateTime AppointmentDateTime { get; set; }
    public string Location { get; set; } = string.Empty;
    public string? Instructions { get; set; }

    public AppointmentStatus Status { get; set; } = AppointmentStatus.Scheduled;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<NotificationJob> NotificationJobs { get; set; } = [];
}
