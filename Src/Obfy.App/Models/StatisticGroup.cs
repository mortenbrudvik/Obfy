namespace Obfy.App.Models;

/// <summary>One counter in a <see cref="StatisticGroup"/>. A counter that is zero is never sent.</summary>
public sealed record StatisticChip(string Label, int Value);

/// <summary>A titled row of counters (<c>Renamed</c>, <c>Encrypted</c>, <c>Protection</c>, <c>Metadata</c>, <c>Total</c>). A group with no counters is not sent.</summary>
public sealed record StatisticGroup(string Name, IReadOnlyList<StatisticChip> Chips);
