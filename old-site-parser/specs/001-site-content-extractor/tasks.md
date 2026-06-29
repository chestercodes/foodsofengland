# Tasks: Website Content Extraction Pipeline

**Input**: Design documents from `/specs/001-site-content-extractor/`
**Prerequisites**: plan.md ✓, spec.md ✓, research.md ✓, data-model.md ✓, contracts/cli.md ✓, quickstart.md ✓

**Tests**: Not explicitly requested — test tasks not included.

**Organization**: Tasks are grouped by pipeline step (each step maps to a user story). The pipeline is strictly sequential: each step reads from the previous step's database output.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US6)
- Includes exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: Create .NET solution, projects, and configure dependencies

- [X] T001 Create .NET 10 solution with OldSiteParser console project (src/OldSiteParser/OldSiteParser.fsproj) and OldSiteParser.Tests xUnit project (src/OldSiteParser.Tests/OldSiteParser.Tests.fsproj), including solution file at src/OldSiteParser.sln
- [X] T002 Add NuGet package references: Microsoft.EntityFrameworkCore.Sqlite to OldSiteParser, HtmlAgilityPack to OldSiteParser, System.CommandLine to OldSiteParser, FsUnit.xUnit and Microsoft.NET.Test.Sdk to OldSiteParser.Tests; configure .fsproj compilation order listing all planned F# source files (Domain/Types.fs, Domain/Mapping.fs, Database/Entities.fs, Database/Context.fs, Shared/Hashing.fs, Shared/Kebab.fs, Shared/HtmlParsing.fs, Shared/Logging.fs, Steps/Step1–6, Program.fs)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain types, database layer, shared utilities, and CLI entry point that ALL steps depend on

**⚠️ CRITICAL**: No step implementation can begin until this phase is complete

- [X] T003 [P] Define all F# domain types (RelativePath, FileHash, FinalName, RecipeGuid single-case DUs; FileType, TemplateType, FileExistence, CustomTagName union types; ScannedFile, PageContent, ParsedUrl, FinalNameEntry, CustomTagValue, TidyResult, ExtractedMetadata, Recipe record types per data-model.md) in src/OldSiteParser/Domain/Types.fs
- [X] T004 [P] Define all EF Core entity classes with mutable properties and primitive types (Step1ScannedFileEntity, Step2RawContentEntity, Step2ParsedUrlEntity, Step3FinalNameEntity, Step4PreTidyMetadataEntity, Step5TidyResultEntity, Step6PostTidyMetadataEntity, Step6RecipeEntity with columns per data-model.md) in src/OldSiteParser/Database/Entities.fs
- [X] T005 [P] Implement SHA-256 streaming file hashing (accept file path, return hex string; use System.Security.Cryptography.SHA256 with stream input) in src/OldSiteParser/Shared/Hashing.fs
- [X] T006 [P] Implement kebab-case conversion (strip special characters, lowercase, hyphen-separate words) and common-part stripping (configurable list of prefixes/substrings like "foodsofengland", "500-"; retain stripped part if removal causes duplicate collision) in src/OldSiteParser/Shared/Kebab.fs
- [X] T007 [P] Implement shared HTML parsing helpers using HtmlAgilityPack: title tag extraction from HtmlDocument, body content extraction by comment markers (<!---TEXT START--->/<!---TEXT END--->, <!---BODY TEXT START--->/<!---BODY TEXT END--->, <!--PAGE CONTENT-->/EOF, fallback to full body), URL extraction from a/img/link/script tags with href/src attributes and alt text in src/OldSiteParser/Shared/HtmlParsing.fs
- [X] T008 [P] Implement error/warning logging utilities: structured log entries with file path, step name, and message; console warning output for orphan images and non-zero tidy exit codes; error counters for exit code summary in src/OldSiteParser/Shared/Logging.fs
- [X] T009 Implement EF Core DbContext (PipelineDbContext) with DbSets for all 8 entity types, configure primary keys and foreign key relationships per data-model.md ER diagram, call EnsureCreated() on initialization in src/OldSiteParser/Database/Context.fs
- [X] T010 Implement domain-entity mapping functions (toEntity and toDomain for each entity/record pair: ScannedFile↔Step1ScannedFileEntity, PageContent↔Step2RawContentEntity, etc.) in src/OldSiteParser/Domain/Mapping.fs
- [X] T011 Implement CLI argument parsing using System.CommandLine (--source-folder, --database, --working-dir, --step options per contracts/cli.md), step routing that calls each step module's run function, and skeleton step module files (Step1–6 with placeholder run functions) in src/OldSiteParser/Program.fs and src/OldSiteParser/Steps/Step1_IdentifyFiles.fs through Step6_Extract.fs

