namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Models;

public class CategoryRebaseReport
{
    public string DocumentName { get; set; }
    public string CategoryName { get; set; }
    public List<ExactMatchFamily> ExactMatches { get; set; } = new();
    public List<FamilyMappingReport> FamilyMappings { get; set; } = new();
    public List<FamilyData> TemplateOnlyFamilies { get; set; } = new();
}

public class ExactMatchFamily
{
    public string FamilyName { get; set; }
    public List<string> TypeNames { get; set; } = new();
}

public class FamilyMappingReport
{
    public string SourceFamily { get; set; }
    public string TargetFamily { get; set; }
    public string MappingStatus { get; set; } // "Mapped", "No Match"
    public List<TypeMappingReport> TypeMappings { get; set; } = new();
}

public class TypeMappingReport
{
    public string SourceType { get; set; }
    public string TargetType { get; set; }
    public string MappingStatus { get; set; } // "Mapped", "No Match", "Kept Old"
}
