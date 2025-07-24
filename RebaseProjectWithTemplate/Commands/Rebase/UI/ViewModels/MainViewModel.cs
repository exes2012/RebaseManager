
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Models;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.DependencyInjection;
using RebaseProjectWithTemplate.UI.ViewModels.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace RebaseProjectWithTemplate.Commands.Rebase.UI.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly UIApplication _uiApp;
        private bool _isRebaseInProgress;
        private string _progressText;
        private Document _selectedSourceDocument;
        private Document _selectedTemplateDocument;

        public MainViewModel() : this(null)
        {
        }

        public MainViewModel(UIApplication uiApp)
        {
            _uiApp = uiApp;
            Documents = uiApp?.Application.Documents.Cast<Document>().ToList() ?? new List<Document>();
            RebaseCommand = new RelayCommand(async (o) => await ExecuteRebaseCommandAsync());
        }

        public List<Document> Documents { get; }

        public Document SelectedSourceDocument
        {
            get => _selectedSourceDocument;
            set
            {
                SetProperty(ref _selectedSourceDocument, value);
                OnPropertyChanged(nameof(CanExecuteRebase));
                OnPropertyChanged(nameof(ShowSameDocumentWarning));
            }
        }

        public Document SelectedTemplateDocument
        {
            get => _selectedTemplateDocument;
            set
            {
                SetProperty(ref _selectedTemplateDocument, value);
                OnPropertyChanged(nameof(CanExecuteRebase));
                OnPropertyChanged(nameof(ShowSameDocumentWarning));
            }
        }

        public ICommand RebaseCommand { get; }

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

        public bool CanExecuteRebase => SelectedSourceDocument != null &&
                                       SelectedTemplateDocument != null &&
                                       SelectedSourceDocument != SelectedTemplateDocument &&
                                       !IsRebaseInProgress;

        public bool ShowSameDocumentWarning => SelectedSourceDocument != null &&
                                              SelectedTemplateDocument != null &&
                                              SelectedSourceDocument == SelectedTemplateDocument;

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
                var orchestrator = ServiceProvider.CreateRebaseOrchestrator(SelectedSourceDocument, SelectedTemplateDocument);

                var progress = new Progress<string>(message => ProgressText = message);

                var result = await orchestrator.ExecuteFullRebase(true, true, progress);

                if (result.Success)
                {
                    TaskDialog.Show("Success", "Project rebased successfully!");
                }
                else
                {
                    TaskDialog.Show("Error", $"Rebase failed: {result.ErrorMessage}");
                }
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
