# Category Rebase Excel Reporting

## Overview

The Category Rebase functionality now automatically generates detailed Excel reports for every category rebase operation. These reports provide comprehensive insights into the mapping quality and results.

## Report Location

All reports are saved to: `C:\temp\`

## File Naming Convention

Reports are named using the pattern: `{DocumentName}_{CategoryName}.xlsx`

Examples:
- `MyProject_TitleBlocks.xlsx`
- `Office_Building_GenericAnnotation.xlsx`

## Report Structure

Each Excel file contains **two worksheets**:

### 1. "Exact Matches" Sheet

Lists all families that had exact name matches between source and template:

| Column | Description |
|--------|-------------|
| Family Name | Name of the family that matched exactly |
| Type Names | Comma-separated list of all types in the family |

**Summary**: Total count of exact matches

### 2. "Mapping Results" Sheet

Detailed mapping information for all families and types:

| Column | Description |
|--------|-------------|
| Source Family | Original family name from source document |
| Target Family | Mapped family name from template (or "No Match") |
| Family Status | "Mapped" or "No Match" |
| Source Type | Original type name |
| Target Type | Mapped type name (or "No Match") |
| Type Status | "Mapped", "No Match", or "Kept Old" |

**Color Coding**:
- 🟢 **Green**: Successfully mapped types
- 🔴 **Red**: Types that couldn't be matched
- 🟡 **Yellow**: Types that kept their old values
- 🔵 **Cyan**: Template-only families (copied without mapping)

**Summary Section** includes:
- Mapped Families count
- No Match Families count
- Mapped Types count
- No Match Types count
- Kept Old Types count
- Template-Only Families count

## How to Use

### Option 1: Full Project Rebase
Use the main "Rebase Project" command - reports are generated automatically for all categories.

### Option 2: Test Individual Categories
1. Click "Test Category Rebase" button
2. Select category from dialog:
   - Title Blocks
   - Generic Annotations
   - Text Notes
   - Dimensions
3. Report is generated automatically

### Prerequisites
- Source document must be open in Revit
- Template document must be open with "Standard" or "Template" in the title
- Both documents must contain families in the selected category

## Understanding the Results

### High-Quality Mapping Indicators:
- High percentage of "Mapped" families
- Low percentage of "No Match" types
- Minimal "Kept Old" types

### Areas for Improvement:
- Many "No Match" families suggest naming inconsistencies
- High "Kept Old" types indicate incomplete mapping
- Review "Template-Only" families for missing standards

## Technical Details

The reporting system:
1. Captures all data before, during, and after the rebase operation
2. Tracks which types were actually switched vs. planned to be switched
3. Identifies template families that weren't used in mapping
4. Provides accurate status for each element

## Troubleshooting

**Report not generated?**
- Check if C:\temp directory is accessible
- Ensure both source and template documents are open
- Verify the category contains families

**Empty report?**
- Category may not contain any families
- Template document may not have families in this category

**Mapping quality issues?**
- Review family naming conventions
- Consider updating AI prompts for better matching
- Check if template families are properly organized
