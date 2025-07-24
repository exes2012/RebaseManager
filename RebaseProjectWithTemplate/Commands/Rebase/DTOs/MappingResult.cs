namespace RebaseProjectWithTemplate.Commands.Rebase.DTOs;

public class MappingResult
{
    public string Old { get; set; }
    public string New { get; set; }
    public List<TypeMatch> TypeMatches { get; set; }
}

public class TypeMatch
{
    public string OldType { get; set; }
    public string NewType { get; set; }
}