using Autodesk.Revit.DB;
using System.Linq;

namespace RebaseProjectWithTemplate.UI.ViewModels.Extensions
{
    public static class DocumentExtensions
    {
        public static bool HasModelView(this Document doc)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            return collector.OfClass(typeof(View3D)).Any();
        }
    }
}
