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
    public void Generate_Hash_ReturnsDeterministicName()
    {
        // Arrange
        var generator = new NameGenerator();

        // Act
        var name1 = generator.Generate("TestMethod", NamingMode.Hash);
        var name2 = generator.Generate("TestMethod", NamingMode.Hash);

        // Assert
        name1.ShouldBe(name2);
        name1.ShouldStartWith("_");
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
