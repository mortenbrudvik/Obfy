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
}
