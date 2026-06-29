# Implementation Plan: Website Content Extraction Pipeline

**Branch**: `001-site-content-extractor` | **Date**: 2026-03-31 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-site-content-extractor/spec.md`

## Summary

A 6-step console pipeline written in F# on .NET 10 that processes a folder of scraped website files (foodsofengland.co.uk), identifies and catalogues all files, extracts HTML body content and URLs, generates clean kebab-case file names, renders pre-tidy HTML with custom tag replacement, runs HTML Tidy for normalisation, and extracts metadata and recipes. All intermediate state is persisted to SQLite via EF Core, allowing each step to be run independently.

## Technical Context

**Language/Version**: F# on .NET 10  
**Primary Dependencies**: EF Core (SQLite provider), HtmlAgilityPack (lenient HTML parsing), System.CommandLine (CLI argument parsing), System.Threading.Channels (parallel processing with sequential DB writes)  
**Storage**: SQLite via EF Core  
**Testing**: xUnit with FsUnit assertions; unit tests per step, integration tests for full pipeline  
**Target Platform**: Cross-platform .NET 10 console application (Windows primary)  
**Project Type**: CLI  
**Performance Goals**: Process thousands of files without excessive memory usage; stream where possible rather than loading entire files into memory; parallelise per-file I/O within each step using bounded channels with sequential DB writes to respect SQLite's single-writer constraint  
**Constraints**: Must handle malformed/non-standard HTML; must not load all files into memory simultaneously; all SQLite write operations must be serialised (one at a time) due to SQLite's concurrency model  
**Scale/Scope**: ~3,000+ HTML pages, associated images, single user, local execution

## Constitution Check

*No constitution file found. Gate check skipped.*

## Project Structure

### Documentation (this feature)

```text
specs/001-site-content-extractor/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
src/
├── OldSiteParser/                    # Main console application
│   ├── Program.fs                    # Entry point, CLI arg parsing
│   ├── Database/
│   │   ├── Context.fs                # EF Core DbContext
│   │   └── Entities.fs               # EF Core entity types (primitive-typed)
│   ├── Domain/
│   │   ├── Types.fs                  # Strongly-typed domain types (RelativePath, FileHash, FinalName, etc.)
│   │   └── Mapping.fs                # Mapping between EF entities and domain types
│   ├── Steps/
│   │   ├── Step1_IdentifyFiles.fs    # File scanning, hashing, classification
│   │   ├── Step2_ParseHtml.fs        # Content extraction, URL parsing
│   │   ├── Step3_FinalNames.fs       # Name generation, deduplication, overrides
│   │   ├── Step4_PreTidy.fs          # Rendering, URL rewriting, custom tag extraction
│   │   ├── Step5_Tidy.fs             # Run HTML Tidy, capture output
│   │   └── Step6_Extract.fs          # Post-tidy transforms, metadata & recipe extraction
│   └── Shared/
│       ├── Hashing.fs                # File hashing utilities
│       ├── HtmlParsing.fs            # Shared HTML parsing helpers (lenient)
│       ├── Kebab.fs                  # Kebab-case conversion, common-part stripping
│       ├── Logging.fs                # Error/warning logging utilities
│       └── ChannelProcessing.fs      # Bounded channel parallel processing with sequential consumption
│
└── OldSiteParser.Tests/              # Test project
    ├── Steps/
    │   ├── Step1Tests.fs
    │   ├── Step2Tests.fs
    │   ├── Step3Tests.fs
    │   ├── Step4Tests.fs
    │   ├── Step5Tests.fs
    │   └── Step6Tests.fs
    ├── Integration/
    │   └── PipelineTests.fs          # End-to-end pipeline tests
    ├── Shared/
    │   ├── KebabTests.fs
    │   └── HtmlParsingTests.fs
    └── TestData/                     # Sample HTML files for tests
        ├── african-cakes.htm
        ├── apple-hearthbread.htm
        └── ...
```

**Structure Decision**: Single solution with two projects — a console app and a test project. The console app uses a `Domain/` layer with strongly-typed F# types, a `Database/` layer with EF Core primitive-typed entities, and a `Steps/` layer with one module per pipeline step. Tests mirror the step structure with per-step unit tests and a separate integration test module.

## Complexity Tracking

No constitution violations — section not applicable.
