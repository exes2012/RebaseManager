using System.Windows.Forms;

namespace RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.UI;

public class ProgressReporter : IProgress<string>
{
    private readonly IProgress<string> _innerProgress;
    private readonly IProgressReporter _progressReporter;
    private readonly bool _enableDoEvents;
    private int _currentStep = 0;
    private int _totalSteps = 9; // Total number of steps in rebase

    public ProgressReporter(IProgress<string> innerProgress, bool enableDoEvents = true)
    {
        _innerProgress = innerProgress;
        _enableDoEvents = enableDoEvents;
    }

    public ProgressReporter(IProgressReporter progressReporter, bool enableDoEvents = true)
    {
        _progressReporter = progressReporter;
        _enableDoEvents = enableDoEvents;
    }

    public void Report(string value)
    {
        _innerProgress?.Report(value);

        if (_progressReporter != null)
        {
            if (value.StartsWith("Step"))
            {
                _progressReporter.ReportStepStatus(value);
                // Extract step number and calculate progress
                if (int.TryParse(value.Substring(5, 1), out int stepNumber))
                {
                    _currentStep = stepNumber;
                    var progress = (double)_currentStep / _totalSteps * 100;
                    _progressReporter.ReportProgress(progress);
                }
            }
            else
            {
                _progressReporter.ReportStatus(value);
            }
        }

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

    /// <summary>
    /// Wraps an IProgressReporter with DoEvents functionality
    /// </summary>
    public static IProgress<string> WithDoEvents(this IProgressReporter progressReporter)
    {
        return new ProgressReporter(progressReporter, true);
    }
}
