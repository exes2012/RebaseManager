using Autodesk.Revit.UI;

namespace RebaseProjectWithTemplate.Infrastructure
{
    /// <summary>
    /// Static context for storing Revit application references
    /// </summary>
    public static class RevitApplicationContext
    {
        private static UIControlledApplication _uiControlledApplication;

        public static void Initialize(UIControlledApplication uiControlledApp)
        {
            _uiControlledApplication = uiControlledApp;
        }

        public static UIControlledApplication GetUIControlledApplication()
        {
            return _uiControlledApplication;
        }
    }
}
