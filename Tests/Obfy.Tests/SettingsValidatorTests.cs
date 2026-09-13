using System.ComponentModel.DataAnnotations;
using Settings.Core;
using Settings.Core.Validation;
using Shouldly;

namespace Obfy.Tests;

public class SettingsValidatorTests
{
    [Fact]
    public void IsValid_DefaultSample_IsTrue()
    {
        SettingsValidator.IsValid(new SampleSettings()).ShouldBeTrue();
    }

    [Fact]
    public void TryValidate_RequiredViolation_IsFalse()
    {
        var results = new List<ValidationResult>();
        SettingsValidator.TryValidate(new SampleSettings { Name = "" }, results).ShouldBeFalse();
        results.ShouldNotBeEmpty();
    }

    [Fact]
    public void TryValidate_NestedRangeViolation_PrefixesMemberName()
    {
        var results = new List<ValidationResult>();
        var settings = new SampleSettings { Nested = new NestedSettings { Flag = 9 } };
        SettingsValidator.TryValidate(settings, results).ShouldBeFalse();
        results.ShouldContain(r => r.ErrorMessage != null && r.ErrorMessage.StartsWith("Nested."));
    }

    [Fact]
    public void Validate_Invalid_ThrowsValidationException()
    {
        Should.Throw<ValidationException>(() => SettingsValidator.Validate(new SampleSettings { Count = 99 }));
    }

    [Fact]
    public void SettingsServiceBase_SaveLoad_RoundTripsAndRejectsInvalid()
    {
        var path = Path.Combine(Path.GetTempPath(), "obfy-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var service = new SampleSettingsService(path);
            service.Settings.Name = "saved";
            service.Settings.Count = 3;
            service.Save();

            var loaded = new SampleSettingsService(path);
            loaded.Load();
            loaded.Settings.Name.ShouldBe("saved");
            loaded.Settings.Count.ShouldBe(3);

            service.Settings.Count = 99;
            Should.Throw<ValidationException>(() => service.Save());
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private sealed class SampleSettings
    {
        [Required]
        public string Name { get; set; } = "ok";

        [Range(1, 10)]
        public int Count { get; set; } = 5;

        public NestedSettings Nested { get; set; } = new();
    }

    private sealed class NestedSettings
    {
        [Range(0, 1)]
        public int Flag { get; set; }
    }

    private sealed class SampleSettingsService : SettingsServiceBase<SampleSettings>
    {
        public SampleSettingsService(string path) : base(path) { }
    }
}
