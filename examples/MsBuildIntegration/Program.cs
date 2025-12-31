namespace MsBuildIntegration;

public class Program
{
    private const string ApiSecret = "api-secret-key-12345";

    public static void Main(string[] args)
    {
        Console.WriteLine("MSBuild Integration Demo");
        Console.WriteLine("========================");
        Console.WriteLine();
        Console.WriteLine("This project automatically obfuscates on Release build!");
        Console.WriteLine();
        Console.WriteLine($"API Secret (masked): {ApiSecret[..4]}****");
        Console.WriteLine();

        var processor = new DataProcessor();
        processor.ProcessData("Hello, World!");
    }
}

internal class DataProcessor
{
    private readonly string _encryptionKey = "super-secret-encryption-key";

    public void ProcessData(string data)
    {
        Console.WriteLine($"Processing: {data}");
        var encrypted = SimpleEncrypt(data);
        Console.WriteLine($"Encrypted: {encrypted}");
    }

    private string SimpleEncrypt(string input)
    {
        // Simple XOR "encryption" for demo
        var chars = input.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = (char)(chars[i] ^ _encryptionKey[i % _encryptionKey.Length]);
        }
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(new string(chars)));
    }
}
