
using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Infrastructure.Comparers
{
    internal class IdCmp : IEqualityComparer<ElementId>
    { 
        public bool Equals(ElementId a, ElementId b)=>a.IntegerValue==b.IntegerValue;
        public int  GetHashCode(ElementId id)=>id.IntegerValue; 
    }
}
