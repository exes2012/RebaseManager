using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Microsoft.Win32;
using RebaseProjectWithTemplate.Infrastructure;
using RebaseProjectWithTemplate.UI.ViewModels.Base;

namespace RebaseProjectWithTemplate.Commands.TestRebase.UI.ViewModels
{
    public class TestRebaseViewModel : ViewModelBase
    {
        private readonly Document _doc;
        private Document _templateDoc;

        public TestRebaseViewModel(Document doc)
        {
            _doc = doc;
            BrowseTemplateCommand = new RelayCommand(BrowseTemplate);
            TestRebaseCommand = new RelayCommand(TestRebase, CanTestRebase);
        }

        private string _templatePath;
        public string TemplatePath
        {
            get => _templatePath;
            set
            {
                if (SetProperty(ref _templatePath, value))
                {
                    LoadTemplate();
                }
            }
        }

        private string _status = "Select template file to begin test";
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        private bool _isTemplateLoaded;
        public bool IsTemplateLoaded
        {
            get => _isTemplateLoaded;
            set => SetProperty(ref _isTemplateLoaded, value);
        }

        public ICommand BrowseTemplateCommand { get; }
        public ICommand TestRebaseCommand { get; }

        private void BrowseTemplate(object obj)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Revit Files (*.rvt)|*.rvt|Revit Template Files (*.rte)|*.rte",
                Title = "Select Template File"
            };

