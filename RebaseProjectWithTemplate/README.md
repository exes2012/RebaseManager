# Revit Rebase Project With Template

A Revit add-in for rebasing projects with template standardization using AI-powered view template mapping.

## Setup

### 1. Configure API Key

1. Copy `appsettings.example.json` to `appsettings.json`
2. Replace `YOUR_GROK_API_KEY_HERE` with your actual Grok API key from x.ai
3. The `appsettings.json` file is automatically excluded from git commits

### 2. Build and Install

1. Build the project for your Revit version (e.g., "Debug 2023")
2. The add-in will be automatically installed via the `.addin` file

## Features

### Step 1: View Template Rebase
- AI-powered semantic mapping of view templates using Grok-3-mini
- 1-to-1 mapping with no duplicates
- Automatic deletion of old templates and copying of new ones
- Filters views by numeric View Size parameter

### Step 2: Legend Replacement
- Complete replacement of legends from template project
- Preserves first legend as template for duplication
- Copies all content and properties

### Step 3: Drafting View Replacement
- Complete replacement of drafting views from template project
- Sets View Size, View Category, and Category parameters
- Copies all annotations and content

## Usage

1. Open Revit and load both source and template projects
2. Launch the "Rebase Project With Template" command
3. Select source project (to be modified)
4. Select template project (standardized templates)
5. Click "Rebase Project" and monitor progress

## Architecture

- `ProjectRebaseService` - Main orchestrator for all rebase steps
- `ViewTemplateRebaseService` - Handles view template mapping and replacement
- `ViewReplacementService` - Handles legend and drafting view replacement
- `GrokApiService` - AI-powered template mapping via Grok API
- `ViewCopyService` - Utilities for copying view content and annotations
- `PromptService` - Centralized prompt management for AI requests
- `ConfigurationService` - Secure API key management

## Security

- API keys are stored in `appsettings.json` (excluded from git)
- No hardcoded credentials in source code
- Configuration file is copied to output directory during build

## Requirements

- Revit 2020-2026 support
- .NET Framework 4.8 (Revit 2020-2024) or .NET 8.0 (Revit 2025-2026)
- Valid Grok API key from x.ai
