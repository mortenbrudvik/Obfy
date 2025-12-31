namespace BasicConsoleApp;

/// <summary>
/// A simple console application demonstrating Obfy obfuscation.
/// </summary>
public class Program
{
    private const string SecretApiKey = "sk-1234567890abcdef";
    private const string ConnectionString = "Server=localhost;Database=MyApp;User=admin;Password=secret123";

    public static void Main(string[] args)
    {
        Console.WriteLine("=== Basic Console App Demo ===");
        Console.WriteLine();

        var app = new Program();
        app.Run();
    }

    public void Run()
    {
        // These strings will be encrypted by Obfy
        var welcomeMessage = "Welcome to the application!";
        var secretData = "This is sensitive information that should be protected.";

        Console.WriteLine(welcomeMessage);
        Console.WriteLine();

        // Demonstrate various operations
        ProcessUserData("John Doe", "john@example.com");
        PerformCalculation(42, 13);
        DisplaySecretInfo();
    }

    private void ProcessUserData(string name, string email)
    {
        // Method and parameter names will be obfuscated
        var greeting = $"Hello, {name}!";
        var notification = $"Notifications will be sent to: {email}";

        Console.WriteLine(greeting);
        Console.WriteLine(notification);
        Console.WriteLine();
    }

    private void PerformCalculation(int a, int b)
    {
        // Control flow will be obfuscated
        int result;

        if (a > b)
        {
            result = a * b;
            Console.WriteLine($"Multiplication: {a} * {b} = {result}");
        }
        else if (a < b)
        {
            result = a + b;
            Console.WriteLine($"Addition: {a} + {b} = {result}");
        }
        else
        {
            result = a;
            Console.WriteLine($"Equal: {a} = {b} = {result}");
        }

        Console.WriteLine();
    }

    private void DisplaySecretInfo()
    {
        // These constant strings are prime targets for encryption
        Console.WriteLine("Secret Configuration:");
        Console.WriteLine($"  API Key: {MaskString(SecretApiKey)}");
        Console.WriteLine($"  Connection: {MaskString(ConnectionString)}");
        Console.WriteLine();
        Console.WriteLine("In the obfuscated version, these strings are encrypted!");
    }

    private static string MaskString(string value)
    {
        if (value.Length <= 8)
            return new string('*', value.Length);

        return value[..4] + new string('*', value.Length - 8) + value[^4..];
    }
}
