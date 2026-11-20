using System.Security.Cryptography;
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

    /// <summary>
    /// Encrypts binary data using the specified algorithm.
    /// </summary>
    /// <param name="data">The data to encrypt.</param>
    /// <param name="key">The encryption key.</param>
    /// <param name="algorithm">The encryption algorithm.</param>
    /// <returns>The encrypted data as a byte array.</returns>
    public static byte[] EncryptBytes(byte[] data, byte[] key, EncryptionAlgorithm algorithm)
    {
        return algorithm switch
        {
            EncryptionAlgorithm.Xor => EncryptBytesXor(data, key),
            EncryptionAlgorithm.Aes256 => EncryptBytesAes(data, key),
            _ => throw new ArgumentException($"Unknown algorithm: {algorithm}")
        };
    }

    /// <summary>
    /// Decrypts binary data using the specified algorithm.
    /// </summary>
    /// <param name="encryptedData">The encrypted data.</param>
    /// <param name="key">The encryption key.</param>
    /// <param name="algorithm">The encryption algorithm.</param>
    /// <returns>The decrypted data as a byte array.</returns>
    public static byte[] DecryptBytes(byte[] encryptedData, byte[] key, EncryptionAlgorithm algorithm)
    {
        return algorithm switch
        {
            EncryptionAlgorithm.Xor => DecryptBytesXor(encryptedData, key),
            EncryptionAlgorithm.Aes256 => DecryptBytesAes(encryptedData, key),
            _ => throw new ArgumentException($"Unknown algorithm: {algorithm}")
        };
    }

    private static byte[] EncryptXor(string plainText, byte[] key)
    {
        var data = EncodeString(plainText);
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

        return DecodeString(result);
    }

    private static byte[] EncryptAes(string plainText, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = EncodeString(plainText);
        var encrypted = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Prepend IV to encrypted data
        var result = new byte[aes.IV.Length + encrypted.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encrypted, 0, result, aes.IV.Length, encrypted.Length);

        return result;
    }

    private static string DecryptAes(byte[] encryptedData, byte[] key)
    {
        var encrypted = ReadAesPayload(encryptedData);
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = encrypted.Iv;

        using var decryptor = aes.CreateDecryptor();
        var decrypted = decryptor.TransformFinalBlock(encrypted.Cipher, 0, encrypted.Cipher.Length);

        return DecodeString(decrypted);
    }

    private static byte[] EncryptBytesXor(byte[] data, byte[] key)
    {
        var result = new byte[data.Length];
        for (var i = 0; i < data.Length; i++)
        {
            result[i] = (byte)(data[i] ^ key[i % key.Length]);
        }
        return result;
    }

    private static byte[] DecryptBytesXor(byte[] encryptedData, byte[] key)
    {
        // XOR is symmetric
        return EncryptBytesXor(encryptedData, key);
    }

    private static byte[] EncryptBytesAes(byte[] data, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var encrypted = encryptor.TransformFinalBlock(data, 0, data.Length);

        // Prepend IV to encrypted data
        var result = new byte[aes.IV.Length + encrypted.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encrypted, 0, result, aes.IV.Length, encrypted.Length);

        return result;
    }

    private static byte[] DecryptBytesAes(byte[] encryptedData, byte[] key)
    {
        var encrypted = ReadAesPayload(encryptedData);
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = encrypted.Iv;

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(encrypted.Cipher, 0, encrypted.Cipher.Length);
    }

    /// <summary>
    /// Raw UTF-16 code units. <see cref="Encoding.UTF8"/> and <see cref="Encoding.Unicode"/>
    /// both replace a lone surrogate with U+FFFD.
    /// </summary>
    private static byte[] EncodeString(string plainText)
    {
        var bytes = new byte[plainText.Length * 2];
        for (var i = 0; i < plainText.Length; i++)
        {
            bytes[i * 2] = (byte)plainText[i];
            bytes[(i * 2) + 1] = (byte)(plainText[i] >> 8);
        }

        return bytes;
    }

    private static string DecodeString(byte[] data)
    {
        var chars = new char[data.Length / 2];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = (char)(data[i * 2] | (data[(i * 2) + 1] << 8));
        return new string(chars);
    }

    private readonly record struct AesPayload(byte[] Iv, byte[] Cipher);

    private static AesPayload ReadAesPayload(byte[] encryptedData)
    {
        ArgumentNullException.ThrowIfNull(encryptedData);
        const int ivLength = 16;
        if (encryptedData.Length < ivLength)
            throw new ArgumentException("AES payload is shorter than the 16-byte IV.", nameof(encryptedData));

        var iv = new byte[ivLength];
        Buffer.BlockCopy(encryptedData, 0, iv, 0, ivLength);
        var cipher = new byte[encryptedData.Length - ivLength];
        Buffer.BlockCopy(encryptedData, ivLength, cipher, 0, cipher.Length);
        return new AesPayload(iv, cipher);
    }
}
