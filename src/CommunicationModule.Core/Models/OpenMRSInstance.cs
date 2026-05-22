namespace CommunicationModule.Core.Models;

public class OpenMRSInstance
{
    public Guid Id { get; set; }

    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;

    public string DisplayName { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "2.7";
    public string AccessKeyHash { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}