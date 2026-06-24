namespace CommunicationModule.Core.Models;

public class Organisation
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ApiKeyHash { get; set; } = null!;

    // Staat nog niet in de db
    public ICollection<ProviderSubscription> ProviderSubscriptions { get; set; } = [];
    public ICollection<OpenMRSInstance> OpenMRSInstances { get; set; } = [];
}
