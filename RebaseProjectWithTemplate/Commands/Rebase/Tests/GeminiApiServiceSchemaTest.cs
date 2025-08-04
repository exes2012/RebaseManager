using System;
using System.Collections.Generic;
using RebaseProjectWithTemplate.Commands.Rebase.DTOs;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Ai;
using System.Reflection;

namespace RebaseProjectWithTemplate.Commands.Rebase.Tests;

/// <summary>
/// Simple test to verify GeminiApiService schema generation logic
/// </summary>
public static class GeminiApiServiceSchemaTest
{
    public static void TestSchemaGeneration()
    {
        var service = new GeminiApiService();
        
        // Use reflection to access private method
        var method = typeof(GeminiApiService).GetMethod("GetResponseSchema", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        
        if (method == null)
        {
            Console.WriteLine("ERROR: GetResponseSchema method not found");
            return;
        }

        // Test ViewTemplateMappingResponse schema
        var genericMethod = method.MakeGenericMethod(typeof(ViewTemplateMappingResponse));
        var viewTemplateSchema = genericMethod.Invoke(service, null);
        
        Console.WriteLine("ViewTemplateMappingResponse schema generated successfully");
        Console.WriteLine($"Schema type: {viewTemplateSchema.GetType().Name}");

        // Test List<MappingResult> schema  
        genericMethod = method.MakeGenericMethod(typeof(List<MappingResult>));
        var mappingResultSchema = genericMethod.Invoke(service, null);
        
        Console.WriteLine("List<MappingResult> schema generated successfully");
        Console.WriteLine($"Schema type: {mappingResultSchema.GetType().Name}");

        // Verify they are different objects
        if (viewTemplateSchema != mappingResultSchema)
        {
            Console.WriteLine("SUCCESS: Different schemas generated for different types");
        }
        else
        {
            Console.WriteLine("ERROR: Same schema object returned for different types");
        }
    }
}
