using OfficeOpenXml;
using OfficeOpenXml.Style;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Models;
using System.Drawing;
using System.IO;

namespace RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Reporting;

public class CategoryRebaseReportService
{
    public void GenerateExcelReport(CategoryRebaseReport report)
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        
        var directoryPath = @"C:\temp";
        Directory.CreateDirectory(directoryPath);
        
        var safeDocumentName = GetSafeFileName(report.DocumentName);
        var safeCategoryName = GetSafeFileName(report.CategoryName);
        var fileName = $"{safeDocumentName}_{safeCategoryName}.xlsx";
        var filePath = Path.Combine(directoryPath, fileName);
        
        using var package = new ExcelPackage();
        
        CreateExactMatchesSheet(package, report);
        CreateMappingSheet(package, report);
        
        package.SaveAs(new FileInfo(filePath));
    }
    
    private void CreateExactMatchesSheet(ExcelPackage package, CategoryRebaseReport report)
    {
        var worksheet = package.Workbook.Worksheets.Add("Exact Matches");
        
        // Headers
        worksheet.Cells[1, 1].Value = "Family Name";
        worksheet.Cells[1, 2].Value = "Type Names";
        
        // Style headers
        using (var range = worksheet.Cells[1, 1, 1, 2])
        {
            range.Style.Font.Bold = true;
            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(Color.LightBlue);
            range.Style.Border.BorderAround(ExcelBorderStyle.Thin);
        }
        
        // Data
        var row = 2;
        foreach (var exactMatch in report.ExactMatches)
        {
            worksheet.Cells[row, 1].Value = exactMatch.FamilyName;
            worksheet.Cells[row, 2].Value = string.Join(", ", exactMatch.TypeNames);
            row++;
        }
        
        // Auto-fit columns
        worksheet.Cells.AutoFitColumns();
        
        // Add summary
        worksheet.Cells[row + 1, 1].Value = "Total Exact Matches:";
        worksheet.Cells[row + 1, 2].Value = report.ExactMatches.Count;
        worksheet.Cells[row + 1, 1, row + 1, 2].Style.Font.Bold = true;
    }
    
    private void CreateMappingSheet(ExcelPackage package, CategoryRebaseReport report)
    {
        var worksheet = package.Workbook.Worksheets.Add("Mapping Results");
        
        // Headers
        worksheet.Cells[1, 1].Value = "Source Family";
        worksheet.Cells[1, 2].Value = "Target Family";
        worksheet.Cells[1, 3].Value = "Family Status";
        worksheet.Cells[1, 4].Value = "Source Type";
        worksheet.Cells[1, 5].Value = "Target Type";
        worksheet.Cells[1, 6].Value = "Type Status";
        
        // Style headers
        using (var range = worksheet.Cells[1, 1, 1, 6])
        {
            range.Style.Font.Bold = true;
            range.Style.Fill.PatternType = ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(Color.LightGreen);
            range.Style.Border.BorderAround(ExcelBorderStyle.Thin);
        }
        
        var row = 2;
        
        // Family mappings
        foreach (var familyMapping in report.FamilyMappings)
        {
            if (familyMapping.TypeMappings.Any())
            {
                foreach (var typeMapping in familyMapping.TypeMappings)
                {
                    worksheet.Cells[row, 1].Value = familyMapping.SourceFamily;
                    worksheet.Cells[row, 2].Value = familyMapping.TargetFamily;
                    worksheet.Cells[row, 3].Value = familyMapping.MappingStatus;
                    worksheet.Cells[row, 4].Value = typeMapping.SourceType;
                    worksheet.Cells[row, 5].Value = typeMapping.TargetType;
                    worksheet.Cells[row, 6].Value = typeMapping.MappingStatus;
                    
                    // Color coding for type status
                    var typeStatusCell = worksheet.Cells[row, 6];
                    switch (typeMapping.MappingStatus)
                    {
                        case "Mapped":
                            typeStatusCell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                            typeStatusCell.Style.Fill.BackgroundColor.SetColor(Color.LightGreen);
                            break;
                        case "No Match":
                            typeStatusCell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                            typeStatusCell.Style.Fill.BackgroundColor.SetColor(Color.LightCoral);
                            break;
                        case "Kept Old":
                            typeStatusCell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                            typeStatusCell.Style.Fill.BackgroundColor.SetColor(Color.LightYellow);
                            break;
                    }
                    
                    row++;
                }
            }
            else
            {
                // Family without types
                worksheet.Cells[row, 1].Value = familyMapping.SourceFamily;
                worksheet.Cells[row, 2].Value = familyMapping.TargetFamily;
                worksheet.Cells[row, 3].Value = familyMapping.MappingStatus;
                worksheet.Cells[row, 4].Value = "";
                worksheet.Cells[row, 5].Value = "";
                worksheet.Cells[row, 6].Value = "";
                row++;
            }
        }
        
        // Template-only families
        if (report.TemplateOnlyFamilies.Any())
        {
            row++; // Empty row
            worksheet.Cells[row, 1].Value = "Template-Only Families:";
            worksheet.Cells[row, 1].Style.Font.Bold = true;
            row++;
            
            foreach (var templateFamily in report.TemplateOnlyFamilies)
            {
                worksheet.Cells[row, 1].Value = "";
                worksheet.Cells[row, 2].Value = templateFamily.FamilyName;
                worksheet.Cells[row, 3].Value = "Template Only";
                worksheet.Cells[row, 4].Value = string.Join(", ", templateFamily.Types.Select(t => t.TypeName));
                worksheet.Cells[row, 5].Value = "";
                worksheet.Cells[row, 6].Value = "";
                
                // Color coding for template-only
                using (var range = worksheet.Cells[row, 2, row, 3])
                {
                    range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    range.Style.Fill.BackgroundColor.SetColor(Color.LightCyan);
                }
                
                row++;
            }
        }
        
        // Auto-fit columns
        worksheet.Cells.AutoFitColumns();
        
        // Add summary
        row += 2;
        worksheet.Cells[row, 1].Value = "Summary:";
        worksheet.Cells[row, 1].Style.Font.Bold = true;
        row++;
        
        var mappedFamilies = report.FamilyMappings.Count(f => f.MappingStatus == "Mapped");
        var noMatchFamilies = report.FamilyMappings.Count(f => f.MappingStatus == "No Match");
        var mappedTypes = report.FamilyMappings.SelectMany(f => f.TypeMappings).Count(t => t.MappingStatus == "Mapped");
        var noMatchTypes = report.FamilyMappings.SelectMany(f => f.TypeMappings).Count(t => t.MappingStatus == "No Match");
        var keptOldTypes = report.FamilyMappings.SelectMany(f => f.TypeMappings).Count(t => t.MappingStatus == "Kept Old");
        
        worksheet.Cells[row, 1].Value = "Mapped Families:";
        worksheet.Cells[row, 2].Value = mappedFamilies;
        row++;
        worksheet.Cells[row, 1].Value = "No Match Families:";
        worksheet.Cells[row, 2].Value = noMatchFamilies;
        row++;
        worksheet.Cells[row, 1].Value = "Mapped Types:";
        worksheet.Cells[row, 2].Value = mappedTypes;
        row++;
        worksheet.Cells[row, 1].Value = "No Match Types:";
        worksheet.Cells[row, 2].Value = noMatchTypes;
        row++;
        worksheet.Cells[row, 1].Value = "Kept Old Types:";
        worksheet.Cells[row, 2].Value = keptOldTypes;
        row++;
        worksheet.Cells[row, 1].Value = "Template-Only Families:";
        worksheet.Cells[row, 2].Value = report.TemplateOnlyFamilies.Count;
    }
    
    private string GetSafeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
    }
}
