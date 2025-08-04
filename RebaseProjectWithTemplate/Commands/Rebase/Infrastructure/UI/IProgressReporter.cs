namespace RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.UI;

public interface IProgressReporter
{
    void ReportProgress(double value);
    void ReportStatus(string status);
    void ReportStepStatus(string status);
}
