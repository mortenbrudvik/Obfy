using dnlib.DotNet;
using Microsoft.Extensions.Logging;
using Obfy.Core.Models;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;
using Obfy.Core.Virtualization;

namespace Obfy.Core.Obfuscators.Assembly;

/// <summary>
/// Replaces eligible methods with a stub that calls the imported <c>Obfy.Runtime.Vm.Run</c>
/// interpreter. Selection, encoding, seed XOR, and import are delegated to
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
            var throwaway = new VmMemberTables();
            var eligible = 0;
            foreach (var method in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (selectedSet.Contains(method))
                {
                    eligible++;
                    continue;
                }

                if (VmEncoder.TryEncode(method, EmptyIds, throwaway, out _, out var skipReason))
                {
                    eligible++;
                    continue;
                }

                context.SkippedItems.Add(SkippedItem.UnsupportedMethod(method.FullName, skipReason ?? "unsupported opcode"));
                _logger.LogDebug("Virtualization skipped {Method}: {Reason}", method.FullName, skipReason);
            }

            if (eligible > max)
            {
                var warning = $"Virtualization: maxMethods={max} reached; further eligible methods were skipped.";
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

            var ids = selected.Select((m, i) => (m, i)).ToDictionary(x => x.m, x => x.i);
            var tables = new VmMemberTables();
            var encoded = new List<(MethodDef Method, byte[] Code)>();
            foreach (var method in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!VmEncoder.TryEncode(method, ids, tables, out var code, out var reason))
                {
                    context.SkippedItems.Add(SkippedItem.UnsupportedMethod(method.FullName, reason ?? "unsupported opcode"));
                    _logger.LogDebug(
                        "Virtualization selected {Method} then failed encode: {Reason}",
                        method.FullName,
                        reason);
                    continue;
                }

                encoded.Add((method, code));
            }

            if (encoded.Count == 0)
            {
                const string unused = "Virtualization was enabled but no eligible methods were encoded.";
                context.Warnings.Add(unused);
                _logger.LogWarning("{Warning}", unused);
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
                var returnTypes = encoded.Select(e => e.Method.MethodSig.RetType.ToTypeDefOrRef()).ToList();
                var vmType = VmImporter.Import(
                    context, blob, starts, opMap, xorKey,
                    tables.Methods, tables.Fields, tables.Types, returnTypes);
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
            _logger.LogError(ex, "Virtualization failed");
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
        return VmEncoder.Select(candidates, max).ToHashSet();
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
