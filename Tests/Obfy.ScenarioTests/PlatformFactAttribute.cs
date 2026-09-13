namespace Obfy.ScenarioTests;

public enum PlatformWorkload
{
    Maui,
    Blazor
}

/// <summary>
/// Fact excluded from default CI via Category=Platform (on this attribute and typically the class)
/// and optionally skipped when a named SDK/workload is missing at discovery.
/// NativeAOT is not skipped here: the ILCompiler pack is restored on first publish,
/// not preinstalled under <c>dotnet/packs</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
[Trait("Category", "Platform")]
public sealed class PlatformFactAttribute : FactAttribute
{
    public PlatformFactAttribute()
    {
    }

    public PlatformFactAttribute(PlatformWorkload requireWorkload)
    {
        switch (requireWorkload)
        {
            case PlatformWorkload.Maui when !PlatformWorkloads.HasMaui():
                Skip = "MAUI workload is not installed";
                break;
            case PlatformWorkload.Blazor when !PlatformWorkloads.HasBlazorWasmSdk():
                Skip = "Microsoft.NET.Sdk.BlazorWebAssembly is not in this SDK";
                break;
        }
    }
}
