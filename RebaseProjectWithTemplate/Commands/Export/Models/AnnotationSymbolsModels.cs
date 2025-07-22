using System.Collections.Generic;

namespace RebaseProjectWithTemplate.Commands.Export.Models
{
    public class CategoryData
    {
        public string CategoryName { get; set; }
        public List<FamilyData> Families { get; set; }
    }

    public class FamilyData
    {
        public string FamilyName { get; set; }
        public long FamilyId { get; set; }
        public List<FamilyTypeData> Types { get; set; }
    }

    public class FamilyTypeData
    {
        public string TypeName { get; set; }
        public long TypeId { get; set; }
    }
}