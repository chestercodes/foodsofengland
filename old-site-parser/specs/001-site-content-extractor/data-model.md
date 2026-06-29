# Data Model: Website Content Extraction Pipeline

**Date**: 2026-03-31  
**Feature**: [spec.md](spec.md) | [plan.md](plan.md)

## Overview

All data is stored in a single SQLite database file. Each pipeline step reads from previous steps' tables and writes to its own. The relative file path from the source root is the primary key for most tables, establishing a chain of references across all steps.

## EF Core Entities (primitive-typed, for database layer)

### Step1ScannedFileEntity

**Table**: `Step1ScannedFiles`  
**Written by**: Step 1  
**Read by**: Steps 2, 3

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `RelativePath` | `string` | No (PK) | Relative path from source root |
| `FileHash` | `string` | No | SHA-256 hex string |
| `FileType` | `string` | No | `"html"`, `"image"`, or `"unknown"` |
| `TitleTag` | `string` | Yes | Extracted `<title>` value (HTML only) |
| `ErrorMessage` | `string` | Yes | Error description if file could not be classified |
| `IsExcluded` | `bool` | No | True if "page not found" or error file |
| `HasFilesPrefix` | `bool` | No | True if relative path starts with `files/` |
| `IsRefreshedSitePage` | `bool?` | Yes | Null for non-HTML files; true if file contains Yoast SEO plugin text (2025 refresh) |
| `HasPageNotFoundTitle` | `bool?` | Yes | Null for non-HTML files; true if title contains "page not found" |
| `LastModified` | `DateTime` | No | File last modified timestamp |

### AnalysisLogEntity

**Table**: `AnalysisLogs`  
**Written by**: Step 1  
**Read by**: Consumer (for review)

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `Id` | `int` | No (PK, auto) | Auto-increment ID |
| `Step` | `string` | No | Pipeline step that generated the log entry |
| `Category` | `string` | No | Log category (e.g. `"duplicate"`, `"files-prefix-orphan"`, `"refreshed-page-orphan"`) |
| `RelativePath` | `string` | Yes | Related file path, if applicable |
| `Message` | `string` | No | Descriptive log message |

### Step2RawContentEntity

**Table**: `Step2RawContents`  
**Written by**: Step 2  
**Read by**: Steps 3, 4

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `RelativePath` | `string` | No (PK, FK→Step1ScannedFiles) | Relative file path |
| `Content` | `string` | No | Extracted body content |
| `TemplateType` | `string` | No | `"text-start"`, `"body-text-start"`, `"page-content"`, or `"none"` |

### Step2ParsedUrlEntity

**Table**: `Step2ParsedUrls`  
**Written by**: Step 2  
**Read by**: Step 3

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `Id` | `int` | No (PK, auto) | Auto-increment ID |
| `RelativePath` | `string` | No (FK→Step2RawContents) | Source HTML file |
| `RawUrl` | `string` | No | The URL string as-is from the HTML |
| `EncodedUrl` | `string` | No | Percent-encoded normalized URL for file path comparison |
| `TagType` | `string` | No | HTML tag (e.g. `"a"`, `"img"`, `"link"`, `"script"`) |
| `AltText` | `string` | Yes | Alt text if present |
| `FileExists` | `bool?` | Yes | `true` = exists, `false` = doesn't exist, `null` = external/ambiguous |

### Step3FinalNameEntity

**Table**: `Step3FinalNames`  
**Written by**: Step 3  
**Read by**: Steps 4, 5, 6

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `RelativePath` | `string` | No (PK, FK→Step1ScannedFiles) | Original relative file path |
| `FinalName` | `string` | No | Cleaned kebab-case name (without extension) |
| `FileType` | `string` | No | `"html"`, `"image"`, or `"other"` |
| `DuplicateOf` | `string` | Yes | Original name before deduplication, null if no collision |

### Step4PreTidyMetadataEntity

**Table**: `Step4PreTidyMetadata`  
**Written by**: Step 4  
**Read by**: Step 6

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `Id` | `int` | No (PK, auto) | Auto-increment ID |
| `RelativePath` | `string` | No (FK→Step3FinalNames) | Source HTML file |
| `TagName` | `string` | No | `"class"`, `"subclass"`, or `"locale"` |
| `TextContent` | `string` | No | Extracted text content (inner text, HTML stripped) |

