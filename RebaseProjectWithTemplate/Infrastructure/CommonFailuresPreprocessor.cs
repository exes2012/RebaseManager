using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;

namespace RebaseProjectWithTemplate.Infrastructure
{
    public class CommonFailuresPreprocessor : IFailuresPreprocessor
    {
        public static void SetFailuresPreprocessor(Transaction transaction)
        {
            var options = transaction.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(new CommonFailuresPreprocessor());
            transaction.SetFailureHandlingOptions(options);
        }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            var failureMessages = failuresAccessor.GetFailureMessages();
            var hasErrors = false;

            foreach (var failureMessage in failureMessages)
            {
                var severity = failureMessage.GetSeverity();
                if (severity == FailureSeverity.Warning)
                {
                    HandleWarning(failuresAccessor, failureMessage);
                }
                else if (severity == FailureSeverity.Error)
                {
                    hasErrors = HandleError(failuresAccessor, failureMessage);
                }
            }

            return hasErrors ? FailureProcessingResult.ProceedWithCommit : FailureProcessingResult.Continue;
        }

        private void HandleWarning(FailuresAccessor failuresAccessor, FailureMessageAccessor failureMessage)
        {
            LogHelper.Warning($"WARNING SWALLOWED: {failureMessage.GetDescriptionText()}, " +
                              $"failing elements: {string.Join(", ", failureMessage.GetFailingElementIds())}");

            failuresAccessor.DeleteWarning(failureMessage);
        }

        private bool HandleError(FailuresAccessor failuresAccessor, FailureMessageAccessor failureMessage)
        {
            var definitionId = failureMessage.GetFailureDefinitionId();
            var description = failureMessage.GetDescriptionText();
            var elementIds = failureMessage.GetFailingElementIds();
            var resolution = GetNextResolution(failuresAccessor, failureMessage);

            if (resolution == FailureResolutionType.Invalid)
            {
                LogHelper.Information($"WARNING SWALLOWED: Unable to resolve " +
                                      $"{FailureSeverity.Error} {definitionId} {description}, trying to delete");
                failuresAccessor.DeleteWarning(failureMessage);
                return false;
            }

            LogHelper.Information($"WARNING SWALLOWED: Attempting to resolve failure with severity 'Error', " +
                                  $"definition ID '{definitionId.Guid}', resolution '{resolution}', " +
                                  $"description '{description}', failing elements: {string.Join(", ", elementIds.Select(id => id.IntegerValue))}");

            failureMessage.SetCurrentResolutionType(resolution);
            failuresAccessor.ResolveFailure(failureMessage);
            return true;
        }

        private FailureResolutionType GetNextResolution(
            FailuresAccessor failuresAccessor, FailureMessageAccessor failureMessageAccessor)
        {
            var suitableResolutions = new List<FailureResolutionType>()
            {
                FailureResolutionType.Default,
                FailureResolutionType.MoveElements,
                FailureResolutionType.SetValue,
                FailureResolutionType.UnlockConstraints,
                FailureResolutionType.DetachElements,
                FailureResolutionType.SkipElements,
                FailureResolutionType.Others,
                FailureResolutionType.DeleteElements,
            };

            var usedResolutions = failuresAccessor
                .GetAttemptedResolutionTypes(failureMessageAccessor)
                .ToList();

            foreach (var resolution in suitableResolutions)
            {
                if (!usedResolutions.Contains(resolution) && failureMessageAccessor.HasResolutionOfType(resolution))
                {
                    return resolution;
                }
            }

            return FailureResolutionType.Invalid;
        }
    }
}
