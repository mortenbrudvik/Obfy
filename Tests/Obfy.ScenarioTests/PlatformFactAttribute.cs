using Xunit.Abstractions;
using Xunit.Sdk;

namespace Obfy.ScenarioTests;

/// <summary>
/// Always applies Category=Platform so a lone [PlatformFact] cannot leak into default CI.
/// Optionally skips at discovery when a named SDK/workload is missing.
/// NativeAOT is not skipped here: the ILCompiler pack is restored on first publish.
/// </summary>
[TraitDiscoverer("Obfy.ScenarioTests.PlatformCategoryDiscoverer", "Obfy.ScenarioTests")]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class PlatformFactAttribute : FactAttribute, ITraitAttribute
{
    public PlatformFactAttribute(string? requireWorkload = null)
    {
        if (requireWorkload is "maui" && !PlatformWorkloads.HasMaui())
            Skip = "MAUI workload is not installed";
        else if (requireWorkload is "blazor" && !PlatformWorkloads.HasBlazorWasmSdk())
            Skip = "Microsoft.NET.Sdk.BlazorWebAssembly is not in this SDK";
    }
}

public sealed class PlatformCategoryDiscoverer : ITraitDiscoverer
{
    public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
    {
        yield return new KeyValuePair<string, string>("Category", "Platform");
    }
}
