using System;
using System.Security.Cryptography;
using System.Text;

namespace GradeSyncApi.Helpers
{
    public class AesHelper
    {
        public static Tuple<string, string> Encrypt(string plainText, string privateKeyBase64, string? ivBase64 = null)
        {
            var dataBytes = Encoding.ASCII.GetBytes(plainText);

            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Key = Convert.FromBase64String(privateKeyBase64);

                if (ivBase64 is not null)
                {
                    aes.IV = Convert.FromBase64String(ivBase64);
                }

                using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                {
                    var encrypted = PerformCryptography(dataBytes, encryptor);
                    return Tuple.Create(Convert.ToBase64String(encrypted), Convert.ToBase64String(aes.IV));
                }
            }
        }

        public static string Decrypt(string dataBase64, string privateKeyBase64, string iVBase64)
        {
            var dataBytes = Convert.FromBase64String(dataBase64);

            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Key = Convert.FromBase64String(privateKeyBase64);
                aes.IV = Convert.FromBase64String(iVBase64);

                using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                {
                    var decrypted = PerformCryptography(dataBytes, decryptor);
                    return Encoding.ASCII.GetString(decrypted);
                }
            }
        }

        private static byte[] PerformCryptography(byte[] data, ICryptoTransform cryptoTransform)
        {
            using (var ms = new MemoryStream())
            using (var cryptoStream = new CryptoStream(ms, cryptoTransform, CryptoStreamMode.Write))
            {
                cryptoStream.Write(data, 0, data.Length);
                cryptoStream.FlushFinalBlock();

                return ms.ToArray();
            }
        }
    }
}

