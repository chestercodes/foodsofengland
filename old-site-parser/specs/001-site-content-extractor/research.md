# Research: Website Content Extraction Pipeline

**Date**: 2026-03-31  
**Feature**: [spec.md](spec.md) | [plan.md](plan.md)

## R-001: F# with EF Core on .NET 10

**Decision**: Use EF Core 10 (LTS) with the SQLite provider, configured from F#.

**Rationale**: EF Core 10 is a Long Term Support release (supported until Nov 2028) and runs on .NET 10. EF Core's DbContext and entity types are defined using C#-style classes, which F# supports via `[<CLIMutable>]` attributed record types or plain classes. The SQLite provider (`Microsoft.EntityFrameworkCore.Sqlite`) is mature and well-tested. F# can call EF Core's fluent API in `OnModelCreating` without issues. Migrations are not needed — the database is created at runtime via `EnsureCreated()` since this is a local tool with a disposable database.

**Alternatives considered**:
- **Dapper** — lighter weight but requires manual SQL; EF Core provides schema generation and change tracking which simplifies the step-by-step workflow.
- **ADO.NET directly** — too verbose for the number of tables involved.
- **SQLitePCLRaw** — too low-level.

**Key notes for F# + EF Core**:
- Entity types should be declared as F# classes (not records) with mutable properties for EF Core compatibility, using primitive types (string, int, bool, etc.)
- Domain types (strongly-typed wrappers like `RelativePath`, `FileHash`, `FinalName`) live in a separate Domain module and map to/from EF entities at the boundary
- Queries use LINQ via F# query expressions or method syntax

## R-002: HTML Parsing with HtmlAgilityPack

**Decision**: Use HtmlAgilityPack (HAP) 1.12.4 for all HTML parsing tasks.

**Rationale**: HAP is the most widely-used .NET HTML parser (307M+ NuGet downloads), targets .NET Standard 2.0 (fully compatible with .NET 10), and is specifically designed to be "very tolerant of real world malformed HTML." This is critical since the source HTML is hand-written by a non-developer with unclosed tags, non-standard custom tags, and inconsistent markup. HAP builds a read/write DOM and supports XPath queries.

**Alternatives considered**:
- **AngleSharp** — stricter parser that more closely follows the HTML5 spec. Could reject or rewrite some of the malformed markup. HAP's tolerance is a better fit.
- **Regex-only** — too fragile for real HTML parsing. However, regex will be used for specific well-defined patterns (comment markers, custom tag extraction, `scroll.png` line modification) where the patterns are simple and well-understood.

**Usage approach**:
- Use HAP for: extracting `<title>` tags, extracting URLs from parsed content (finding `<a>`, `<img>`, `<link>`, `<script>` tags and their href/src attributes), and general DOM navigation
- Use regex for: locating comment markers (`<!---TEXT START--->` etc.), extracting/replacing custom tags (`<class>`, `<subclass>`, `<locale>`), modifying `scroll.png` lines, and user-defined transformations

## R-003: Custom Tag Parsing Strategy

**Decision**: Use regex-based extraction for custom tags (`<class>`, `<subclass>`, `<locale>`) and Whimberry CMS tags (`<W-*>`).

**Rationale**: The custom tags are non-standard HTML that HAP may not parse predictably (it might mangle or reinterpret them). The tags follow predictable patterns with a limited number of variants:
- `<class>...<class>` (doubled opener as closer)
- `<class>...</class>` (proper closing)
- `<locale><i>text</locale></i>` (misplaced nesting)
- `<subclass><i>text</i></subclass>` (proper nesting)

Regex can handle all these patterns with a few alternations. The content within the tags may contain HTML links (`<a href="...">`), which need the link URL extracted separately.

**Regex patterns** (to be refined during implementation):
- `<(class|subclass|locale)>(.*?)<(?:/?\1|(?=\1))>` with appropriate flags for the doubled-opener variant
- `<W-[^>]*>` for Whimberry CMS tags
- Special handling for the `</i>` trailing after `</locale>` in the misplaced nesting case

**Replacement**: Each custom tag block is replaced with `<!-- {tagname}: {text_content} -->`.

## R-004: HTML Tidy CLI Integration

**Decision**: Shell out to the `tidy` command-line tool using `System.Diagnostics.Process`.

**Rationale**: HTML Tidy is a standalone executable. There is no maintained .NET binding. Shelling out is the standard approach. Each file is processed independently, so failures in one file do not affect others.

**Approach**:
- Create a `Process` with `tidy` as the executable
- Pass the input file path and configuration flags as arguments
- Capture stdout, stderr, and exit code
- Store results in the database
- Tidy exit codes: 0 = no warnings/errors, 1 = warnings only, 2 = errors. All are captured; exit code 2 is flagged for inspection.

**Alternatives considered**:
- **LibTidy .NET bindings** — no actively maintained packages for .NET 10.
- **AngleSharp as a normaliser** — different output characteristics; the user specifically wants the tidy CLI tool.

## R-005: File Hashing

**Decision**: Use SHA-256 via `System.Security.Cryptography.SHA256.HashData()` with streaming.

**Rationale**: SHA-256 is efficient, collision-resistant for this use case, and available in the BCL without additional packages. The `HashData` method on .NET 10 accepts a `Stream`, allowing files to be hashed without loading entirely into memory — important for large files. The hash is stored as a hex string.

**Alternatives considered**:
- **MD5** — faster but weaker collision resistance. Not needed since we're just detecting duplicates in a local dataset.
- **xxHash** — faster still, but requires an extra package and SHA-256 is fast enough for thousands of files.

