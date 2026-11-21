using dnlib.DotNet;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;
using Obfy.Core.Virtualization;

namespace Obfy.Core.Obfuscators.Assembly;

/// <summary>
/// Replaces eligible instance and static methods with a stub that calls the imported
/// <c>Obfy.Runtime.Vm.Run</c> interpreter. Skips EH, generic methods/types/calls, byref,
/// custom structs, <c>Nullable{T}</c>, and constructors. Gated off NativeAOT / Unity IL2CPP /
/// Blazor WASM. Selection, encoding, seed XOR, and import are delegated to
/// <see cref="VmEncoder"/>, <see cref="VmSeed"/>, and <see cref="VmImporter"/>.
/// </summary>
public class VirtualizationObfuscator : IObfuscator
{
    private static readonly IReadOnlyDictionary<MethodDef, int> EmptyIds =
        new Dictionary<MethodDef, int>();

    private readonly ILogger<VirtualizationObfuscator> _logger;

    public VirtualizationObfuscator(ILogger<VirtualizationObfuscator> logger) => _logger = logger;

    public string Name => "Virtualization";
    public int Priority => (int)ObfuscationPhase.Virtualization;
    public bool SupportsTargetType(TargetType targetType) => targetType == TargetType.Assembly;
    public bool IsEnabled(ObfySettings settings) => settings.Virtualization.Enabled;

    public Task<ObfuscationResult> ObfuscateAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        var module = context.RequireModule();
        var stats = new ObfuscationStatistics();
        var max = context.Settings.Virtualization.MaxMethods;
        if (max is < 1 or > 256)
        {
            return Task.FromResult(ObfuscationResult.Failed(
                $"Virtualization.MaxMethods must be 1-256 (got {max})."));
        }

        try
        {
            var candidates = CollectCandidates(module, context);
            var selected = VmEncoder.Select(candidates, max);
            var selectedSet = selected.ToHashSet();
            var eligible = 0;
            var overflowNames = new List<string>();
            foreach (var method in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (selectedSet.Contains(method))
                {
                    eligible++;
                    continue;
                }

                // A fresh table per probe: a shared one fills up across a large assembly's candidates
                // and would then misreport perfectly encodable methods as out of range.
                if (VmEncoder.TryEncode(method, EmptyIds, new VmMemberTables(), out _, out var skipReason))
                {
                    eligible++;
                    if (overflowNames.Count < 10)
                        overflowNames.Add(method.FullName);
                    continue;
                }

                if (skipReason is VmSkipReasons.NoBody or VmSkipReasons.Constructor)
                {
                    _logger.LogDebug("Virtualization skipped {Method}: {Reason}", method.FullName, skipReason);
                    continue;
                }

                context.SkippedItems.Add(SkippedItem.UnsupportedMethod(
                    method.FullName, skipReason ?? VmSkipReasons.UnsupportedOpcode));
                _logger.LogDebug("Virtualization skipped {Method}: {Reason}", method.FullName, skipReason);
            }

            if (eligible > max)
            {
                var extra = overflowNames.Count == 0
                    ? string.Empty
                    : " Not virtualized (first 10): " + string.Join(", ", overflowNames) + ".";
                var warning = $"Virtualization: maxMethods={max} reached; further eligible methods were skipped.{extra}";
                context.Warnings.Add(warning);
                _logger.LogWarning("{Warning}", warning);
            }

            if (selected.Count == 0)
            {
                const string unused = "Virtualization was enabled but no eligible methods were encoded.";
                context.Warnings.Add(unused);
                _logger.LogWarning("{Warning}", unused);
                return Task.FromResult(ObfuscationResult.Successful(stats));
            }

            var (encoded, tables) = EncodeFinal(selected, (method, reason) =>
            {
                context.SkippedItems.Add(SkippedItem.UnsupportedMethod(
                    method.FullName, reason ?? VmSkipReasons.UnsupportedOpcode));
                _logger.LogDebug("Virtualization dropped a selected method at final encode: {Reason}", reason);
            }, cancellationToken);

            if (encoded.Count == 0)
            {
                const string none = "Virtualization was enabled but no eligible methods were encoded.";
                context.Warnings.Add(none);
                _logger.LogWarning("{Warning}", none);
                return Task.FromResult(ObfuscationResult.Successful(stats));
            }

            var blob = Concat(encoded);
            var starts = PrefixSums(encoded);
            var seed = VmSeed.Compute(context);
            var opMap = VmSeed.CreateOpMap(seed);
            var xorKey = VmSeed.CreateXorKey(seed);
            VmSeed.Apply(blob, opMap, xorKey);

            MethodDef run;
            try
            {
                // Strip modifiers like the parameters below: init-only setters return void modreq(IsExternalInit),
                // and a custom modifier does not belong in the ldtoken type signature.
                var returnTypes = encoded
                    .Select(e => (e.Method.MethodSig.RetType.RemovePinnedAndModifiers() ?? e.Method.MethodSig.RetType)
                        .ToTypeDefOrRef())
                    .ToList();
                var argumentCounts = new int[encoded.Count];
                var argumentTypes = new List<ITypeDefOrRef>();
                for (var i = 0; i < encoded.Count; i++)
                {
                    var parameters = encoded[i].Method.Parameters;
                    argumentCounts[i] = parameters.Count;
                    foreach (var parameter in parameters)
                    {
                        var signature = parameter.Type.RemovePinnedAndModifiers() ?? parameter.Type;
                        argumentTypes.Add(signature.ToTypeDefOrRef());
                    }
                }

                var vmType = VmImporter.Import(
                    context, blob, starts, opMap, xorKey,
                    tables.Methods, tables.Fields, tables.Types, returnTypes,
                    argumentTypes, argumentCounts);
                run = vmType.FindMethod("Run")
                    ?? throw new InvalidOperationException("Virtualization failed: Run missing.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Task.FromResult(ObfuscationResult.Failed($"Virtualization failed: {ex.Message}", ex));
            }

            for (var i = 0; i < encoded.Count; i++)
                VmImporter.WriteStub(encoded[i].Method, run, i);

            stats.ProtectionsApplied = encoded.Count;
            _logger.LogInformation("Virtualized {Count} methods", encoded.Count);
            return Task.FromResult(ObfuscationResult.Successful(stats));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Virtualization failed ({ExceptionType})", ex.GetType().Name);
            return Task.FromResult(ObfuscationResult.Failed($"Virtualization failed: {ex.Message}", ex));
        }
    }

