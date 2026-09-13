namespace WpfApp;

/// <summary>
/// Public type that does not match the *ViewModel / *View heuristic.
/// With preserveXaml and without preservePublicApi its name is expected to be renamed.
/// </summary>
public sealed class DashboardModel
{
    public string Caption { get; } = "should-be-renamed";
}
