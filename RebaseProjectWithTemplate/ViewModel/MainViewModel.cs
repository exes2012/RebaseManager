using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RebaseProjectWithTemplate.ViewModel.Base;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace RebaseProjectWithTemplate.ViewModel
{
    public class MainViewModel : ViewModelBase
    {
        private Document _selectedSourceDocument;
        private Document _selectedTemplateDocument;
        private string _progressText;
        private bool _isProgressVisible;

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
                   SelectedSourceDocument.PathName != SelectedTemplateDocument.PathName;
        }

        private void ExecuteRebase(object parameter)
        {
            IsProgressVisible = true;
            ProgressText = "Rebasing in progress...";

            Task.Delay(3000).ContinueWith(t =>
            {
                IsProgressVisible = false;
                ProgressText = "Rebase Completed";
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }

    public static class DocumentExtensions
    {
        public static bool HasModelView(this Document doc)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            return collector.OfClass(typeof(View3D)).Any();
        }
    }
    
    public static class RelayCommandExtensions
    {
        public static void RaiseCanExecuteChanged(this ICommand command)
        {
            if (command is RelayCommand relayCommand)
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
}