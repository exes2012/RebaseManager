using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RebaseProjectWithTemplate.Core.Services;
using RebaseProjectWithTemplate.UI.ViewModels.Base;
using RebaseProjectWithTemplate.UI.ViewModels.Extensions;

using RebaseProjectWithTemplate.Infrastructure.Grok;
using RebaseProjectWithTemplate.Infrastructure.Revit;

namespace RebaseProjectWithTemplate.UI.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private Document _selectedSourceDocument;
        private Document _selectedTemplateDocument;
        private string _progressText;
        private bool _isProgressVisible;
        private bool _isRebaseButtonEnabled = true;

        public ObservableCollection<Document> AllOpenDocuments { get; }
        
        public Document SelectedSourceDocument
        {
            get => _selectedSourceDocument;
            set
            {
                _selectedSourceDocument = value;
                OnPropertyChanged();
                ValidateSelections();
            }
        }

        public Document SelectedTemplateDocument
        {
            get => _selectedTemplateDocument;
            set
            {
                _selectedTemplateDocument = value;
                OnPropertyChanged();
                ValidateSelections();
            }
        }

        public string ProgressText
        {
            get => _progressText;
            set
            {
                _progressText = value;
                OnPropertyChanged();
            }
        }

        public bool IsProgressVisible
        {
            get => _isProgressVisible;
            set
            {
                _isProgressVisible = value;
                OnPropertyChanged();
            }
        }

        public bool IsRebaseButtonEnabled
        {
            get => _isRebaseButtonEnabled;
            set
            {
                _isRebaseButtonEnabled = value;
                OnPropertyChanged();
            }
        }



        public ICommand RebaseCommand { get; }

        // Parameterless constructor for the designer
        public MainViewModel()
        {
            AllOpenDocuments = new ObservableCollection<Document>();
            RebaseCommand = new RelayCommand(ExecuteRebase, CanExecuteRebase);
            IsProgressVisible = true;
            ProgressText = "Rebase process is ready...";
        }

        public MainViewModel(UIApplication uiApp)
        {
            AllOpenDocuments = new ObservableCollection<Document>(
                uiApp.Application.Documents
                    .Cast<Document>()
                    .Where(d => !d.IsFamilyDocument && d.HasModelView())
                    .ToList());

            RebaseCommand = new RelayCommand(ExecuteRebase, CanExecuteRebase);
            
            IsProgressVisible = false;
            ProgressText = "";

            ValidateSelections();
        }

        private void ValidateSelections()
        {
            (RebaseCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private bool CanExecuteRebase(object parameter)
        {
            return SelectedSourceDocument != null &&
                   SelectedTemplateDocument != null &&
                   SelectedSourceDocument.PathName != SelectedTemplateDocument.PathName &&
                   IsRebaseButtonEnabled;
        }

        private async void ExecuteRebase(object parameter)
        {
            IsProgressVisible = true;
            IsRebaseButtonEnabled = false;
            ProgressText = "Starting view template rebase...";

            try
            {
                var progress = new Progress<string>(message =>
                {
                    ProgressText = message;
                });

                using (var grokService = new GrokApiService())
                {
                    var viewTemplateRebaseService = new ViewTemplateRebaseService(grokService);
                    var viewReplacementService = new ViewReplacementService();
                    var rebaseService = new ProjectRebaseService(viewTemplateRebaseService, viewReplacementService);

                    var result = await rebaseService.ExecuteFullRebase(
                        SelectedSourceDocument,
                        SelectedTemplateDocument,
                        progress);

                    if (result.Success)
                    {
                        ProgressText = "Rebase completed successfully!";

                        MessageBox.Show(
                            "Project rebase completed successfully!",
                            "Rebase Complete",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    else
                    {
                        ProgressText = $"Rebase failed: {result.ErrorMessage}";
                        MessageBox.Show(
                            $"View template rebase failed:\n\n{result.ErrorMessage}",
                            "Rebase Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                ProgressText = $"Error: {ex.Message}";
                MessageBox.Show(
                    $"An error occurred during rebase:\n\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                IsRebaseButtonEnabled = true;
                // Keep progress visible to show final status
            }
        }
    }
}