**Checkpoint**: Foundation ready — step implementation can now begin sequentially

---

## Phase 3: User Story 1 — Scan and Identify All Files (Priority: P1) 🎯 MVP

**Goal**: Recursively scan all files in the source folder, compute hashes, classify as HTML/image/unknown, extract titles, detect "page not found" pages, and report duplicates.

**Independent Test**: Run the app with `--step 1` against a folder containing HTML files, images, unknown files, and duplicates. Verify the Step1ScannedFiles table is populated correctly and the duplicate report prints to console.

### Implementation

- [X] T012 [US1] Implement recursive file scanning: enumerate all files under --source-folder, compute SHA-256 hash per file via Hashing module, classify by extension (htm/html → HTML; jpg/jpeg/png/gif/bmp/webp/svg/ico → image; else → unknown with error message), store Step1ScannedFileEntity per file in src/OldSiteParser/Steps/Step1_IdentifyFiles.fs
- [X] T013 [US1] Implement HTML title extraction (via HtmlParsing helper), "page not found" exclusion (case-insensitive title match sets IsExcluded=true), unknown-type error recording (ErrorMessage + IsExcluded=true), and graceful error handling for unreadable files (log error, continue processing) in src/OldSiteParser/Steps/Step1_IdentifyFiles.fs
- [X] T014 [US1] Implement duplicate detection: group all Step1ScannedFileEntity records by FileHash, print duplicate groups (paths sharing same hash) to console, include count summary in src/OldSiteParser/Steps/Step1_IdentifyFiles.fs

**Checkpoint**: Step 1 fully functional — `--step 1` scans, classifies, and reports duplicates

---

## Phase 4: User Story 2 — Parse HTML Content and Extract URLs (Priority: P1)

**Goal**: Extract body content from each non-excluded HTML file using template comment markers, and parse all URLs with tag type, alt text, and file-existence flags.

**Independent Test**: Run `--step 2` after Step 1. Verify Step2RawContents has extracted content with correct template type, and Step2ParsedUrls has all URLs with correct tag types and file-existence flags.

### Implementation

- [X] T015 [US2] Implement body content extraction: for each non-excluded HTML Step1ScannedFileEntity, load file, use HtmlParsing helpers to locate comment markers and extract content between start/end markers (or to EOF), determine TemplateType, store Step2RawContentEntity in src/OldSiteParser/Steps/Step2_ParseHtml.fs
- [X] T016 [US2] Implement URL extraction: parse extracted content with HtmlAgilityPack to find all a/img/link/script tags, record each URL string, tag type, alt text; for relative paths and foodsofengland.co.uk domain URLs resolve against --source-folder to check file existence (strip hash fragments before checking), store null for external/mailto URLs, store Step2ParsedUrlEntity records in src/OldSiteParser/Steps/Step2_ParseHtml.fs

**Checkpoint**: Steps 1–2 functional — content and URLs extracted for all HTML files

---

## Phase 5: User Story 3 — Determine Final File Names (Priority: P2)

**Goal**: Generate clean kebab-case final names for all HTML files and images, with deduplication, override support, and common-part stripping.

**Independent Test**: Run `--step 3` after Steps 1–2. Verify Step3FinalNames has unique names for all non-excluded files, overrides are applied, orphan images are logged to console, and no unresolved duplicate collisions exist.

### Implementation

- [X] T017 [US3] Implement HTML final name generation: strip "Foods of England - " and "The Foods of England - " prefixes from title tag, convert to kebab-case via Kebab module, fall back to kebabified file name if no title, detect duplicate final names and log errors for second+ occurrences in src/OldSiteParser/Steps/Step3_FinalNames.fs
- [X] T018 [US3] Implement image final name generation: query ParsedUrls to count HTML references per image, single-reference images get referencing page's FinalName as prefix, multi-reference images keep own kebabified name, zero-reference (orphan) images keep own kebabified name with console warning via Logging module, apply common-part stripping via Kebab module (retain if stripping causes collision), detect duplicate final image names in src/OldSiteParser/Steps/Step3_FinalNames.fs
- [X] T019 [US3] Implement applyOverrides function (RelativePath → FinalName → FinalName) as an editable code function for user-defined name conflict resolution, apply overrides after automatic generation, store all Step3FinalNameEntity records (with IsOverride flag) in src/OldSiteParser/Steps/Step3_FinalNames.fs

**Checkpoint**: Steps 1–3 functional — all files have unique final names

---

## Phase 6: User Story 4 — Pre-Tidy Rendering to Working Directory (Priority: P2)