## R-006: Domain Types in F#

**Decision**: Use single-case discriminated unions for strongly-typed domain values.

**Rationale**: F# single-case DUs (e.g. `type RelativePath = RelativePath of string`) provide compile-time safety to prevent mixing up different string values (file paths, hashes, final names, URLs). They have zero runtime overhead and are idiomatic F#. At the EF Core boundary, mapping functions convert between primitives and domain types.

**Key types**:
- `RelativePath` — relative file path from root folder
- `FileHash` — SHA-256 hex string
- `FinalName` — the cleaned kebab-case name
- `RecipeGuid` — the GUID assigned to scroll.png images

## R-007: Testing Strategy

**Decision**: xUnit with FsUnit for F# assertion syntax. In-memory SQLite for database tests. Example HTML files from `example-html/` as test fixtures.

**Rationale**: xUnit is the standard .NET test framework, and FsUnit provides idiomatic F# assertion syntax (`result |> should equal expected`). In-memory SQLite (`Data Source=:memory:`) provides fast, isolated database tests without file I/O. The real example HTML files already in the repository serve as realistic test fixtures.

**Test structure**:
- **Unit tests per step**: Each step module gets its own test file testing the core logic with known inputs/outputs
- **Shared utility tests**: Kebab-case conversion, HTML parsing helpers
- **Integration tests**: End-to-end pipeline test using a small set of example HTML files, verifying the database state after each step
- **Tidy step tests**: May require the tidy CLI to be available; marked with a trait/category so they can be skipped in environments without tidy installed

**Alternatives considered**:
- **Expecto** — F#-native test framework, but xUnit has broader tooling support (VS Test Explorer, CI integrations).
- **NUnit** — similar to xUnit; xUnit chosen for consistency with broader .NET ecosystem.

## R-008: CLI Argument Parsing

**Decision**: Use `System.CommandLine` for parsing CLI arguments.

**Rationale**: `System.CommandLine` is the standard .NET library for CLI parsing. The app needs to accept: a root folder path, a database file path, which step(s) to run, and a working directory path. `System.CommandLine` handles argument validation, help text generation, and subcommand routing.

**Alternatives considered**:
- **Argu** — F#-specific CLI parsing library using DUs. Elegant F# API but less widely supported and documented than `System.CommandLine`.
- **Manual parsing** — viable for simple args but doesn't scale well as options grow.

## R-009: Recipe Boundary Detection

**Decision**: Use a two-marker approach: coloured `<hr>` + `scroll.png` for recipe start, plain `<hr>` for recipe end.

**Rationale**: From the example HTML files, recipe blocks consistently follow this pattern:
1. Start: `<hr color="#B81010"><img src="scroll.png" border="0">` followed by source attribution
2. End: plain `<hr>` (no colour attribute) or the start of the next recipe

The `scroll.png` image gets a unique GUID `id` attribute added in Step 4. After tidy, the GUID survives as an attribute on the `<img>` tag. Recipe text is everything between the scroll image line and the next plain `<hr>`.

Pages like cookbook transcriptions (`book1658compleat.htm`) have no `scroll.png` markers and will not produce recipe records.

## R-010: Metadata Extraction

**Decision**: Defer specific metadata extraction logic to implementation, but store as key-value pairs.

**Rationale**: The spec identifies metadata as generic key-value pairs that can be pulled from the content. Known sources of metadata include:
- Custom tags: `<class>` → class category, `<subclass>` → subcategory, `<locale>` → region (already extracted in Step 4, stored as `CustomTagValue`)
- Potential additional metadata: page creation date (from `<!--New Page created on...-->` comments), source references, unverified status markers (`[Unverified]`)

The `ExtractedMetadata` table stores (file_reference, key, value) triples, allowing multiple per file and multiple with the same key. The specific extractors will be implemented as a list of functions that each scan the content and return key-value pairs.

## R-011: Within-Step Parallelism via .NET Channels

**Decision**: Use `System.Threading.Channels` with a bounded channel to parallelise per-file I/O within each pipeline step, while serialising all SQLite database writes through a single consumer thread.

**Rationale**: Each pipeline step iterates over hundreds to thousands of files, performing I/O-bound work (file reading, hashing, HTML parsing, external process execution) that is independent per file. Parallelising this work across `Environment.ProcessorCount` threads significantly reduces wall-clock time. However, SQLite only supports a single writer at a time, and EF Core's `DbContext` is not thread-safe. The bounded channel pattern cleanly separates these concerns:

1. **Producers** (parallel, bounded by semaphore): Process files on thread pool threads. Each producer writes its result (entities) into the channel.
2. **Consumer** (single-threaded): Reads from the channel and performs all `DbContext.Add()` calls sequentially, followed by a single `SaveChanges()` after all items are processed.

This pattern is implemented in `Shared/ChannelProcessing.fs` as a reusable `runParallel` function used by Steps 1, 2, 4, 5, and 6. Step 3 (name generation) is not parallelised because its deduplication logic requires sequential access to all generated names.

**Alternatives considered**:
- **Parallel.ForEachAsync** — simpler API but doesn't naturally separate the DB write path; would require `ConcurrentBag` or similar, adding complexity.
- **Task.WhenAll with ConcurrentBag** — collects all results first, writes after. Simpler but loses streaming behaviour (all results buffered in memory before any DB writes begin).
- **Multiple DbContext instances** — one per thread. Works for some databases but SQLite still serialises writes internally, adding overhead without benefit.
