using System;
using System.Collections.Generic;

namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Models
{
    public class FamilyMappingEntry
    {
        public string SourceFamilyName { get; set; }
        public FamilyData SourceFamily { get; set; }
        
        public string TargetFamilyName { get; set; }
        public FamilyData LoadedFamily { get; set; } // После загрузки из template
        
        public MappingStatus Status { get; set; }
        public MappingSource Source { get; set; }
        public List<TypeMappingEntry> TypeMappings { get; set; } = new List<TypeMappingEntry>();
        public string ErrorMessage { get; set; }
        public bool HasInstances { get; set; }
        
        // Для отчетности
        public DateTime ProcessedAt { get; set; }
        public int InstanceCount { get; set; }
    }

    public class TypeMappingEntry
    {
        public string SourceTypeName { get; set; }
        public int SourceTypeId { get; set; }
        
        public string TargetTypeName { get; set; }
        public int TargetTypeId { get; set; }
        
        public TypeMappingStatus Status { get; set; }
        public string ErrorMessage { get; set; }
    }

    public enum MappingStatus
    {
        Pending,           // Ждет обработки
        ExactMatch,        // Точное совпадение имен
        AiMapped,          // Замаплено через AI
        LoadFailed,        // Не удалось загрузить из template
        ToRename,          // Переименовать с _rebase_old
        ToDelete,          // Удалить (нет instances)
        Processed,         // Обработано успешно
        Failed             // Обработка провалилась
    }

    public enum MappingSource
    {
        ExactMatch,
        AI,
        Manual,
        NotMapped
    }

    public enum TypeMappingStatus
    {
        Pending,
        Mapped,
        Failed,
        NotFound
    }

    public class MappingExecutionResult
    {
        public int TotalFamilies { get; set; }
        public int ExactMatches { get; set; }
        public int AiMapped { get; set; }
        public int LoadFailed { get; set; }
        public int RenamedFamilies { get; set; }
        public int DeletedFamilies { get; set; }
        public int SwitchedInstances { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public TimeSpan Duration { get; set; }
    }

    public class SwitchInstancesResult
    {
        public List<string> SuccessfullyProcessedFamilies { get; set; } = new List<string>();
        public List<string> FailedFamilies { get; set; } = new List<string>();
        public List<string> RemovedFamilies { get; set; } = new List<string>();
        public List<string> FamiliesMarkedForDeletion { get; set; } = new List<string>();
        public int SwitchedInstancesCount { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }
}
