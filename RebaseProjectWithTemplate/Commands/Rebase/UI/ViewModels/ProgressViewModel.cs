using System;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.UI;
using RebaseProjectWithTemplate.UI.ViewModels.Base;

namespace RebaseProjectWithTemplate.Commands.Rebase.UI.ViewModels;

public class ProgressViewModel : ViewModelBase, IProgressReporter
{
    private readonly CancellationTokenSource _cancellationTokenSource;
    private double _progressValue;
    private string _taskStatus;
    private string _stepStatus;
    private bool _isCancelEnabled = true;

    public ProgressViewModel()
    {
    }

    public ProgressViewModel(CancellationTokenSource cancellationTokenSource)
    {
        _cancellationTokenSource = cancellationTokenSource;
        CancelCommand = new RelayCommand(OnCancelCommandExecute);
    }

    public ICommand CancelCommand { get; }

    public bool IsCancelEnabled
    {
        get => _isCancelEnabled;
        set => SetProperty(ref _isCancelEnabled, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        set => SetProperty(ref _progressValue, value);
    }

    public string TaskStatus
    {
        get => _taskStatus;
        set => SetProperty(ref _taskStatus, value);
    }

    public string StepStatus
    {
        get => _stepStatus;
        set => SetProperty(ref _stepStatus, value);
    }

    public void ReportProgress(double value)
    {
        ProgressValue = value;
    }

    public void ReportStatus(string status)
    {
        TaskStatus = status;
    }

    public void ReportStepStatus(string status)
    {
        StepStatus = status;
    }

    private void OnCancelCommandExecute(object parameter)
    {
        var result = MessageBox.Show(
            "Are you sure you want to cancel the rebase operation?",
            "Cancel Rebase",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            IsCancelEnabled = false;
            _cancellationTokenSource?.Cancel();

            if (parameter is Window window)
            {
                window.Close();
            }
        }
    }
}
