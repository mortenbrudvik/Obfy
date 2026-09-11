using System.Security.Cryptography;
using Obfy.Core.Models;
using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Tests;

public class EncryptionHelperTests
{
    [Theory]
    [InlineData(EncryptionAlgorithm.Xor)]
    [InlineData(EncryptionAlgorithm.Aes256)]
    public void EncryptDecrypt_RoundTrip_ReturnsOriginal(EncryptionAlgorithm algorithm)
    {
        // Arrange
        var original = "Hello, World! This is a test string.";
        var key = EncryptionHelper.GenerateKey(algorithm);

        // Act
        var encrypted = EncryptionHelper.Encrypt(original, key, algorithm);
        var decrypted = EncryptionHelper.Decrypt(encrypted, key, algorithm);

        // Assert
        decrypted.ShouldBe(original);
    }

    [Theory]
    [InlineData(EncryptionAlgorithm.Xor)]
    [InlineData(EncryptionAlgorithm.Aes256)]
    public void EncryptToBase64_DecryptFromBase64_RoundTrip(EncryptionAlgorithm algorithm)
    {
        // Arrange
        var original = "Test string for base64 encoding";
        var key = EncryptionHelper.GenerateKey(algorithm);

        // Act
        var base64 = EncryptionHelper.EncryptToBase64(original, key, algorithm);
        var decrypted = EncryptionHelper.DecryptFromBase64(base64, key, algorithm);

        // Assert
        decrypted.ShouldBe(original);
    }

    [Fact]
    public void GenerateKey_Xor_Returns32Bytes()
    {
        // Act
        var key = EncryptionHelper.GenerateKey(EncryptionAlgorithm.Xor);

        // Assert
        key.Length.ShouldBe(32);
    }

    [Fact]
    public void GenerateKey_Aes256_Returns32Bytes()
    {
        // Act
        var key = EncryptionHelper.GenerateKey(EncryptionAlgorithm.Aes256);

        // Assert
        key.Length.ShouldBe(32);
    }

    [Fact]
    public void Encrypt_DifferentKeys_ProducesDifferentOutput()
    {
        // Arrange
        var original = "Test string";
        var key1 = EncryptionHelper.GenerateKey(EncryptionAlgorithm.Aes256);
        var key2 = EncryptionHelper.GenerateKey(EncryptionAlgorithm.Aes256);

        // Act
        var encrypted1 = EncryptionHelper.EncryptToBase64(original, key1, EncryptionAlgorithm.Aes256);
        var encrypted2 = EncryptionHelper.EncryptToBase64(original, key2, EncryptionAlgorithm.Aes256);

        // Assert
        encrypted1.ShouldNotBe(encrypted2);
    }

    [Theory]
    [InlineData(EncryptionAlgorithm.Xor)]
    [InlineData(EncryptionAlgorithm.Aes256)]
    public void EncryptBytes_RoundTrip_ReturnsOriginal(EncryptionAlgorithm algorithm)
    {
        var original = new byte[] { 0, 1, 2, 255, 16, 32 };
        var key = EncryptionHelper.GenerateKey(algorithm);

        var encrypted = EncryptionHelper.EncryptBytes(original, key, algorithm);
        var decrypted = EncryptionHelper.DecryptBytes(encrypted, key, algorithm);

        decrypted.ShouldBe(original);
    }

    [Theory]
    [InlineData(EncryptionAlgorithm.Xor)]
    [InlineData(EncryptionAlgorithm.Aes256)]
    public void Encrypt_RoundTrips_EmptyString(EncryptionAlgorithm algorithm)
    {
        var key = EncryptionHelper.GenerateKey(algorithm);
        var encrypted = EncryptionHelper.EncryptToBase64(string.Empty, key, algorithm);

        EncryptionHelper.DecryptFromBase64(encrypted, key, algorithm).ShouldBe(string.Empty);
    }

    [Theory]
    [InlineData(EncryptionAlgorithm.Xor)]
    [InlineData(EncryptionAlgorithm.Aes256)]
    public void Encrypt_RoundTrips_UnicodeString(EncryptionAlgorithm algorithm)
    {
        const string original = "héllo • 世界 • 🚀 • Ω";
        var key = EncryptionHelper.GenerateKey(algorithm);

        var encrypted = EncryptionHelper.EncryptToBase64(original, key, algorithm);

        EncryptionHelper.DecryptFromBase64(encrypted, key, algorithm).ShouldBe(original);
    }

    [Fact]
    public void DecryptAes_WithWrongKey_DoesNotReturnOriginal()
    {
        const string original = "SensitivePayload";
        var key = EncryptionHelper.GenerateKey(EncryptionAlgorithm.Aes256);
        var wrongKey = EncryptionHelper.GenerateKey(EncryptionAlgorithm.Aes256);

        var encrypted = EncryptionHelper.EncryptToBase64(original, key, EncryptionAlgorithm.Aes256);

        // AES with PKCS7 padding almost always throws on a wrong key (bad padding); on the rare
        // occasion it doesn't, the plaintext must still not match. Either outcome is acceptable —
        // what must never happen is silently recovering the original with the wrong key.
        try
        {
            var decrypted = EncryptionHelper.DecryptFromBase64(encrypted, wrongKey, EncryptionAlgorithm.Aes256);
            decrypted.ShouldNotBe(original);
        }
        catch (CryptographicException)
        {
            // Expected: bad padding / invalid block.
        }
    }

    [Fact]
    public void DecryptAes_WithTamperedCiphertext_DoesNotReturnOriginal()
    {
        const string original = "IntegrityMatters!";
        var key = EncryptionHelper.GenerateKey(EncryptionAlgorithm.Aes256);

        var encryptedBytes = EncryptionHelper.Encrypt(original, key, EncryptionAlgorithm.Aes256);
        encryptedBytes[^1] ^= 0xFF; // flip bits in the final ciphertext block

        try
        {
            var decrypted = EncryptionHelper.Decrypt(encryptedBytes, key, EncryptionAlgorithm.Aes256);
            decrypted.ShouldNotBe(original);
        }
        catch (CryptographicException)
        {
            // Expected: tampering breaks PKCS7 padding.
        }
    }
}
