
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.UI;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.DependencyInjection;
using RebaseProjectWithTemplate.UI.ViewModels.Base;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;

namespace RebaseProjectWithTemplate.Commands.Rebase.UI.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly UIApplication _uiApp;
        private bool _isRebaseInProgress;
        private string _progressText;
        private string _templateFilePath;

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

                var progress = new Progress<string>(message => ProgressText = message).WithDoEvents();

                var result = await orchestrator.ExecuteFullRebase(true, true, progress);

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
            catch (Exception ex)
            {
                TaskDialog.Show("Error", $"An unexpected error occurred: {ex.Message}");
            }
            finally
            {
                IsRebaseInProgress = false;
            }
        }
    }
}
