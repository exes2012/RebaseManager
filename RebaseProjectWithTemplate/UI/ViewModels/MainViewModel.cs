using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Services;
using RebaseProjectWithTemplate.UI.ViewModels.Base;
using RebaseProjectWithTemplate.UI.ViewModels.Extensions;

using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Grok;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Revit;
using RebaseProjectWithTemplate.Infrastructure;

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

        private bool _copyViewTemplates = true;
        public bool CopyViewTemplates
        {
            get => _copyViewTemplates;
            set
            {
                _copyViewTemplates = value;
                OnPropertyChanged();
            }
        }

        private bool _rebaseTitleBlocks = true;
        public bool RebaseTitleBlocks
        {
            get => _rebaseTitleBlocks;
            set
            {
                _rebaseTitleBlocks = value;
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
            LogHelper.Information("=== Starting Project Rebase ===");
            LogHelper.Information($"Source Document: {SelectedSourceDocument?.PathName ?? "Unknown"}");
            LogHelper.Information($"Template Document: {SelectedTemplateDocument?.PathName ?? "Unknown"}");

            IsProgressVisible = true;
            IsRebaseButtonEnabled = false;
            ProgressText = "Starting rebase...";

            try
            {
                var progress = new Progress<string>(message =>
                {
                    ProgressText = message;
                    LogHelper.Information($"Progress: {message}");
                });

                var rebaseService = new ProjectRebaseService();

                var result = await rebaseService.ExecuteFullRebase(
                    SelectedSourceDocument,
                    SelectedTemplateDocument,
                    CopyViewTemplates,
                    RebaseTitleBlocks,
                    progress);

                if (result.Success)
                {
                    ProgressText = "Rebase completed successfully!";
                    LogHelper.Information("=== Project Rebase Completed Successfully ===");

                    MessageBox.Show(
                        "Project rebase completed successfully!",
                        "Rebase Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    ProgressText = $"Rebase failed: {result.ErrorMessage}";
                    LogHelper.Error($"Project Rebase Failed: {result.ErrorMessage}");

                    MessageBox.Show(
                        $"Rebase failed:\n\n{result.ErrorMessage}",
                        "Rebase Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                ProgressText = $"Error: {ex.Message}";
                LogHelper.Error($"Unexpected error during rebase: {ex.Message}");
                LogHelper.Error($"Stack trace: {ex.StackTrace}");

                MessageBox.Show(
                    $"An error occurred during rebase:\n\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                IsRebaseButtonEnabled = true;
                LogHelper.Information("=== Project Rebase Session Ended ===");
                // Keep progress visible to show final status
            }
        }
    }
}