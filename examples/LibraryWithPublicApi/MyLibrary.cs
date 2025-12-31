namespace MyLibrary;

/// <summary>
/// Public API - these names will be preserved when using preservePublicApi: true
/// </summary>
public class Calculator
{
    private readonly string _internalSecret = "internal-key-12345";
    private int _operationCount;

    /// <summary>
    /// Public method - name preserved
    /// </summary>
    public int Add(int a, int b)
    {
        _operationCount++;
        LogOperation("Add", a, b);
        return PerformAddition(a, b);
    }

    /// <summary>
    /// Public method - name preserved
    /// </summary>
    public int Multiply(int a, int b)
    {
        _operationCount++;
        LogOperation("Multiply", a, b);
        return PerformMultiplication(a, b);
    }

    /// <summary>
    /// Public property - name preserved
    /// </summary>
    public int OperationCount => _operationCount;

    // Private methods - these WILL be renamed
    private int PerformAddition(int x, int y)
    {
        return x + y;
    }

    private int PerformMultiplication(int x, int y)
    {
        return x * y;
    }

    private void LogOperation(string operation, int a, int b)
    {
        // This string will be encrypted
        var logMessage = $"[{DateTime.Now}] {operation}: {a}, {b}";
        Console.WriteLine(logMessage);
    }
}

/// <summary>
/// Public interface - names preserved
/// </summary>
public interface IDataProcessor
{
    void Process(string data);
    string GetResult();
}

/// <summary>
/// Public class implementing interface - public members preserved
/// </summary>
public class DataProcessor : IDataProcessor
{
    private string _result = string.Empty;
    private readonly string _encryptionKey = "super-secret-key";

    public void Process(string data)
    {
        // Private implementation details will be obfuscated
        var processed = TransformData(data);
        _result = EncryptResult(processed);
    }

    public string GetResult()
    {
        return _result;
    }

    // Private methods - will be renamed
    private string TransformData(string input)
    {
        return input.ToUpperInvariant();
    }

    private string EncryptResult(string data)
    {
        // Simplified "encryption" for demo
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(data));
    }
}

// Internal class - will be fully obfuscated
internal class InternalHelper
{
    private const string HelperSecret = "helper-internal-secret";

    public static string FormatMessage(string message)
    {
        return $"[Helper] {message}";
    }
}
