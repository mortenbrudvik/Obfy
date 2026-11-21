using System.Reflection;
using Obfy.Core.Models;
using Obfy.Core.Models.Solution;
using Obfy.Core.Services;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// <see cref="IObfuscationService"/> has two overloads that carry <see cref="ObfuscationRunOptions"/>. Each has a
/// default body that forwards to the older overload and drops the options, so that a fake or a test double compiles
/// without them. A real implementation must not take that default: a decorator that implements only the older
/// overloads would forward every call without the options, and an incremental-cache hit would hand back an empty
/// symbol map to a caller that asked for one. This is the test that makes that a failure instead of a surprise.
/// </summary>
public class ObfuscationServiceContractTests
{
    private static readonly string[] OptionsOverloads = [nameof(IObfuscationService.ObfuscateAsync), nameof(IObfuscationService.MergeAndObfuscateAsync)];

    [Fact]
    public void EveryImplementationInCore_OverridesTheOptionsOverloads()
    {
        var implementations = typeof(IObfuscationService).Assembly.GetTypes()
            .Where(static t => t is { IsClass: true, IsAbstract: false } && typeof(IObfuscationService).IsAssignableFrom(t))
            .ToList();

        implementations.ShouldContain(typeof(ObfuscationService), "the scan must find the real service");
        Violations(implementations).ShouldBeEmpty();
    }

    [Fact]
    public void ADecoratorThatImplementsOnlyTheOlderOverloads_IsFlagged()
    {
        var violations = Violations([typeof(OlderOverloadsOnlyDecorator)]);

        violations.Count.ShouldBe(OptionsOverloads.Length);
        violations.ShouldContain(v => v.Contains(nameof(IObfuscationService.ObfuscateAsync)) && v.Contains(nameof(OlderOverloadsOnlyDecorator)));
        violations.ShouldContain(v => v.Contains(nameof(IObfuscationService.MergeAndObfuscateAsync)) && v.Contains(nameof(OlderOverloadsOnlyDecorator)));
    }

    [Fact]
    public void ADecoratorThatOverridesTheOptionsOverloads_IsNotFlagged() =>
        Violations([typeof(CompleteDecorator)]).ShouldBeEmpty();

    /// <summary>The implementations that leave an options overload to the interface's default body.</summary>
    private static List<string> Violations(IEnumerable<Type> implementations)
    {
        var violations = new List<string>();
        foreach (var type in implementations)
        {
            var map = type.GetInterfaceMap(typeof(IObfuscationService));
            for (var i = 0; i < map.InterfaceMethods.Length; i++)
            {
                var method = map.InterfaceMethods[i];
                var takesOptions = method.GetParameters().Any(static p => p.ParameterType == typeof(ObfuscationRunOptions));
                if (!takesOptions || !OptionsOverloads.Contains(method.Name))
                    continue;

                // A class that does not provide the member has no target, or the interface's own default body.
                var target = map.TargetMethods[i];
                if (target is null || target.DeclaringType == typeof(IObfuscationService))
                    violations.Add($"{type.Name} does not override {method.Name} with {nameof(ObfuscationRunOptions)}");
            }
        }

        return violations;
    }

    private class OlderOverloadsOnlyDecorator : IObfuscationService
    {
        public Task<ObfuscationResult> ObfuscateAsync(string inputPath, string? outputPath, ObfySettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ObfuscationResult>> ObfuscateBatchAsync(IEnumerable<string> inputPaths, string outputDirectory, ObfySettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ObfuscationResult> ObfuscateSourcesAsync(IReadOnlyList<string> inputPaths, string? outputDirectory, ObfySettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task WriteSymbolMapAsync(Dictionary<string, string> symbolMap, string outputPath) =>
            throw new NotSupportedException();

        public Task<ObfuscationResult> MergeAndObfuscateAsync(IEnumerable<string> inputPaths, string outputPath, ObfySettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ClosedSetResult> ObfuscateClosedSetAsync(IReadOnlyList<ClosedSetInput> inputs, string outputDirectory, ObfySettings settings, bool forcePreservePublic = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CompleteDecorator : OlderOverloadsOnlyDecorator, IObfuscationService
    {
        public Task<ObfuscationResult> ObfuscateAsync(string inputPath, string? outputPath, ObfySettings settings, ObfuscationRunOptions options, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ObfuscationResult> MergeAndObfuscateAsync(IEnumerable<string> inputPaths, string outputPath, ObfySettings settings, ObfuscationRunOptions options, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
