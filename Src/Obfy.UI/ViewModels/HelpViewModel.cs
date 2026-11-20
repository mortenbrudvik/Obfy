using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Obfy.UI.Help;

namespace Obfy.UI.ViewModels;

public partial class HelpViewModel : ObservableObject
{
    public IReadOnlyList<HelpTopic> Topics { get; }

    public Action? RequestClose { get; set; }

    [ObservableProperty]
    private HelpTopic _selectedTopic;

    public HelpViewModel()
    {
        Topics = HelpCatalog.Create();
        _selectedTopic = Topics[0];
    }

    [RelayCommand]
    private void CloseHelp() => RequestClose?.Invoke();
}
