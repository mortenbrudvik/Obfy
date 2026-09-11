using Obfy.Core.Models;
using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Tests;

public class NameGeneratorTests
{
    [Fact]
    public void Generate_Sequential_ReturnsSequentialNames()
    {
        // Arrange
        var generator = new NameGenerator();

        // Act
        var name1 = generator.Generate(NamingMode.Sequential);
        var name2 = generator.Generate(NamingMode.Sequential);
        var name3 = generator.Generate(NamingMode.Sequential);

        // Assert
        name1.ShouldBe("a");
        name2.ShouldBe("b");
        name3.ShouldBe("c");
    }

    [Fact]
    public void Reset_ResetsSequentialCounter()
    {
        // Arrange
        var generator = new NameGenerator();
        generator.Generate(NamingMode.Sequential);
        generator.Generate(NamingMode.Sequential);

        // Act
        generator.Reset();
        var name = generator.Generate(NamingMode.Sequential);

        // Assert
        name.ShouldBe("a");
    }

    [Fact]
    public void Generate_Hash_IsDeterministicAcrossRuns()
    {
        // The same original name yields the same hash name on a fresh generator, so a given input
        // assembly produces a reproducible symbol map.
        var name1 = new NameGenerator().Generate("TestMethod", NamingMode.Hash);
        var name2 = new NameGenerator().Generate("TestMethod", NamingMode.Hash);

        name1.ShouldBe(name2);
        name1.ShouldStartWith("_");
    }

    [Fact]
    public void Generate_MakesRepeatedNamesUnique()
    {
        // Two symbols that would otherwise collide (same original name in one run) must be given
        // distinct names; a duplicate in the same scope would produce invalid metadata.
        var generator = new NameGenerator();

        var first = generator.Generate("Dup", NamingMode.Hash);
        var second = generator.Generate("Dup", NamingMode.Hash);

        first.ShouldNotBe(second);
    }

    [Fact]
    public void Generate_ProducesNoCollisions_OverManyNames()
    {
        var generator = new NameGenerator();

        var names = Enumerable.Range(0, 5000)
            .Select(_ => generator.Generate(NamingMode.Unreadable))
            .ToList();

        names.Distinct().Count().ShouldBe(names.Count);
    }

    [Fact]
    public void Generate_Random_ReturnsValidIdentifier()
    {
        // Arrange
        var generator = new NameGenerator();

        // Act
        var name = generator.Generate(NamingMode.Random);

        // Assert
        name.Length.ShouldBeInRange(8, 16);
        char.IsLetter(name[0]).ShouldBeTrue();
    }

    [Fact]
    public void Generate_Unreadable_StartsWithUnderscore()
    {
        // Arrange
        var generator = new NameGenerator();

        // Act
        var name = generator.Generate(NamingMode.Unreadable);

        // Assert
        name.ShouldStartWith("_");
        name.Length.ShouldBeGreaterThan(1);
    }
}
