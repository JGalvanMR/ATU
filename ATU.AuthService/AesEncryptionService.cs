using ATU.Shared.Models;
using System.Security.Cryptography;
using System.Text;

namespace ATU.AuthService
{
    // En Program.cs o archivo separado
    class AesEncryptionService : IEncryptionService
    {
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

        public string Encrypt(string plaintext)
        {
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.GenerateIV();
            var iv = aes.IV;

            using var encryptor = aes.CreateEncryptor();
            var plainBytes = Encoding.UTF8.GetBytes(plaintext);
            var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            var result = new byte[iv.Length + cipherBytes.Length];
            Buffer.BlockCopy(iv, 0, result, 0, iv.Length);
            Buffer.BlockCopy(cipherBytes, 0, result, iv.Length, cipherBytes.Length);

            return Convert.ToBase64String(result);
        }

        public string Decrypt(string ciphertext)
        {
            var data = Convert.FromBase64String(ciphertext);
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV = data.Take(16).ToArray();

            using var decryptor = aes.CreateDecryptor();
            var decrypted = decryptor.TransformFinalBlock(data, 16, data.Length - 16);
            return Encoding.UTF8.GetString(decrypted);
        }
    }
}
