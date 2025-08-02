using System.Windows;
using Autodesk.Revit.DB;
using RebaseProjectWithTemplate.Commands.TestRebase.UI.ViewModels;

namespace RebaseProjectWithTemplate.Commands.TestRebase.UI.Views
{
    public partial class TestRebaseView : Window
    {
        public TestRebaseView(Document doc)
        {
            InitializeComponent();
            DataContext = new TestRebaseViewModel(doc);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

    }
}
