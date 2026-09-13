using Xunit.Abstractions;
using Xunit.Sdk;

namespace Obfy.ScenarioTests;

public enum PlatformWorkload
{
    Maui,
    Blazor
}

/// <summary>
/// Always applies Category=Platform so a lone [PlatformFact] cannot leak into default CI.
/// Optionally skips at discovery when a named SDK/workload is missing.
/// NativeAOT is not skipped here: the ILCompiler pack is restored on first publish,
/// not preinstalled under <c>dotnet/packs</c>.
/// </summary>
[TraitDiscoverer("Obfy.ScenarioTests.PlatformCategoryDiscoverer", "Obfy.ScenarioTests")]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class PlatformFactAttribute : FactAttribute, ITraitAttribute
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

public sealed class PlatformCategoryDiscoverer : ITraitDiscoverer
{
    public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
    {
        yield return new KeyValuePair<string, string>("Category", "Platform");
    }
}
