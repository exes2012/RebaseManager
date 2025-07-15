using System.Collections.Generic;
using System.Text.Json;

namespace RebaseProjectWithTemplate.Services
{
    public static class PromptService
    {
        public static string GetViewTemplateMappingSystemPrompt()
        {
            return @"You are an expert Revit BIM consultant specializing in view template mapping and project standardization. 
Your task is to analyze view template names from a source project and map them to the most appropriate templates in a target project based on SEMANTIC MEANING and FUNCTIONAL PURPOSE, not just text similarity.

CRITICAL MAPPING PRINCIPLES:
1. **SEMANTIC UNDERSTANDING**: Focus on what each template DOES, not just name similarity
   - GRIDD templates = Floor access systems and grids
   - MAC templates = Movable access covers 
   - PATHWAY templates = Cable routing and pathways
   - POWER templates = Electrical power systems

2. **STRICT 1-TO-1 FUNCTIONAL MAPPING**: Each source maps to exactly ONE target
   - FLOOR PLAN → FLOOR PLAN (G-F)
   - INSTALL PLAN → INSTALL PLAN (G-I) 
   - AREA PLAN → AREA PLAN (G-A)
   - STAGING PLAN → STAGING PLAN (G-S)
   - DETAIL → DETAIL templates
   - ZONE → ZONE templates
   - REINFORCEMENT → REINFORCEMENT (G-R)
   - NO target template should be used twice

3. **DISCIPLINE CONSISTENCY**: Keep discipline groupings intact
   - All GRIDD- templates should map to GRIDD- targets
   - All MAC- templates should map to MAC- targets
   - All PATHWAY- templates should map to PATHWAY- targets
   - All POWER- templates should map to POWER- targets

4. **VERSION PRIORITY & DEDUPLICATION**: For multiple source versions, choose ONLY ONE:
   - Prefer templates WITHOUT version suffixes (V2, V3, OLD)
   - If only versioned templates exist, choose the highest version number
   - Ignore 'Working View' and 'INTERNAL' templates unless they're the only option
   - CRITICAL: Do not map multiple versions of the same template - choose the best one

5. **SEMANTIC EQUIVALENTS**: Understand these are the same concept:
   - 'CABLE DROP PLAN' = 'GC-D' 
   - 'LOW VOLTAGE PLAN' = 'GC-LV'
   - 'POWER AND LOW VOLTAGE PLAN' = 'GC-PV'
   - 'CURB & FRAME' = 'G-C'
   - 'INSTALL ZONE' = 'G-Z'

Map based on MEANING and FUNCTION, not confidence scores. Every source template should have a logical target based on its purpose.

Respond ONLY with valid JSON in the specified format. Do not include any explanatory text.";
        }

        public static string CreateViewTemplateMappingPrompt(List<string> sourceTemplates, List<string> targetTemplates)
        {
            var sourceJson = JsonSerializer.Serialize(sourceTemplates);
            var targetJson = JsonSerializer.Serialize(targetTemplates);

            return $@"Analyze and map source view templates to target templates based on SEMANTIC MEANING and FUNCTIONAL PURPOSE.

SOURCE TEMPLATES (from project being modified):
{sourceJson}

TARGET TEMPLATES (standardized templates to use):
{targetJson}

CRITICAL MAPPING REQUIREMENTS:
1. **STRICT 1-TO-1 MAPPING**: Each source template maps to exactly ONE target template
2. **NO DUPLICATES**: Each target template can only be used ONCE in the mapping
3. **SEMANTIC MAPPING**: Map by functional purpose, not text similarity
4. **VERSION CONSOLIDATION**: For multiple versioned source templates (V2, V3, OLD), choose ONLY the most current/appropriate version and map it. IGNORE the rest.
5. **DISCIPLINE PRESERVATION**: Keep discipline groupings (GRIDD→GRIDD, MAC→MAC, etc.)
6. **FUNCTIONAL EQUIVALENCE**: Understand semantic equivalents:
   - FLOOR PLAN concepts → G-F targets
   - INSTALL PLAN concepts → G-I targets  
   - AREA PLAN concepts → G-A targets
   - DETAIL concepts → Detail targets
   - ZONE concepts → Zone targets
   - etc.

ANALYSIS APPROACH:
- For versioned templates (e.g., GRIDD - 04 - INSTALL PLAN - G-I, GRIDD - 04 - INSTALL PLAN - G-I V2, GRIDD - 04 - INSTALL PLAN - G-I V3), choose ONLY ONE (preferably the highest version or base version)
- Each target template should appear exactly ONCE in the mappings
- If multiple source templates have the same function, choose the best representative
- Ignore version numbers and focus on core function
- Understand abbreviations and naming conventions
- Map based on workflow purpose, not string matching
- Prioritize functional clarity over naming similarity

VALIDATION RULES:
- Count of mappings should be less than or equal to count of target templates
- No target template should appear twice in mappings
- Each source template should appear at most once in mappings

Return response in this exact JSON format:
{{
  ""mappings"": [
    {{
      ""sourceTemplate"": ""exact source name"",
      ""targetTemplate"": ""exact target name"",
      ""confidence"": 1.0,
      ""reason"": ""functional mapping explanation""
    }}
  ],
  ""unmapped"": [
    {{
      ""sourceTemplate"": ""exact source name"",
      ""reason"": ""no functional equivalent found""
    }}
  ]
}}";
        }
    }
}
