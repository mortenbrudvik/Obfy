using System.Reflection;
using Obfy.Core.Models;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// Guards against drift between the individual counters, <see cref="ObfuscationStatistics.Merge"/>,
/// and <see cref="ObfuscationStatistics.TotalTransformations"/>. Adding a counter that is not wired
/// into both will fail these reflection-based tests.
/// </summary>
public class ObfuscationStatisticsTests
{
    private static List<PropertyInfo> CounterProperties() =>
        typeof(ObfuscationStatistics).GetProperties()
            .Where(p => p.PropertyType == typeof(int) && p.CanWrite)
            .ToList();

    [Fact]
    public void TotalTransformations_IncludesEveryCounter()
    {
        var stats = new ObfuscationStatistics();
        var counters = CounterProperties();

        var expected = 0;
        for (var i = 0; i < counters.Count; i++)
        {
            counters[i].SetValue(stats, i + 1);
            expected += i + 1;
        }

        stats.TotalTransformations.ShouldBe(expected);
    }

    [Fact]
    public void Merge_SumsEveryCounter()
    {
        var target = new ObfuscationStatistics();
        var other = new ObfuscationStatistics();

        foreach (var counter in CounterProperties())
        {
            counter.SetValue(target, 2);
            counter.SetValue(other, 3);
        }

        target.Merge(other);

        foreach (var counter in CounterProperties())
        {
            ((int)counter.GetValue(target)!).ShouldBe(5, $"counter '{counter.Name}' was not merged");
        }
    }
}
