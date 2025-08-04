using System.Reflection;
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using RebaseProjectWithTemplate.Infrastructure;

namespace RebaseProjectWithTemplate;

public class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        LogHelper.Initialize();
        RevitApplicationContext.Initialize(application);

        // Регистрируем глобальный обработчик ошибок
        application.ControlledApplication.FailuresProcessing += GlobalFailureHandler;
        LogHelper.Information("Global failure handler registered at application startup");

        var assemblyName = Assembly.GetExecutingAssembly().Location;
        var tabName = "Revit Tools";
        application.CreateRibbonTab(tabName);

        var panel = application.CreateRibbonPanel(tabName, "Rebase Tool");

        var buttonData = new PushButtonData(
            "RebaseProject",
            "Rebase Project",
            assemblyName,
            "RebaseProjectWithTemplate.Commands.Rebase.RebaseCommand");

        panel.AddItem(buttonData);

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        // Отписываемся от глобального обработчика ошибок
        application.ControlledApplication.FailuresProcessing -= GlobalFailureHandler;
        LogHelper.Information("Global failure handler unregistered at application shutdown");

        return Result.Succeeded;
    }

    private void GlobalFailureHandler(object sender, FailuresProcessingEventArgs e)
    {
        FailuresAccessor failuresAccessor = e.GetFailuresAccessor();
        IList<FailureMessageAccessor> failures = failuresAccessor.GetFailureMessages();
        bool hasCriticalError = false;

        if (failures.Any())
        {
            foreach (var failure in failures)
            {
                var failureSeverity = failure.GetSeverity();

                LogHelper.Information($"Global Failure Handler - Failure: {failure.GetDescriptionText()}, Severity: {failureSeverity}, " +
                                      $"FailureDefinitionId: {failure.GetFailureDefinitionId()}, " +
                                      $"Failing Elements: {string.Join(", ", failure.GetFailingElementIds())}");

                if (failureSeverity == FailureSeverity.Warning)
                {
                    if (failure.HasResolutions())
                    {
                        LogHelper.Information($"Attempting to resolve warning: {failure.GetDescriptionText()}");
                        try
                        {
                            failuresAccessor.ResolveFailure(failure);
                            LogHelper.Information($"Resolved warning: {failure.GetDescriptionText()}");
                        }
                        catch (Exception ex)
                        {
                            LogHelper.Error($"Failed to resolve warning: {failure.GetDescriptionText()}. Error: {ex.Message}");
                            failuresAccessor.DeleteWarning(failure);
                        }
                    }
                    else
                    {
                        LogHelper.Warning($"Deleting warning without resolution: {failure.GetDescriptionText()}");
                        failuresAccessor.DeleteWarning(failure);
                    }
                }
                else if (failure.HasResolutions())
                {
                    LogHelper.Information($"Attempting to resolve failure: {failure.GetDescriptionText()}");
                    try
                    {
                        failuresAccessor.ResolveFailure(failure);
                        LogHelper.Information($"Resolved failure: {failure.GetDescriptionText()}");
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Error($"Failed to resolve failure: {failure.GetDescriptionText()}. Error: {ex.Message}");
                    }
                }
                else if (failureSeverity == FailureSeverity.Error)
                {
                    LogHelper.Error($"Unresolvable error: {failure.GetDescriptionText()} " +
                                    $"FOR ELEMENTS: {string.Join(", ", failure.GetFailingElementIds())}");
                    hasCriticalError = true;
                }
            }
        }

        if (hasCriticalError)
        {
            FailureHandlingOptions options = failuresAccessor.GetFailureHandlingOptions();
            options.SetClearAfterRollback(true);
            failuresAccessor.SetFailureHandlingOptions(options);

            e.SetProcessingResult(FailureProcessingResult.ProceedWithRollBack);
        }
        else
        {
            e.SetProcessingResult(FailureProcessingResult.Continue);
        }
    }
}