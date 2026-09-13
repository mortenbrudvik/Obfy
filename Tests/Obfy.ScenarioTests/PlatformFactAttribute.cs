namespace Obfy.ScenarioTests;

/// <summary>
/// Fact that is excluded from default CI via Category=Platform (set on the test class)
/// and optionally skipped when a named SDK/workload is missing at discovery.
/// NativeAOT is not skipped here: the ILCompiler pack is restored on first publish,
/// not preinstalled under <c>dotnet/packs</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class PlatformFactAttribute : FactAttribute
{
    public PlatformFactAttribute(string? requireWorkload = null)
    {
        if (requireWorkload is "maui" && !PlatformWorkloads.HasMaui())
            Skip = "MAUI workload is not installed";
        else if (requireWorkload is "blazor" && !PlatformWorkloads.HasBlazorWasmSdk())
            Skip = "Microsoft.NET.Sdk.BlazorWebAssembly is not in this SDK";
    }
}