    internal static List<MethodDef> CollectCandidates(ModuleDef module, PipelineContext context)
    {
        var candidates = new List<MethodDef>();
        foreach (var type in module.GetTypes())
        {
            if (ObfuscatorHelpers.IsRuntimeHelper(type) || type.IsGlobalModuleType)
                continue;
            if (!ObfuscationAttributeRules.AllowType(type, context.Settings, ObfuscationFeature.Virtualization, context.Warnings))
                continue;
            foreach (var method in type.Methods)
            {
                if (!ObfuscationAttributeRules.AllowMethod(method, context.Settings, ObfuscationFeature.Virtualization, context.Warnings))
                    continue;
                candidates.Add(method);
            }
        }

        return candidates;
    }

    internal static HashSet<MethodDef> SelectSet(PipelineContext context)
    {
        var max = context.Settings.Virtualization.MaxMethods;
        var candidates = CollectCandidates(context.RequireModule(), context);
        var (encoded, _) = EncodeFinal(VmEncoder.Select(candidates, max), onDropped: null, CancellationToken.None);
        return encoded.Select(e => e.Method).ToHashSet();
    }

    /// <summary>
    /// Encodes the selected methods with their final ids. <see cref="VmEncoder.Select"/> probes
    /// without ids (an intra-VM call is 3 bytes); the real encode uses CallVm (4 bytes), so a method
    /// near a u16 limit can be admitted and then fail. Such a method is dropped and the rest
    /// re-encoded, because their ids and the member tables depend on the final set.
    /// </summary>
    private static (List<(MethodDef Method, byte[] Code)> Encoded, VmMemberTables Tables) EncodeFinal(
        IReadOnlyList<MethodDef> selected,
        Action<MethodDef, string?>? onDropped,
        CancellationToken cancellationToken)
    {
        var chosen = selected.ToList();
        while (true)
        {
            var ids = chosen.Select((m, i) => (m, i)).ToDictionary(x => x.m, x => x.i);
            var tables = new VmMemberTables();
            var encoded = new List<(MethodDef Method, byte[] Code)>();
            MethodDef? failed = null;
            foreach (var method in chosen)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!VmEncoder.TryEncode(method, ids, tables, out var code, out var reason))
                {
                    onDropped?.Invoke(method, reason);
                    failed = method;
                    break;
                }

                encoded.Add((method, code));
            }

            if (failed is null)
                return (encoded, tables);
            chosen.Remove(failed);
        }
    }

    private static byte[] Concat(List<(MethodDef Method, byte[] Code)> encoded)
    {
        var size = 0;
        foreach (var item in encoded)
            size += item.Code.Length;
        var blob = new byte[size];
        var offset = 0;
        foreach (var item in encoded)
        {
            Buffer.BlockCopy(item.Code, 0, blob, offset, item.Code.Length);
            offset += item.Code.Length;
        }

        return blob;
    }

    private static int[] PrefixSums(List<(MethodDef Method, byte[] Code)> encoded)
    {
        var starts = new int[encoded.Count];
        var offset = 0;
        for (var i = 0; i < encoded.Count; i++)
        {
            starts[i] = offset;
            offset += encoded[i].Code.Length;
        }

        return starts;
    }
}
