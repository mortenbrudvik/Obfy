using System.Globalization;
using System.Resources;

namespace SatelliteLib;

public static class Greeter
{
    public static string Hello(string culture)
    {
        var manager = new ResourceManager("SatelliteLib.Strings", typeof(Greeter).Assembly);
        return manager.GetString("Hello", new CultureInfo(culture)) ?? string.Empty;
    }
}
