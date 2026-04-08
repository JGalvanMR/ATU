using ATU.Shared.Models;
using System.Security.Cryptography;
using System.Text;

namespace ATU.AuthService;

/// <summary>
/// Cifrado AES-256 con clave estable desde configuración.
/// La clave se lee de appsettings.json — nunca se genera aleatoriamente
/// para que los secrets cifrados sobrevivan reinicios del servidor.
/// </summary>
public sealed class AesEncryptionService : IEncryptionService
{
    private readonly byte[] _key;

    public AesEncryptionService(IConfiguration configuration)
    {
        // Lee la clave desde configuración (32 bytes en Base64)
        var keyBase64 = configuration["Atu:EncryptionKey"];

        if (!string.IsNullOrWhiteSpace(keyBase64))
        {
            _key = Convert.FromBase64String(keyBase64);

            if (_key.Length != 32)
                throw new InvalidOperationException(
                    $"Atu:EncryptionKey debe ser exactamente 32 bytes en Base64. " +
                    $"Longitud actual: {_key.Length} bytes.");
        }
        else
        {
            // Si no hay clave configurada, usa una derivada del nombre de la app.
            // NOTA: esto es solo para desarrollo — en producción configura Atu:EncryptionKey
            Console.WriteLine("[ATU] ⚠️  Atu:EncryptionKey no configurada. " +
                              "Usando clave de desarrollo. Configúrala en appsettings.json.");

            _key = DeriveKeyFromPassphrase("ATU-CAMARAS-FRIAS-DEVELOPMENT-KEY-2024");
        }
    }

    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Formato: [16 bytes IV][N bytes ciphertext] → Base64
        var result = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return string.Empty;

        var data = Convert.FromBase64String(ciphertext);

        if (data.Length < 16)
            throw new CryptographicException("Ciphertext demasiado corto para contener IV.");

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = data[..16];  // primeros 16 bytes = IV

        using var decryptor = aes.CreateDecryptor();
        var decrypted = decryptor.TransformFinalBlock(data, 16, data.Length - 16);

        return Encoding.UTF8.GetString(decrypted);
    }

    /// <summary>
    /// Deriva 32 bytes deterministas desde una frase.
    /// Usado como fallback de desarrollo — siempre produce la misma clave.
    /// </summary>
    private static byte[] DeriveKeyFromPassphrase(string passphrase)
    {
        // PBKDF2 con sal fija (solo para desarrollo)
        var salt = Encoding.UTF8.GetBytes("ATU-SALT-2024-MRLUCKY");
        using var pbkdf2 = new Rfc2898DeriveBytes(passphrase, salt, 100_000, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(32);
    }
}
