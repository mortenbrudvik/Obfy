using System.Security.Cryptography;
using System.Text;
using Obfy.Core.Models;

namespace Obfy.Core.Utilities;

/// <summary>
/// Provides encryption utilities for string obfuscation.
/// </summary>
public static class EncryptionHelper
{
    /// <summary>
    /// Encrypts a string using the specified algorithm.
    /// </summary>
    /// <param name="plainText">The text to encrypt.</param>
    /// <param name="key">The encryption key.</param>
    /// <param name="algorithm">The encryption algorithm.</param>
    /// <returns>The encrypted data as a byte array.</returns>
    public static byte[] Encrypt(string plainText, byte[] key, EncryptionAlgorithm algorithm)
    {
        return algorithm switch
        {
            EncryptionAlgorithm.Xor => EncryptXor(plainText, key),
            EncryptionAlgorithm.Aes256 => EncryptAes(plainText, key),
            _ => throw new ArgumentException($"Unknown algorithm: {algorithm}")
        };
    }

    /// <summary>
    /// Decrypts data using the specified algorithm.
    /// </summary>
    /// <param name="encryptedData">The encrypted data.</param>
    /// <param name="key">The encryption key.</param>
    /// <param name="algorithm">The encryption algorithm.</param>
    /// <returns>The decrypted string.</returns>
    public static string Decrypt(byte[] encryptedData, byte[] key, EncryptionAlgorithm algorithm)
    {
        return algorithm switch
        {
            EncryptionAlgorithm.Xor => DecryptXor(encryptedData, key),
            EncryptionAlgorithm.Aes256 => DecryptAes(encryptedData, key),
            _ => throw new ArgumentException($"Unknown algorithm: {algorithm}")
        };
    }

    /// <summary>
    /// Generates a random encryption key.
    /// </summary>
    /// <param name="algorithm">The algorithm to generate the key for.</param>
    /// <returns>A random key of appropriate length.</returns>
    public static byte[] GenerateKey(EncryptionAlgorithm algorithm)
    {
        var keySize = algorithm switch
        {
            EncryptionAlgorithm.Xor => 32,
            EncryptionAlgorithm.Aes256 => 32, // 256 bits
            _ => 32
        };

        return RandomNumberGenerator.GetBytes(keySize);
    }

    /// <summary>
    /// Encrypts data to a Base64 string for embedding in code.
    /// </summary>
    public static string EncryptToBase64(string plainText, byte[] key, EncryptionAlgorithm algorithm)
    {
        var encrypted = Encrypt(plainText, key, algorithm);
        return Convert.ToBase64String(encrypted);
    }

    /// <summary>
    /// Decrypts a Base64 string.
    /// </summary>
    public static string DecryptFromBase64(string base64Data, byte[] key, EncryptionAlgorithm algorithm)
    {
        var encrypted = Convert.FromBase64String(base64Data);
        return Decrypt(encrypted, key, algorithm);
    }

    private static byte[] EncryptXor(string plainText, byte[] key)
    {
        var data = Encoding.UTF8.GetBytes(plainText);
        var result = new byte[data.Length];

        for (var i = 0; i < data.Length; i++)
        {
            result[i] = (byte)(data[i] ^ key[i % key.Length]);
        }

        return result;
    }

    private static string DecryptXor(byte[] encryptedData, byte[] key)
    {
        var result = new byte[encryptedData.Length];

        for (var i = 0; i < encryptedData.Length; i++)
        {
            result[i] = (byte)(encryptedData[i] ^ key[i % key.Length]);
        }

        return Encoding.UTF8.GetString(result);
    }

    private static byte[] EncryptAes(string plainText, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Prepend IV to encrypted data
        var result = new byte[aes.IV.Length + encrypted.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encrypted, 0, result, aes.IV.Length, encrypted.Length);

        return result;
    }

    private static string DecryptAes(byte[] encryptedData, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;

        // Extract IV from beginning of data
        var iv = new byte[aes.BlockSize / 8];
        Buffer.BlockCopy(encryptedData, 0, iv, 0, iv.Length);
        aes.IV = iv;

        // Extract encrypted content
        var encrypted = new byte[encryptedData.Length - iv.Length];
        Buffer.BlockCopy(encryptedData, iv.Length, encrypted, 0, encrypted.Length);

        using var decryptor = aes.CreateDecryptor();
        var decrypted = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);

        return Encoding.UTF8.GetString(decrypted);
    }
}
