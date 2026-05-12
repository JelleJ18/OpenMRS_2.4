namespace CommunicationModule.Core.Models;

public class Organisation
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public ICollection<ProviderSubscription> ProviderSubscriptions { get; set; } = [];
}
