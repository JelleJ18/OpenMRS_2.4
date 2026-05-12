namespace CommunicationModule.Core.Models;

public class ProviderSubscription
{
    public Guid Id { get; set; }

    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;

    public string ProviderName { get; set; } = string.Empty;

    // API key is stored encrypted (AES-256) — never plain text
    public string EncryptedApiKey { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