### Step5TidyResultEntity

**Table**: `Step5TidyResults`  
**Written by**: Step 5  
**Read by**: Step 6

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `FinalName` | `string` | No (PK) | Final file name |
| `ExitCode` | `int` | No | Tidy process exit code (0=ok, 1=warnings, 2=errors) |
| `Stdout` | `string` | No | Tidy stdout output |
| `Stderr` | `string` | No | Tidy stderr output |

### Step6PostTidyMetadataEntity

**Table**: `Step6PostTidyMetadata`  
**Written by**: Step 6  
**Read by**: Consumer

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `Id` | `int` | No (PK, auto) | Auto-increment ID |
| `RelativePath` | `string` | No (FK→Step3FinalNames) | Source HTML file |
| `MetadataKey` | `string` | No | Key name (e.g. `"created_date"`, `"source_ref"`) |
| `MetadataValue` | `string` | No | Value |

### Step6RecipeEntity

**Table**: `Step6Recipes`  
**Written by**: Step 6  
**Read by**: Consumer

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `RecipeGuid` | `string` | No (PK) | GUID from the scroll.png `id` attribute |
| `RelativePath` | `string` | No (FK→Step3FinalNames) | Source HTML file |
| `RecipeText` | `string` | No | Full recipe text (lines joined) |

## Domain Types (F# strongly-typed, for application layer)

These are single-case discriminated unions that wrap primitive values. They live in `Domain/Types.fs` and are used throughout the step logic. Conversion to/from EF entities happens in `Domain/Mapping.fs`.

```fsharp
type RelativePath = RelativePath of string
type FileHash = FileHash of string
type FinalName = FinalName of string
type RecipeGuid = RecipeGuid of string

type FileType = Html | Image | Unknown

type TemplateType = TextStart | BodyTextStart | PageContent | NoTemplate

type FileExistence = Exists | DoesNotExist | Ambiguous

type CustomTagName = Class | SubClass | Locale

type ScannedFile = {
    Path: RelativePath
    Hash: FileHash
    FileType: FileType
    TitleTag: string option
    Error: string option
    IsExcluded: bool
    HasFilesPrefix: bool
    IsRefreshedSitePage: bool option
    HasPageNotFoundTitle: bool option
    LastModified: System.DateTime
}

type PageContentRecord = {
    Path: RelativePath
    Content: string
    Template: TemplateType
}

type ParsedUrl = {
    SourcePath: RelativePath
    RawUrl: string
    EncodedUrl: string
    TagType: string
    AltText: string option
    FileExists: FileExistence option
}

type FinalNameEntry = {
    OriginalPath: RelativePath
    Name: FinalName
    FileType: FileType
    DuplicateOf: string option
}

type CustomTagValue = {
    SourcePath: RelativePath
    TagName: CustomTagName
    TextContent: string
}

type TidyResult = {
    FinalName: FinalName
    ExitCode: int
    Stdout: string
    Stderr: string
}

type ExtractedMetadata = {
    SourcePath: RelativePath
    Key: string
    Value: string
}

type Recipe = {
    Guid: RecipeGuid
    SourcePath: RelativePath
    Text: string
}
```

## Entity Relationship Diagram

```
Step1ScannedFiles (Step 1)
    |
    ├──< Step2RawContents (Step 2) [1:0..1 — HTML files only]
    |       |
    |       └──< Step2ParsedUrls (Step 2) [1:N — many URLs per page]
    |
    └──< Step3FinalNames (Step 3) [1:0..1 — non-excluded files]
            |
            ├──< Step4PreTidyMetadata (Step 4) [1:N — multiple custom tags per page]
            |
            ├──< Step5TidyResults (Step 5) [1:0..1 — one per HTML file]
            |
            ├──< Step6PostTidyMetadata (Step 6) [1:N — many metadata entries per page]
            |
            └──< Step6Recipes (Step 6) [1:N — many recipes per page]

AnalysisLogs (Step 1)              [standalone — cleared and repopulated on each run]
```
