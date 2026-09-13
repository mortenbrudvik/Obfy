using Obfy.UI.Help;
using Obfy.UI.ViewModels;
using Shouldly;

namespace Obfy.UI.Tests.ViewModels;

public class HelpViewModelTests
{
    [Fact]
    public void Constructor_SelectsGettingStarted()
    {
        var vm = new HelpViewModel();
        vm.SelectedTopic.Id.ShouldBe(HelpCatalog.GettingStartedId);
    }

    [Fact]
    public void Topics_MatchCatalogOrder()
    {
        var vm = new HelpViewModel();
        var catalog = HelpCatalog.Create();
        vm.Topics.Select(t => t.Id).ShouldBe(catalog.Select(t => t.Id));
    }

    [Fact]
    public void SettingSelectedTopic_UpdatesArticle_LeavesTopicsUnchanged()
    {
        var vm = new HelpViewModel();
        var before = vm.Topics.ToList();

        vm.SelectedTopic = vm.Topics[3];

        vm.SelectedTopic.Title.ShouldBe("Techniques");
        vm.Topics.Select(t => t.Id).ShouldBe(before.Select(t => t.Id));
    }
}