**Goal**: Write rendered HTML files to the working directory with rewritten URLs, scroll.png GUIDs, custom tag extraction/replacement, and regex transformations.

**Independent Test**: Run `--step 4` after Steps 1–3. Verify output .html files exist in --working-dir with correct final names, URLs point to final names, scroll.png images have GUID id attributes, custom tags are replaced with HTML comments, and Step4PreTidyMetadata is populated.

### Implementation

- [X] T020 [US4] Implement content rendering: for each HTML Step3FinalNameEntity, read PageContent, rewrite all internal URLs to their target file's FinalName (look up each URL's target path in FinalNames table, replace href/src values), write output to --working-dir/{final-name}.html in src/OldSiteParser/Steps/Step4_PreTidy.fs
- [X] T021 [US4] Implement scroll.png GUID injection: find lines containing scroll.png references, generate a new GUID per occurrence, add id="{guid}" attribute to the img tag, track the GUID ↔ file mapping for Step 6 recipe extraction in src/OldSiteParser/Steps/Step4_PreTidy.fs
- [X] T022 [US4] Implement custom tag extraction and replacement using regex: find `<class>`, `<subclass>`, `<locale>` tags handling proper closing (`</class>`), doubled-opener closing (`<class>...<class>`), and misplaced inner-tag nesting (`<locale><i>text</locale></i>`); extract inner text content and category link URL (for class tags wrapping `<a>` tags); store Step4PreTidyMetadataEntity records; replace each tag block with `<!-- {tagname}: {content} -->` HTML comment in src/OldSiteParser/Steps/Step4_PreTidy.fs
- [X] T023 [US4] Implement Whimberry CMS tag replacement (regex matching `<W-[^>]*>` and `<W-/[^>]*>` variants, replace with HTML comments) and user-defined regex transformations (configurable list of pattern/replacement pairs applied after all other transformations) in src/OldSiteParser/Steps/Step4_PreTidy.fs

**Checkpoint**: Steps 1–4 functional — working directory has clean, renderable HTML ready for tidy

---

## Phase 7: User Story 5 — Run Tidy Console Application (Priority: P3)

**Goal**: Execute HTML Tidy CLI on each rendered file, capture output, and flag errors.

**Independent Test**: Run `--step 5` after Steps 1–4 (with tidy installed on PATH). Verify Step5TidyResults has exit codes, stdout, and stderr for each file, and non-zero exit codes are logged.

### Implementation

- [X] T024 [US5] Implement tidy CLI execution: for each .html file in --working-dir, launch tidy via System.Diagnostics.Process, capture stdout (cleaned output), stderr (warnings/errors), and exit code (0=ok, 1=warnings, 2=errors), write tidied output to file, store Step5TidyResultEntity, log non-zero exit codes via Logging module, handle tidy-not-found gracefully (clear error, no data loss) in src/OldSiteParser/Steps/Step5_Tidy.fs

**Checkpoint**: Steps 1–5 functional — all HTML files tidied and results stored

---

## Phase 8: User Story 6 — Post-Tidy Cleanup and Recipe Extraction (Priority: P3)

**Goal**: Apply post-tidy transforms, extract metadata key-value pairs, and detect/extract recipe blocks by scroll.png GUID boundaries.

**Independent Test**: Run `--step 6` after Steps 1–5. Verify post-tidy transformations applied, Step6PostTidyMetadata has key-value pairs, and Step6Recipes has recipe records with correct GUIDs matching Step 4 scroll.png injections and correct text boundaries.

### Implementation

- [X] T025 [US6] Implement post-tidy transformations: read tidied HTML output files, apply configured extraneous tag removal patterns (cleanup artifacts tidy may have inserted or left), write cleaned content back in src/OldSiteParser/Steps/Step6_Extract.fs
- [X] T026 [US6] Implement metadata extraction: scan post-tidy content with a list of extractor functions, each returning key-value pairs; built-in extractors for page creation dates (from `<!--New Page created on...-->` comments), source references, and unverified markers (`[Unverified]`); store Step6PostTidyMetadataEntity records (multiple per file, same key allowed) in src/OldSiteParser/Steps/Step6_Extract.fs
- [X] T027 [US6] Implement recipe block detection and extraction: find `<img` tags with src containing scroll.png and a GUID id attribute, capture recipe text from the scroll image line to the next plain `<hr>` (no colour attribute), the next recipe start (`<hr color="#B81010">`), or end-of-file, store Step6RecipeEntity with GUID and joined text lines in src/OldSiteParser/Steps/Step6_Extract.fs

**Checkpoint**: Full pipeline functional — all 6 steps produce structured output in the database

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Final validation and hardening across all steps

