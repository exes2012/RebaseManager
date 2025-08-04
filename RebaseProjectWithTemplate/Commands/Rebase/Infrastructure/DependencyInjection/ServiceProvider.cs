using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Services;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Ai;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Revit;

namespace RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.DependencyInjection;

public static class ServiceProvider
{
    public static ProjectRebaseOrchestrator CreateRebaseOrchestrator(Document doc, Document tpl)
    {
        // AI Service
        var aiService = new GeminiApiService();

        // Repositories
        var familyRepo = new FamilyRepository(doc, tpl);
        var viewTemplateRepo = new ViewTemplateRepository(doc, tpl);
        var viewRepo = new ViewRepository(doc, tpl);

        // Orchestrators
        var categoryRebaseOrchestrator = new CategoryRebaseOrchestrator(familyRepo, aiService);
        var viewTemplateRebaseOrchestrator = new ViewTemplateRebaseOrchestrator(doc, tpl, aiService);
        var viewRebaseOrchestrator = new ViewRebaseOrchestrator(viewRepo);
        var elementTypeRebaseOrchestrator = new ElementTypeRebaseOrchestrator(doc, tpl, aiService);
        var schedulesRebaseOrchestrator = new SchedulesRebaseOrchestrator(doc, tpl);
        var sharedParametersRebaseOrchestrator = new SharedParametersRebaseOrchestrator(doc, tpl);

        // Main Orchestrator
        var rebaseOrchestrator = new ProjectRebaseOrchestrator(
            doc,
            categoryRebaseOrchestrator,
            viewTemplateRebaseOrchestrator,
            viewRebaseOrchestrator,
            elementTypeRebaseOrchestrator,
            schedulesRebaseOrchestrator,
            sharedParametersRebaseOrchestrator);

        return rebaseOrchestrator;
    }
}