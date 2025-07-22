
using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Infrastructure.Comparers
{
    internal class FamCmp : IEqualityComparer<Family>
    { 
        public bool Equals(Family a, Family b)=>a?.Id==b?.Id;
        public int  GetHashCode(Family f)=>f.Id.IntegerValue; 
    }
}
