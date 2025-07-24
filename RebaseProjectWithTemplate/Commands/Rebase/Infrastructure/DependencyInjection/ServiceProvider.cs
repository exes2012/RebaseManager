using Autodesk.Revit.DB;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Services;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Ai;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Revit;

namespace RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.DependencyInjection;

public static class ServiceProvider
{
    public static RebaseOrchestrator CreateRebaseOrchestrator(Document doc, Document tpl)
    {
        // AI Service
        var aiService = new GeminiApiService(); // Or GrokApiService

        // Repositories
        var familyRepo = new FamilyRepository(doc, tpl);
        var viewTemplateRepo = new ViewTemplateRepository(doc, tpl);
        var viewRepo = new ViewRepository(doc, tpl);

        // Orchestrators
        var categoryRebaseOrchestrator = new CategoryRebaseOrchestrator(familyRepo, aiService);
        var viewTemplateRebaseOrchestrator = new ViewTemplateRebaseOrchestrator(viewTemplateRepo, aiService);
        var viewRebaseOrchestrator = new ViewRebaseOrchestrator(viewRepo);

        // Main Orchestrator
        var rebaseOrchestrator = new RebaseOrchestrator(
            doc,
            categoryRebaseOrchestrator,
            viewTemplateRebaseOrchestrator,
            viewRebaseOrchestrator);

        return rebaseOrchestrator;
    }
}