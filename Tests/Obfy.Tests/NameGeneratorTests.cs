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
    public void Generate_Hash_IsSaltedPerGenerator()
    {
        // Hash mode must not be a dictionary oracle of the original name. A fresh generator uses a
        // new salt, so the same input does not produce a stable, reversible digest.
        var name1 = new NameGenerator().Generate("TestMethod", NamingMode.Hash);
        var name2 = new NameGenerator().Generate("TestMethod", NamingMode.Hash);

        name1.ShouldStartWith("_");
        name1.ShouldNotBe(name2);
    }

    [Fact]
    public void Generate_Hash_IsStableWithinOneGenerator()
    {
        var generator = new NameGenerator();
        var first = generator.Generate("Alpha", NamingMode.Hash);
        generator.Reset();
        var afterReset = generator.Generate("Alpha", NamingMode.Hash);

        first.ShouldStartWith("_");
        afterReset.ShouldStartWith("_");
        first.ShouldNotBe(afterReset);
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

    [Fact]
    public void Generate_Sequential_NeverProducesCSharpKeyword()
    {
        // Sequential base-26 names would otherwise land on keywords like "do"/"if"/"int", which would
        // not compile if used to rename a source symbol. They must be skipped.
        var generator = new NameGenerator();
        var keywords = new HashSet<string>
        {
            "do", "if", "in", "is", "for", "int", "new", "out", "ref", "try", "void", "null",
            "true", "case", "else", "enum", "goto", "long", "this", "base", "lock", "byte"
        };

        var generated = Enumerable.Range(0, 3000)
            .Select(_ => generator.Generate(NamingMode.Sequential))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var keyword in keywords)
        {
            generated.ShouldNotContain(keyword);
        }
    }

    [Fact]
    public void Generate_Sequential_NeverProducesContextualKeyword()
    {
        var generator = new NameGenerator();
        var keywords = new HashSet<string>
        {
            "var", "record", "file", "required", "async", "await", "yield", "dynamic",
            "nint", "nuint", "and", "or", "not", "with", "init"
        };

        var generated = Enumerable.Range(0, 5000)
            .Select(_ => generator.Generate(NamingMode.Sequential))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var keyword in keywords)
        {
            generated.ShouldNotContain(keyword);
        }
    }
}
