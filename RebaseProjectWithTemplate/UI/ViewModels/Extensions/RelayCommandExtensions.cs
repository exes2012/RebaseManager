using System.Windows.Input;
using RebaseProjectWithTemplate.UI.ViewModels.Base;

namespace RebaseProjectWithTemplate.UI.ViewModels.Extensions
{
    public static class RelayCommandExtensions
    {
        public static void RaiseCanExecuteChanged(this ICommand command)
        {
            if (command is RelayCommand)
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
}
