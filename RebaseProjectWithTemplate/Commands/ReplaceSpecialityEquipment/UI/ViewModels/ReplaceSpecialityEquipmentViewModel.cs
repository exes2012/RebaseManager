
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

        private bool _overwriteParametersLookup;
        public bool OverwriteParametersLookup
        {
            get => _overwriteParametersLookup;
            set
            {
                if (SetProperty(ref _overwriteParametersLookup, value) && value)
                {
                    OverwriteParametersSnapshot = false;
                }
            }
        }

        private bool _overwriteParametersSnapshot;
        public bool OverwriteParametersSnapshot
        {
            get => _overwriteParametersSnapshot;
            set
            {
                if (SetProperty(ref _overwriteParametersSnapshot, value) && value)
                {
                    OverwriteParametersLookup = false;
                }
            }
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

                foreach (var inst in instancesToReplace)
                {
                    ElementId newSymId = SelectedFamilySymbol.Id;

                    if (OverwriteParametersLookup)
                    {
                        // Need a temporary variable for the ref parameter in OverwriteParametersWithLookup
                        FamilyInstance currentInst = inst;
                        OverwriteParametersWithLookup(ref currentInst, newSymId);
                    }
                    else if (OverwriteParametersSnapshot)
                    {
                        // Need a temporary variable for the ref parameter in OverwriteParametersWithSnapshot
                        FamilyInstance currentInst = inst;
                        OverwriteParametersWithSnapshot(ref currentInst, newSymId);
                    }
                    else // No overwrite selected, just change symbol
                    {
                        inst.ChangeTypeId(newSymId);
                    }
                }
                
                transaction.Commit();
            }

            stopwatch.Stop();
            OperationTime = $"Operation completed in {stopwatch.Elapsed.TotalSeconds:F2} seconds.";

            TaskDialog.Show("Replacement Summary",
                $"Replaced {instancesToReplace.Count} elements in {stopwatch.Elapsed.TotalSeconds:F2} seconds.");
        }

        private void OverwriteParametersWithLookup(ref FamilyInstance inst, ElementId newSymId)
        {
            var parameterValues = new Dictionary<string, object>();
            foreach (Parameter p in inst.ParametersMap)
            {
                if ((p.Id.IntegerValue > 0 || p.IsShared))
                {
                    if (p.HasValue && !p.IsReadOnly)
                    {
                        parameterValues[p.Definition.Name] = GetParameterValue(p);
                    }
                }
            }

            inst.ChangeTypeId(newSymId);

            foreach (var pair in parameterValues)
            {
                var newParam = inst.LookupParameter(pair.Key);
                if (newParam != null && !newParam.IsReadOnly)
                {
                    SetParameterValue(newParam, pair.Value);
                }
            }
        }

        private void OverwriteParametersWithSnapshot(ref FamilyInstance inst, ElementId newSymId)
        {
            // 1. Снимаем значения всех изменяемых параметров
            var snapshot = new Dictionary<Definition, object>();

            foreach (Parameter p in inst.Parameters)               // один линейный проход
            {
                if ((p.Id.IntegerValue > 0 || p.IsShared) && !p.IsReadOnly && p.HasValue)
                {
                    snapshot[p.Definition] = GetParameterValue(p);
                }
            }

            // 2. Меняем тип экземпляра
            if (inst.ChangeTypeId(newSymId) == ElementId.InvalidElementId)
                return;                                            // если не удалось — выходим

            // 3. Возвращаем сохранённые значения
            foreach (KeyValuePair<Definition, object> kvp in snapshot)
            {
                Definition def = kvp.Key;
                object val = kvp.Value;

                Parameter p = inst.get_Parameter(def);             // быстрый доступ по Definition
                if (p != null && !p.IsReadOnly)
                {
                    SetParameterValue(p, val);
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
