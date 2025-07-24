using System.Windows.Forms;

namespace RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.UI;

public class ProgressReporter : IProgress<string>
{
    private readonly IProgress<string> _innerProgress;
    private readonly bool _enableDoEvents;

    public ProgressReporter(IProgress<string> innerProgress, bool enableDoEvents = true)
    {
        _innerProgress = innerProgress;
        _enableDoEvents = enableDoEvents;
    }

    public void Report(string value)
    {
        _innerProgress?.Report(value);
        
        if (_enableDoEvents)
        {
            // Allow UI to update and remain responsive
            Application.DoEvents();
        }
    }
}

public static class ProgressExtensions
{
    /// <summary>
    /// Wraps an IProgress with DoEvents functionality
    /// </summary>
    public static IProgress<string> WithDoEvents(this IProgress<string> progress)
    {
        return new ProgressReporter(progress, true);
    }
}
