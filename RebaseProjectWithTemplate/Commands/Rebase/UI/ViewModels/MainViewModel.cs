
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.UI;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.DependencyInjection;
using RebaseProjectWithTemplate.UI.ViewModels.Base;
using RebaseProjectWithTemplate.Commands.Rebase.UI.Views;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Ai.Prompting;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

namespace RebaseProjectWithTemplate.Commands.Rebase.UI.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly UIApplication _uiApp;
        private bool _isRebaseInProgress;
        private string _progressText;
        private string _templateFilePath;

        // Rebase options
        private bool _rebaseViewTemplates = true;
        private bool _rebaseTitleBlocks = true;
        private bool _rebaseSystemElements = true;
        private bool _rebaseSchedules = true;
        private bool _rebaseSharedParameters = true;

        public MainViewModel() : this(null)
        {
        }

        public MainViewModel(UIApplication uiApp)
        {
            _uiApp = uiApp;
            BrowseCommand = new RelayCommand(ExecuteBrowseCommand);
            RebaseCommand = new RelayCommand(async (o) => await ExecuteRebaseCommandAsync(), (o) => CanExecuteRebase);
        }

        public ICommand BrowseCommand { get; }
        public ICommand RebaseCommand { get; }

        public string TemplateFilePath
        {
            get => _templateFilePath;
            set
            {
                SetProperty(ref _templateFilePath, value);
                OnPropertyChanged(nameof(CanExecuteRebase));
            }
        }

        public bool IsRebaseInProgress
        {
            get => _isRebaseInProgress;
            set
            {
                SetProperty(ref _isRebaseInProgress, value);
                OnPropertyChanged(nameof(CanExecuteRebase));
            }
        }

        public string ProgressText
        {
            get => _progressText;
            set => SetProperty(ref _progressText, value);
        }

        public bool CanExecuteRebase => !string.IsNullOrEmpty(TemplateFilePath) &&
                                       !IsRebaseInProgress;

        // Rebase options properties
        public bool RebaseViewTemplates
        {
            get => _rebaseViewTemplates;
            set => SetProperty(ref _rebaseViewTemplates, value);
        }

        public bool RebaseTitleBlocks
        {
            get => _rebaseTitleBlocks;
            set => SetProperty(ref _rebaseTitleBlocks, value);
        }

        public bool RebaseSystemElements
        {
            get => _rebaseSystemElements;
            set => SetProperty(ref _rebaseSystemElements, value);
        }

        public bool RebaseSchedules
        {
            get => _rebaseSchedules;
            set => SetProperty(ref _rebaseSchedules, value);
        }

        public bool RebaseSharedParameters
        {
            get => _rebaseSharedParameters;
            set => SetProperty(ref _rebaseSharedParameters, value);
        }

        private void ExecuteBrowseCommand(object obj)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Revit Files (*.rvt)|*.rvt",
                Title = "Select Template File"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                TemplateFilePath = openFileDialog.FileName;
            }
        }

        private async Task ExecuteRebaseCommandAsync()
        {
            if (_uiApp == null)
            {
                TaskDialog.Show("Error", "UIApplication not initialized");
                return;
            }

            IsRebaseInProgress = true;
            ProgressText = "Starting rebase...";

            var cancellationTokenSource = new CancellationTokenSource();
            var progressViewModel = new ProgressViewModel(cancellationTokenSource);

            // Show progress window in separate thread
            ShowProgressWindowOnNewThread(progressViewModel);

            try
            {
                var sourceDocument = _uiApp.ActiveUIDocument.Document;
                var sourcePath = sourceDocument.PathName;
                sourceDocument.Save();

                var rebasedPath = Path.Combine(Path.GetDirectoryName(sourcePath),
                    $"{Path.GetFileNameWithoutExtension(sourcePath)}_REBASED{Path.GetExtension(sourcePath)}");

                File.Copy(sourcePath, rebasedPath, true);

                var rebasedDocument = _uiApp.Application.OpenDocumentFile(rebasedPath);
                var templateDocument = _uiApp.Application.OpenDocumentFile(TemplateFilePath);

                var orchestrator = ServiceProvider.CreateRebaseOrchestrator(rebasedDocument, templateDocument);

                // Use progress reporter that updates the separate progress window
                var progress = progressViewModel.WithDoEvents();

                var result = await orchestrator.ExecuteFullRebase(
                    RebaseViewTemplates,
                    RebaseTitleBlocks,
                    RebaseSystemElements,
                    RebaseSchedules,
                    RebaseSharedParameters,
                    BuiltInCategory.OST_Floors,
                    progress,
                    cancellationTokenSource.Token);

                if (result.Success)
                {
                    TaskDialog.Show("Success", "Project rebased successfully!");
                }
                else
                {
                    TaskDialog.Show("Error", $"Rebase failed: {result.ErrorMessage}");
                }
                rebasedDocument.Close(true);
                templateDocument.Close(false);
            }
            catch (OperationCanceledException)
            {
                TaskDialog.Show("Cancelled", "Rebase operation was cancelled by user.");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", $"An unexpected error occurred: {ex.Message}");
            }
            finally
            {
                IsRebaseInProgress = false;
                // Close progress window
                CloseProgressWindow();
            }
        }

        private ProgressView _progressView;

        private void ShowProgressWindowOnNewThread(ProgressViewModel progressViewModel)
        {
            var progressWindowThread = new Thread(() =>
            {
                _progressView = new ProgressView();
                _progressView.DataContext = progressViewModel;
                _progressView.Show();

                Dispatcher.Run();
            });

            progressWindowThread.SetApartmentState(ApartmentState.STA);
            progressWindowThread.IsBackground = true;
            progressWindowThread.Start();
        }

        private void CloseProgressWindow()
        {
            _progressView?.Dispatcher.Invoke(() => { _progressView.Close(); });
        }
    }
}
