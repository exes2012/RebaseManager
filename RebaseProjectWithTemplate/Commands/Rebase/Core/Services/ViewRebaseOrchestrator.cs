using RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;

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
        progress?.Report("Step 2: Replacing legends...");
        _viewRepo.ReplaceLegends();

        progress?.Report("Step 3: Replacing drafting views...");
        _viewRepo.ReplaceDraftingViews();
    }
}