using System.Collections.Generic;

namespace RebaseProjectWithTemplate.Models
{
    public class ViewTemplateMappingResponse
    {
        public List<ViewTemplateMapping> Mappings { get; set; } = new();
        public List<UnmappedViewTemplate> Unmapped { get; set; } = new();
    }

    public class ViewTemplateMapping
    {
        public string SourceTemplate { get; set; }
        public string TargetTemplate { get; set; }
    }

    public class UnmappedViewTemplate
    {
        public string SourceTemplate { get; set; }
        public string Reason { get; set; }
    }
}
