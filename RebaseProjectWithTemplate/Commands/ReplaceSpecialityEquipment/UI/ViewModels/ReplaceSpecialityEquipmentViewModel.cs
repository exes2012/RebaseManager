
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RebaseProjectWithTemplate.UI.ViewModels.Base;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Diagnostics;

namespace RebaseProjectWithTemplate.Commands.ReplaceSpecialityEquipment.UI.ViewModels
{
    public class ReplaceSpecialityEquipmentViewModel : ViewModelBase
    {
        private readonly UIApplication _uiApp;
        private Document _doc;

        private FamilySymbol _selectedFamilySymbolToReplace;
        public FamilySymbol SelectedFamilySymbolToReplace
        {
            get => _selectedFamilySymbolToReplace;
            set => SetProperty(ref _selectedFamilySymbolToReplace, value);
        }

        private FamilySymbol _selectedFamilySymbol;
        public FamilySymbol SelectedFamilySymbol
        {
            get => _selectedFamilySymbol;
            set => SetProperty(ref _selectedFamilySymbol, value);
        }



        private bool _useMassReplace = true;
        public bool UseMassReplace
        {
            get => _useMassReplace;
            set => SetProperty(ref _useMassReplace, value);
        }

        private string _operationTime;
        public string OperationTime
        {
            get => _operationTime;
            set => SetProperty(ref _operationTime, value);
        }

        public ICommand ReplaceCommand { get; }

        public List<FamilySymbol> FamilySymbolsToReplace { get; }
        public List<FamilySymbol> FamilySymbols { get; }

        public ReplaceSpecialityEquipmentViewModel(UIApplication uiApp)
        {
            _uiApp = uiApp;
            _doc = _uiApp.ActiveUIDocument.Document;

            FamilySymbolsToReplace = GetSpecialityEquipmentSymbolsToReplace();
            FamilySymbols = GetSpecialityEquipmentSymbols();

            ReplaceCommand = new RelayCommand(Replace, CanReplace);
        }

        private List<FamilySymbol> GetSpecialityEquipmentSymbolsToReplace()
        {
            return new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_SpecialityEquipment)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Select(fi => fi.Symbol)
                .Distinct(new ElementIdComparer())
                .OrderBy(s => s.FamilyName).ThenBy(s => s.Name)
                .ToList();
        }

