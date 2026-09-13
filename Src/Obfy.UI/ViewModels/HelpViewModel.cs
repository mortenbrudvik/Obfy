using CommunityToolkit.Mvvm.ComponentModel;
using Obfy.UI.Help;

namespace Obfy.UI.ViewModels;

public partial class HelpViewModel : ObservableObject
{
    public IReadOnlyList<HelpTopic> Topics { get; }

    [ObservableProperty]
    private HelpTopic _selectedTopic;

    public HelpViewModel()
    {
        Topics = HelpCatalog.Create();
        _selectedTopic = Topics[0];
    }
}