- [X] T028 Add step prerequisite validation: before running each step, verify that required tables from previous steps are populated (e.g. Step 2 checks Step1ScannedFiles exists and is non-empty), exit with code 2 and clear message if prerequisites missing in src/OldSiteParser/Program.fs
- [X] T029 Add completion summary output ("Completed steps [1,2,3,...] in X.Xs. N errors logged.") with elapsed time tracking and exit code logic (0=all success, 1=completed with warnings, 2=fatal error per contracts/cli.md) in src/OldSiteParser/Program.fs
- [X] T030 Run quickstart.md validation: build solution with `dotnet build`, run all 6 steps sequentially against example-html/ folder, verify database tables are populated, inspect output files in working directory

---

## Phase 10: Extended Analysis — ScannedFile Properties & Analysis Logging

**Purpose**: Add HasFilesPrefix, IsRefreshedSitePage, HasPageNotFoundTitle properties to Step1ScannedFileEntity and log analysis findings (duplicates, files-prefix orphans, refreshed-page orphans) to a new AnalysisLogs database table.

- [X] T031 Add HasFilesPrefix (bool), IsRefreshedSitePage (bool?), HasPageNotFoundTitle (bool?) to Step1ScannedFileEntity/ScannedFile domain type, update Mapping functions, update data-model.md
- [X] T032 Add AnalysisLogEntity (Id, Step, Category, RelativePath, Message) and AnalysisLogs DbSet to PipelineDbContext
- [X] T033 Update Step1 scanFile to populate new fields: HasFilesPrefix from path prefix, IsRefreshedSitePage from SEO string (null for non-HTML), HasPageNotFoundTitle from title (null for non-HTML)
- [X] T034 Add analysis functions: reportFilesPrefixOrphans (files/ with no non-prefixed counterpart), reportRefreshedPageOrphans (refreshed pages with no non-refreshed .htm sibling), update reportDuplicates to return log entries; clear AnalysisLogs on startup; write all entries to DB
- [X] T035 Add unit tests for new ScannedFile properties (HasFilesPrefix, IsRefreshedSitePage, HasPageNotFoundTitle) and fix existing test constructions of Step1ScannedFileEntity

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 — **BLOCKS all user stories**
- **Phase 3 (US1 — Scan)**: Depends on Phase 2
- **Phase 4 (US2 — Parse)**: Depends on Phase 3 (US1 populates Step1ScannedFiles)
- **Phase 5 (US3 — Names)**: Depends on Phase 4 (US2 populates Step2RawContents + Step2ParsedUrls)
- **Phase 6 (US4 — Render)**: Depends on Phase 5 (US3 populates Step3FinalNames)
- **Phase 7 (US5 — Tidy)**: Depends on Phase 6 (US4 writes rendered files)
- **Phase 8 (US6 — Extract)**: Depends on Phase 7 (US5 writes tidied files)
- **Phase 9 (Polish)**: Depends on all story phases complete

### Story Dependencies (strictly sequential pipeline)

```
US1 (Scan) → US2 (Parse) → US3 (Names) → US4 (Render) → US5 (Tidy) → US6 (Extract)
```

Each step reads from the previous step's database tables. No story-level parallelism is possible.

### Within-Phase Parallel Opportunities

- **Phase 2**: T003, T004, T005, T006, T007, T008 are all [P] — independent files, no cross-dependencies. T009 depends on T004, T010 depends on T003+T004, T011 depends on T009.

### Parallel Example: Phase 2 Foundational

```
Parallel batch 1: T003, T004, T005, T006, T007, T008  (all [P], independent files)
Sequential:       T009  (depends on T004 Entities.fs)
Sequential:       T010  (depends on T003 Types.fs + T004 Entities.fs)
Sequential:       T011  (depends on T009 Context.fs)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: Run `--step 1` against example-html/ folder
5. Verify Step1ScannedFiles table and duplicate report

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. US1 (Scan) → Validate Step 1 → **MVP!**
3. US2 (Parse) → Validate Steps 1–2
4. US3 (Names) → Validate Steps 1–3
5. US4 (Render) → Validate Steps 1–4, inspect output .html files
6. US5 (Tidy) → Validate Steps 1–5
7. US6 (Extract) → Validate full pipeline, inspect metadata and recipes
8. Polish → Final validation against quickstart.md

Each step adds value without breaking previous steps.

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific pipeline step/user story
- The pipeline is strictly sequential — each step depends on all prior steps
- Parallelism exists within Phase 2 (multiple foundational utilities) but not between stories
- Commit after each task or logical group
- Stop at any checkpoint to validate the pipeline up to that point