            if (dialog.ShowDialog() == true)
            {
                TemplatePath = dialog.FileName;
            }
        }

        private void LoadTemplate()
        {
            if (string.IsNullOrEmpty(TemplatePath) || !File.Exists(TemplatePath))
            {
                IsTemplateLoaded = false;
                Status = "Invalid template path";
                return;
            }

            try
            {
                Status = "Loading template...";
                
                // Close previous template if exists
                _templateDoc?.Close(false);

                // Open template document
                _templateDoc = _doc.Application.OpenDocumentFile(TemplatePath);

                IsTemplateLoaded = true;
                Status = $"Template loaded: {Path.GetFileName(TemplatePath)}";
                
                LogHelper.Information($"Template loaded successfully: {TemplatePath}");
            }
            catch (Exception ex)
            {
                IsTemplateLoaded = false;
                Status = $"Failed to load template: {ex.Message}";
                LogHelper.Error($"Failed to load template: {ex.Message}");
            }
        }

        private bool CanTestRebase(object obj)
        {
            return IsTemplateLoaded && _templateDoc != null;
        }

        private void TestRebase(object obj)
        {
            if (!CanTestRebase(obj)) return;

            var stopwatch = Stopwatch.StartNew();

            try
            {
                Status = "Running hardcoded test rebase...";

                // Hardcoded family names from the problematic case
                var sourceFamilyName = "FreeAxez-Border-Full";
                var sourceTypeName = "Gridd-70 (Straight Gap)";
                var targetFamilyName = "Access_Flooring-FreeAxez-Gridd-Border-Full";
                var targetTypeName = "Gridd-70 (Straight Gap)";

                var category = BuiltInCategory.OST_SpecialityEquipment;

                LogHelper.Information($"Testing hardcoded mapping: '{sourceFamilyName}:{sourceTypeName}' → '{targetFamilyName}:{targetTypeName}'");

                // Step 1: Copy the target family symbol from template
                Status = "Copying target family symbol from template...";

                var targetSymbolInTemplate = new FilteredElementCollector(_templateDoc)
                    .OfCategory(category)
                    .WhereElementIsElementType()
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(s => s.Family.Name == targetFamilyName && s.Name == targetTypeName);

                if (targetSymbolInTemplate == null)
                {
                    Status = $"Target symbol not found in template: {targetFamilyName}:{targetTypeName}";
                    LogHelper.Warning($"Target symbol not found in template: {targetFamilyName}:{targetTypeName}");
                    return;
                }

                // Copy the target symbol
                using (var tx = new Transaction(_doc, "copy target symbol"))
                {
                    tx.Start();
                    var opts = new CopyPasteOptions();
                    opts.SetDuplicateTypeNamesHandler(new DestTypesHandler());

                    ElementTransformUtils.CopyElements(_templateDoc, new List<ElementId> { targetSymbolInTemplate.Id }, _doc, null, opts);
                    tx.Commit();
                }

                LogHelper.Information($"Copied target symbol: {targetFamilyName}:{targetTypeName}");

                // Step 2: Find source and target symbols in project
                Status = "Finding source and target symbols...";

                var sourceSymbol = new FilteredElementCollector(_doc)
                    .OfCategory(category)
                    .WhereElementIsElementType()
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(s => s.Family.Name == sourceFamilyName && s.Name == sourceTypeName);

                var targetSymbol = new FilteredElementCollector(_doc)
                    .OfCategory(category)
                    .WhereElementIsElementType()
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(s => s.Family.Name == targetFamilyName && s.Name == targetTypeName);

                if (sourceSymbol == null)
                {
                    Status = $"Source symbol not found: {sourceFamilyName}:{sourceTypeName}";
                    LogHelper.Warning($"Source symbol not found: {sourceFamilyName}:{sourceTypeName}");
                    return;
                }

                if (targetSymbol == null)
                {
                    Status = $"Target symbol not found after copy: {targetFamilyName}:{targetTypeName}";
                    LogHelper.Warning($"Target symbol not found after copy: {targetFamilyName}:{targetTypeName}");
                    return;
                }

                // Step 3: Switch instances using mass replace
                Status = "Switching instances using mass replace...";

                var instances = new FilteredElementCollector(_doc)
                    .OfCategory(category)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .Where(inst => inst.Symbol.Id == sourceSymbol.Id)
                    .ToList();

                LogHelper.Information($"Found {instances.Count} instances to switch");

                if (instances.Any())
                {
                    // Cache parameters
                    var parameterCache = new Dictionary<ElementId, Dictionary<string, object>>();
                    foreach (var inst in instances)
                    {
                        parameterCache[inst.Id] = CacheInstanceParametersByName(inst);
                    }

                    // Mass replace
                    using (var tx = new Transaction(_doc, "mass switch instances"))
                    {
                        tx.Start();

                        var instanceIds = instances.Select(i => i.Id).ToList();
                        Element.ChangeTypeId(_doc, instanceIds, targetSymbol.Id);

                        // Restore parameters
                        foreach (var inst in instances)
                        {
                            if (parameterCache.TryGetValue(inst.Id, out var cachedParameters))
                            {
                                RestoreInstanceParametersByName(inst, cachedParameters);
                            }
                        }

                        tx.Commit();
                    }

                    LogHelper.Information($"Mass switched {instances.Count} instances from '{sourceFamilyName}:{sourceTypeName}' to '{targetFamilyName}:{targetTypeName}'");
                }

                stopwatch.Stop();
                Status = $"Test completed in {stopwatch.Elapsed.TotalSeconds:F2} seconds. Switched {instances.Count} instances.";

                LogHelper.Information($"Hardcoded test completed successfully in {stopwatch.Elapsed.TotalSeconds:F2} seconds");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Status = $"Test failed: {ex.Message}";
                LogHelper.Error($"Hardcoded test failed: {ex.Message}");
            }
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

                var p = inst.LookupParameter(paramName);
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

        private static object GetParameterValue(Parameter p)
        {
            if (!p.HasValue) return null;

            switch (p.StorageType)
            {
                case StorageType.Double: return p.AsDouble();
                case StorageType.Integer: return p.AsInteger();
                case StorageType.String: return p.AsString();
                case StorageType.ElementId: return p.AsValueString();
                default: return null;
            }
        }

        private static void SetParameterValue(Parameter p, object val)
        {
            if (val == null) return;

            try
            {
                switch (p.StorageType)
                {
                    case StorageType.Double:
                        if (val is double d) p.Set(d);
                        break;
                    case StorageType.Integer:
                        if (val is int i) p.Set(i);
                        break;
                    case StorageType.String:
                        if (val is string s) p.Set(s);
                        break;
                    case StorageType.ElementId:
                        if (val is string st) p.SetValueString(st);
                        break;
                }
            }
            catch
            {
                // Ignore parameter set errors
            }
        }

        internal class DestTypesHandler : IDuplicateTypeNamesHandler
        {
            public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
            {
                return DuplicateTypeAction.UseDestinationTypes;
            }
        }
    }
}
