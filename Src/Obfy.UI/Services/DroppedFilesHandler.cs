using Mediator;
using Microsoft.Extensions.Logging;
using Obfy.App.Features.Files;
using Obfy.App.Models;
using Obfy.App.Sessions;

namespace Obfy.UI.Services;

/// <summary>
/// What the shell does with the files the page says were dropped: sends them through the mediator, off the UI thread. A drop
/// has no page caller to answer, so a failure is a notice on the file list (the page follows <c>files.changed</c>) as well
/// as a log line; the log keeps the exception type, never its message, which names the dropped file.
/// </summary>
internal sealed class DroppedFilesHandler(IMediator mediator, InputSession input, ILogger logger)
{
    /// <summary>
    /// Starts the work on the thread pool and returns at once: dropping a solution makes the host read and analyse it, which
    /// must not freeze the window. Nothing waits for the answer.
    /// </summary>
    public void Handle(IReadOnlyList<string> paths) => _ = Task.Run(() => HandleAsync(paths));

    /// <summary>Never throws: an exception from a fire-and-forget task would only surface as an unobserved one.</summary>
    public async Task HandleAsync(IReadOnlyList<string> paths)
    {
        try
        {
            await mediator.Send(new DropPathsRequest(paths));
        }
        catch (Exception ex)
        {
            logger.LogError("Could not handle the dropped files ({ExceptionType})", ex.GetType().Name);
            try
            {
                input.Notify(NoticeLevel.Error, "Could not add the dropped files", "Something went wrong while adding them. Try again, or use Add files.");
            }
            catch (Exception noticeEx)
            {
                logger.LogError("Could not tell the user ({ExceptionType})", noticeEx.GetType().Name);
            }
        }
    }
}
