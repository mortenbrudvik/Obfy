using System.IO;
using Moq;
using Obfy.App.Services;
using Obfy.App.Sessions;
using Obfy.UI.Services;
using Shouldly;

namespace Obfy.UI.Tests.Services;

public class ApplicationHostServiceTests
{
    [Fact]
    public async Task StopAsync_PersistsThePreferencesTheSessionHolds()
    {
        var folder = Path.Combine(Path.GetTempPath(), "obfy-host-stop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var settings = new Mock<ISettingsService>();
            settings.SetupAllProperties();
            var session = new InputSession(settings.Object, preferenceDelay: TimeSpan.FromSeconds(5));
            session.SetOutputFolder(folder);
            session.SetWriteSymbolMap(true);

            var provider = new Mock<IServiceProvider>();
            provider.Setup(p => p.GetService(typeof(InputSession))).Returns(session);

            // The debounce is far from firing: only the flush in StopAsync can have written before the process ends.
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await new ApplicationHostService(provider.Object).StopAsync(CancellationToken.None);
            stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3));

            settings.Object.LastOutputDirectory.ShouldBe(folder);
            settings.Object.GenerateSymbolMap.ShouldBeTrue();
            settings.Verify(s => s.SavePreferencesAsync(), Times.Once());
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task StopAsync_WithoutASession_Completes()
    {
        var provider = new Mock<IServiceProvider>();

        await new ApplicationHostService(provider.Object).StopAsync(CancellationToken.None);
    }
}
