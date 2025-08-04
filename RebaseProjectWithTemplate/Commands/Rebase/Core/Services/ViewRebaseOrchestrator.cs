using RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;
using RebaseProjectWithTemplate.Infrastructure;
using System;

namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Services;

public class ViewRebaseOrchestrator
{
    private readonly IViewRepository _viewRepo;

    public ViewRebaseOrchestrator(IViewRepository viewRepo)
    {
        _viewRepo = viewRepo;
    }

    public void RebaseDraftingViewsAndLegends(IProgress<string> progress)
    {
        try
        {
            LogHelper.Information("Starting drafting views and legends rebase");

            progress?.Report("Step 2: Replacing legends...");
            LogHelper.Information("Replacing legends");
            _viewRepo.ReplaceLegends();
            LogHelper.Information("Legends replacement completed");

            progress?.Report("Step 3: Replacing drafting views...");
            LogHelper.Information("Replacing drafting views");
            _viewRepo.ReplaceDraftingViews();
            LogHelper.Information("Drafting views replacement completed");

            LogHelper.Information("Drafting views and legends rebase completed successfully");
        }
        catch (Exception ex)
        {
            LogHelper.Error($"Failed to rebase drafting views and legends: {ex.Message}");
            progress?.Report($"Error rebasing views: {ex.Message}");
            throw;
        }
    }
}