using WpfLib;

namespace WpfApp;

public sealed class MainViewModel
{
    public string Title { get; } = Greeter.Hello();
}
