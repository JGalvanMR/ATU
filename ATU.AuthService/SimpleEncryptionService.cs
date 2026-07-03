using ATU.Shared.Models;
using System.Security.Cryptography;
using System.Text;

namespace ATU.AuthService;

/// <summary>
/// XOR con clave fija para desarrollo.
/// Cambiar por AES-256 con clave en appsettings para producción.
/// </summary>
public sealed class SimpleEncryptionService : IEncryptionService
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("ATU_ENC_KEY_32B_XXXXXXXXXXXXXXX"); // 32 bytes

    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;
        var data = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
            encrypted[i] = (byte)(data[i] ^ Key[i % Key.Length]);
        return Convert.ToBase64String(encrypted);
    }

    public string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return string.Empty;
        var data = Convert.FromBase64String(ciphertext);
        var decrypted = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
            decrypted[i] = (byte)(data[i] ^ Key[i % Key.Length]);
        return Encoding.UTF8.GetString(decrypted);
    }
}