using System.Reflection;
using Mediator;

namespace Obfy.App.Features.App;

/// <summary>Asks for facts about the running application (<c>app.info</c>).</summary>
public record AppInfoRequest : IRequest<AppInfo>;

/// <summary>What <c>app.info</c> answers with.</summary>
public record AppInfo(string Version);

/// <summary>Answers with the informational version of the Obfy engine, without the build metadata after <c>+</c>.</summary>
public class AppInfoRequestHandler : IRequestHandler<AppInfoRequest, AppInfo>
{
    // Obfy.Core carries the product version; this assembly is only a layer on top of it.
    private static readonly Lazy<string> Version = new(ReadVersion);

    public ValueTask<AppInfo> Handle(AppInfoRequest request, CancellationToken cancellationToken) =>
        new(new AppInfo(Version.Value));

    private static string ReadVersion()
    {
        var assembly = typeof(Obfy.Core.Models.ObfySettings).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        var version = assembly.GetName().Version;
        return version is null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
