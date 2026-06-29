# Quickstart: Website Content Extraction Pipeline

**Date**: 2026-03-31  
**Feature**: [spec.md](spec.md) | [plan.md](plan.md)

## Prerequisites

- .NET 10 SDK
- HTML Tidy CLI tool installed and on PATH (for Step 5)

## Build

```bash
cd src
dotnet build
```

## Run Tests

```bash
cd src
dotnet test
```

To skip tests that require the tidy CLI:

```bash
dotnet test --filter "Category!=RequiresTidy"
```

## Usage

```bash
# Run all steps
dotnet run --project src/OldSiteParser -- --source-folder /path/to/scraped-site --database pipeline.db --working-dir ./output

# Run a specific step
dotnet run --project src/OldSiteParser -- --source-folder /path/to/scraped-site --database pipeline.db --working-dir ./output --step 1

# Run steps 1 through 3
dotnet run --project src/OldSiteParser -- --source-folder /path/to/scraped-site --database pipeline.db --working-dir ./output --step 1 --step 2 --step 3
```

### Arguments

| Argument | Required | Description |
|----------|----------|-------------|
| `--source-folder` | Yes | Path to the root folder containing the scraped website files |
| `--database` | Yes | Path to the SQLite database file (created if it doesn't exist) |
| `--working-dir` | Yes | Path to the directory where rendered HTML files are written (Steps 4–6) |
| `--step` | No | Which step(s) to run (1–6). If omitted, all steps run in sequence. Can be specified multiple times. |

### Steps

| Step | Description |
|------|-------------|
| 1 | Scan and identify all files, report duplicates |
| 2 | Parse HTML content and extract URLs |
| 3 | Generate final file names |
| 4 | Pre-tidy rendering (URL rewriting, custom tag extraction, scroll.png GUIDs) |
| 5 | Run HTML Tidy on rendered files |
| 6 | Post-tidy cleanup, metadata extraction, recipe extraction |

## Database

The SQLite database is the handoff between steps. Each step reads from previous steps' tables and writes to its own. The database file persists across runs, allowing individual steps to be re-run.

To inspect the database:

```bash
sqlite3 pipeline.db ".tables"
sqlite3 pipeline.db "SELECT * FROM Step1ScannedFiles LIMIT 5;"
```

## Overrides

Name collision overrides are defined as a function in `src/OldSiteParser/Steps/Step3_FinalNames.fs`. Edit the `applyOverrides` function to map specific relative paths to custom final names:

```fsharp
let applyOverrides (path: RelativePath) (generatedName: FinalName) : FinalName =
    match path with
    | RelativePath "some/conflicting/file.htm" -> FinalName "my-custom-name"
    | _ -> generatedName
```
