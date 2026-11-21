using System.Threading.Tasks;

namespace Obfy.VisualStudio.Services;

public static class OutputServiceExtensions
{
    /// <summary>
    /// Shows the Obfy output pane. A missing output service (the package has not created it) skips the activation.
    /// <c>await output?.ActivateAsync()</c> must not be used for this: on a null service the expression is a null
    /// <see cref="Task"/>, and awaiting it throws a <see cref="System.NullReferenceException"/>.
    /// </summary>
    public static Task ActivateIfAvailableAsync(this IOutputService? output) =>
        output?.ActivateAsync() ?? Task.CompletedTask;
}
