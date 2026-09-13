using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Obfy.UI.Services;

public sealed class WpfUserNotificationService : IUserNotificationService
{
    private readonly ISnackbarService _snackbarService;

    public WpfUserNotificationService(ISnackbarService snackbarService)
    {
        _snackbarService = snackbarService;
    }

    public void Show(string title, string message, NotificationSeverity severity)
    {
        var appearance = severity switch
        {
            NotificationSeverity.Error => ControlAppearance.Danger,
            NotificationSeverity.Warning => ControlAppearance.Caution,
            NotificationSeverity.Success => ControlAppearance.Success,
            _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null)
        };

        var symbol = severity switch
        {
            NotificationSeverity.Error => SymbolRegular.ErrorCircle24,
            NotificationSeverity.Warning => SymbolRegular.Warning24,
            NotificationSeverity.Success => SymbolRegular.Checkmark24,
            _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null)
        };

        IconElement? icon = Application.Current is null ? null : new SymbolIcon(symbol);
        _snackbarService.Show(title, message, appearance, icon, TimeSpan.FromSeconds(4));
    }
}
