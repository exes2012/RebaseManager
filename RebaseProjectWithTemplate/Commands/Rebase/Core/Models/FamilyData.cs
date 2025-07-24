namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Models;

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