// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using GradeSyncApi.Helpers;
using System.Security.Cryptography;

namespace GradeSyncTest
{
    public class Helpers_Test
    {
        [Fact]
        public void CanEncryptAndDecryptText()
        {
            byte[] key = RandomNumberGenerator.GetBytes(32);

            // represents key that would be stored as env var
            string privateKeyBase64 = Convert.ToBase64String(key);
            Console.WriteLine($"Private test key: {privateKeyBase64}");
            string rawText = "https://some-base-url.com/oauth/test";

            var encrypted = AesHelper.Encrypt(rawText, privateKeyBase64);
            var decryptedText = AesHelper.Decrypt(encrypted.Item1, privateKeyBase64, encrypted.Item2);

            Assert.Equal(rawText, decryptedText);
        }
    }
}

