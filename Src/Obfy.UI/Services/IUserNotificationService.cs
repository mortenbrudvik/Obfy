namespace Obfy.UI.Services;

public enum NotificationSeverity
{
    Success,
    Warning,
    Error
}

/// <summary>
/// Host-agnostic user notifications so ViewModels do not construct WPF-UI icons.
/// </summary>
public interface IUserNotificationService
{
    void Show(string title, string message, NotificationSeverity severity);
}
