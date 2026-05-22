using System.Security.Cryptography;
using System.Text;

namespace CommunicationModule.Api.Services;

public sealed class TenantAccessService
{
    public (string PlainTextKey, string KeyHash) CreateAccessKey()
    {
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var plainTextKey = Convert.ToHexString(keyBytes);
        return (plainTextKey, HashKey(plainTextKey));
    }

    public string HashKey(string accessKey)
    {
        var keyBytes = Encoding.UTF8.GetBytes(accessKey.Trim());
        var hashBytes = SHA256.HashData(keyBytes);
        return Convert.ToHexString(hashBytes);
    }

    public bool Matches(string accessKey, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        var incomingHash = HashKey(accessKey);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(incomingHash),
            Encoding.UTF8.GetBytes(storedHash.Trim()));
    }
}