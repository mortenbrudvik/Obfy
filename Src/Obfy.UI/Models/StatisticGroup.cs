namespace Obfy.UI.Models;

public sealed record StatisticChip(string Label, int Value);

public sealed class StatisticGroup
{
    public StatisticGroup(string name, IReadOnlyList<StatisticChip> chips)
    {
        Name = name;
        Chips = chips;
    }

    public string Name { get; }

    public IReadOnlyList<StatisticChip> Chips { get; }
}
