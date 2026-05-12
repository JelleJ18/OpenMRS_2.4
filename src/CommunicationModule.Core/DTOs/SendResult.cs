namespace CommunicationModule.Core.DTOs;

public class SendResult
{
    public bool Success { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? ErrorMessage { get; set; }
}
