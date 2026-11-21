using System;
using System.Globalization;
using System.IO;
using System.Resources;

namespace RichConsole
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.WriteLine("Hello from the rich app");
            Console.WriteLine(SiblingLib.Greeter.Greet("world"));

            var assembly = typeof(Program).Assembly;
            using (var stream = assembly.GetManifestResourceStream("RichConsole.data.txt"))
            using (var reader = new StreamReader(stream))
                Console.WriteLine("resource:" + reader.ReadToEnd().Trim());

            var resources = new ResourceManager("RichConsole.Strings", assembly);
            Console.WriteLine("resx:" + resources.GetString("Welcome", CultureInfo.InvariantCulture));

            long total = 0;
            for (int i = 0; i < 1000; i++)
                total += i * 3 + 12345;
            double d = total * 3.14159 + 2.71828;
            float f = 1.5f * total + 0.25f;
            switch (args.Length)
            {
                case 0: total += 1; break;
                case 1: total += 2; break;
                default: total += 4; break;
            }

            Console.WriteLine(total + ":" + d + ":" + f + ":" + 9999999999L);
            Console.WriteLine(Join("virtual") + Compute(4, 5));
            return 0;
        }

        public static int Compute(int a, int b)
        {
            return (a * 3 + b) - 7;
        }

        public static string Join(string text)
        {
            return text + "!";
        }
    }
}