        private List<FamilySymbol> GetSpecialityEquipmentSymbols()
        {
            return new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_SpecialityEquipment)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .OrderBy(s => s.FamilyName).ThenBy(s => s.Name)
                .ToList();
        }

        private bool CanReplace(object obj)
        {
            return SelectedFamilySymbolToReplace != null && SelectedFamilySymbol != null;
        }

        private void Replace(object obj)
        {
            var stopwatch = Stopwatch.StartNew();
            List<FamilyInstance> instancesToReplace = null;

            using (var transaction = new Transaction(_doc, "Replace Speciality Equipment"))
            {
                transaction.Start();

                instancesToReplace = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FamilyInstance))
                    .OfCategoryId(new ElementId(BuiltInCategory.OST_SpecialityEquipment))
                    .Cast<FamilyInstance>()
                    .Where(fi => fi.Symbol.Id == SelectedFamilySymbolToReplace.Id)
                    .ToList();

                if (instancesToReplace.Any())
                {
                    if (UseMassReplace)
                    {
                        // Mass replace with parameter preservation
                        MassReplaceWithParameterPreservation(instancesToReplace, SelectedFamilySymbol.Id);
                    }
                    else
                    {
                        // Individual replace with parameter preservation (old method)
                        IndividualReplaceWithParameterPreservation(instancesToReplace, SelectedFamilySymbol.Id);
                    }
                }

                transaction.Commit();
            }

            stopwatch.Stop();
            var method = UseMassReplace ? "Mass Replace" : "Individual Replace";
            OperationTime = $"{method}: {stopwatch.Elapsed.TotalSeconds:F2} seconds.";

            TaskDialog.Show("Replacement Summary",
                $"Replaced {instancesToReplace.Count} elements using {method} in {stopwatch.Elapsed.TotalSeconds:F2} seconds.");
        }



        private void IndividualReplaceWithParameterPreservation(List<FamilyInstance> instances, ElementId newTypeId)
        {
            foreach (var inst in instances)
            {
                // Individual replace using optimized snapshot method
                FamilyInstance currentInst = inst;
                OverwriteParametersWithSnapshot(ref currentInst, newTypeId);
            }
        }

        private void MassReplaceWithParameterPreservation(List<FamilyInstance> instances, ElementId newTypeId)
        {
            if (!instances.Any()) return;

            // 1. Cache parameters by name for all instances (for mass replace)
            var parameterCache = new Dictionary<ElementId, Dictionary<string, object>>();

            foreach (var inst in instances)
            {
                parameterCache[inst.Id] = CacheInstanceParametersByName(inst);
            }

            // 2. Mass replace types using Revit's batch method
            var instanceIds = instances.Select(i => i.Id).ToList();
            var resultMap = Element.ChangeTypeId(_doc, instanceIds, newTypeId);

            // 3. Restore parameters for all instances using LookupParameter
            foreach (var originalInst in instances)
            {
                if (parameterCache.TryGetValue(originalInst.Id, out var cachedParameters))
                {
                    RestoreInstanceParametersByName(originalInst, cachedParameters);
                }
            }
        }

        private Dictionary<Definition, object> CacheInstanceParameters(FamilyInstance inst)
        {
            var snapshot = new Dictionary<Definition, object>();

            foreach (Parameter p in inst.ParametersMap)
            {
                if ((p.Id.IntegerValue > 0 || p.IsShared) && !p.IsReadOnly && p.HasValue)
                {
                    snapshot[p.Definition] = GetParameterValue(p);
                }
            }

            return snapshot;
        }

        private Dictionary<string, object> CacheInstanceParametersByName(FamilyInstance inst)
        {
            var snapshot = new Dictionary<string, object>();

            foreach (Parameter p in inst.ParametersMap)
            {
                if ((p.Id.IntegerValue > 0 || p.IsShared) && !p.IsReadOnly && p.HasValue)
                {
                    snapshot[p.Definition.Name] = GetParameterValue(p);
                }
            }

            return snapshot;
        }

        private void RestoreInstanceParametersByName(FamilyInstance inst, Dictionary<string, object> cachedParameters)
        {
            foreach (var kvp in cachedParameters)
            {
                var paramName = kvp.Key;
                var val = kvp.Value;

                var p = inst.LookupParameter(paramName); // Lookup by name for mass replace
                if (p != null && !p.IsReadOnly)
                {
                    try
                    {
                        SetParameterValue(p, val);
                    }
                    catch
                    {
                        // Ignore parameter restore errors
                    }
                }
            }
        }

        private void RestoreInstanceParameters(FamilyInstance inst, Dictionary<Definition, object> cachedParameters)
        {
            foreach (var kvp in cachedParameters)
            {
                var def = kvp.Key;
                var val = kvp.Value;

                var p = inst.get_Parameter(def); // Fast access by Definition
                if (p != null && !p.IsReadOnly)
                {
                    try
                    {
                        SetParameterValue(p, val);
                    }
                    catch
                    {
                        // Ignore parameter restore errors
                    }
                }
            }
        }

        private void OverwriteParametersWithSnapshot(ref FamilyInstance inst, ElementId newSymId)
        {
            // 1. Cache parameters by name for individual replace (more reliable after ChangeTypeId)
            var snapshot = new Dictionary<string, object>();

            foreach (Parameter p in inst.ParametersMap)
            {
                if ((p.Id.IntegerValue > 0 || p.IsShared) && !p.IsReadOnly && p.HasValue)
                {
                    snapshot[p.Definition.Name] = GetParameterValue(p);
                }
            }

            // 2. Change instance type
            inst.ChangeTypeId(newSymId);

            // 3. Restore saved values using ParametersMap
            var map = inst.ParametersMap
                         .Cast<Parameter>()
                         .ToDictionary(p => p.Definition.Name);

            foreach (var kv in snapshot)
            {
                if (map.TryGetValue(kv.Key, out var p) && !p.IsReadOnly)
                {
                    try
                    {
                        SetParameterValue(p, kv.Value);
                    }
                    catch
                    {
                        // Ignore parameter restore errors
                    }
                }
            }
        }

        private object GetParameterValue(Parameter p, bool asIntegerForElementId = false)
        {
            return p.StorageType switch
            {
                StorageType.Double    => (object)p.AsDouble(),
                StorageType.Integer   => (object)p.AsInteger(),
                StorageType.String    => (object)p.AsString(),
                StorageType.ElementId => asIntegerForElementId ? (object)p.AsElementId().IntegerValue : (object)p.AsElementId(),
                _                     => null
            };
        }

        private void SetParameterValue(Parameter p, object value)
        {
            switch (p.StorageType)
            {
                case StorageType.Double:
                    if (value is double) p.Set((double)value);
                    break;
                case StorageType.Integer:
                    if (value is int) p.Set((int)value);
                    break;
                case StorageType.String:
                    if (value is string) p.Set((string)value);
                    break;
                case StorageType.ElementId:
                    if (value is ElementId) p.Set((ElementId)value);
                    else if (value is int) p.Set(new ElementId((int)value));
                    break;
            }
        }

        // Helper class for Distinct() on ElementId
        private class ElementIdComparer : IEqualityComparer<FamilySymbol>
        {
            public bool Equals(FamilySymbol x, FamilySymbol y)
            {
                return x.Id == y.Id;
            }

            public int GetHashCode(FamilySymbol obj)
            {
                return obj.Id.GetHashCode();
            }
        }
    }
}
