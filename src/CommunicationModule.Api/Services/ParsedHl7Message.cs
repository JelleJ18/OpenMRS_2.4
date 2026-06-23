namespace CommunicationModule.Api.Services;
public class ParsedHl7Message
{
    public string MessageId { get; set; } = "";
    public string PatientId { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string Location { get; set; } = "";
    public DateTimeOffset? AppointmentDateTime { get; set; }
    public Guid OrganisationId { get; set; }
}