using System.Security.Cryptography;
using System.Text;

namespace CommunicationModule.Infrastructure.Services;

public sealed class AesEncryptionService
{
    private const int KeySizeBytes = 32;
    private const int IvSizeBytes = 16;

    private readonly byte[] _key;

    public AesEncryptionService(string base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
        {
            throw new ArgumentException("Encryption key is required.", nameof(base64Key));
        }

        _key = Convert.FromBase64String(base64Key);
        if (_key.Length != KeySizeBytes)
        {
            throw new ArgumentException("AES-256 requires a 32-byte key.", nameof(base64Key));
        }
    }

    public string Encrypt(string plainText)
    {
        if (plainText is null)
        {
            throw new ArgumentNullException(nameof(plainText));
        }

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Key = _key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var payload = new byte[IvSizeBytes + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, payload, 0, IvSizeBytes);
        Buffer.BlockCopy(cipherBytes, 0, payload, IvSizeBytes, cipherBytes.Length);

        return Convert.ToBase64String(payload);
    }

    public string Decrypt(string encryptedText)
    {
        if (encryptedText is null)
        {
            throw new ArgumentNullException(nameof(encryptedText));
        }

        var payload = Convert.FromBase64String(encryptedText);
        if (payload.Length <= IvSizeBytes)
        {
            throw new ArgumentException("Encrypted value is invalid.", nameof(encryptedText));
        }

        var iv = new byte[IvSizeBytes];
        var cipherBytes = new byte[payload.Length - IvSizeBytes];
        Buffer.BlockCopy(payload, 0, iv, 0, IvSizeBytes);
        Buffer.BlockCopy(payload, IvSizeBytes, cipherBytes, 0, cipherBytes.Length);

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Key = _key;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }
}