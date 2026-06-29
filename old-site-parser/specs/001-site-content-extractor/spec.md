# Feature Specification: Website Content Extraction Pipeline

**Feature Branch**: `001-site-content-extractor`  
**Created**: 2026-03-31  
**Status**: Draft  
**Input**: User description: "App to extract information from a saved-to-disk food history website (foodsofengland.co.uk) through a multi-step pipeline: file identification, HTML parsing, final naming, pre-tidy rendering, tidy cleanup, and information/recipe extraction."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Scan and Identify All Files (Priority: P1)

A user points the app at a root folder containing the scraped website files. The app recursively scans every file and subfolder, computes a hash for each file, classifies it as HTML or image, extracts the title tag from HTML files, detects "page not found" pages, flags unrecognised file types as errors, and stores all results in a local database. At the end, the app prints a summary of duplicate files (by hash).

**Why this priority**: This is the foundation step — nothing else can proceed without a catalogue of what files exist, what they are, and which are duplicates.

**Independent Test**: Can be fully tested by running the app against a sample folder containing a mix of HTML files, images, unknown files, duplicates, and "page not found" pages, then inspecting the resulting database table and duplicate report.

**Acceptance Scenarios**:

1. **Given** a folder with HTML, image, and miscellaneous files, **When** the scan runs, **Then** every file is recorded in the database with its relative path, hash, and file type.
2. **Given** an HTML file with a title tag of "page not found", **When** the scan runs, **Then** that file is recorded but excluded from subsequent steps.
3. **Given** a file that is neither HTML nor an image (e.g. `.json`, `.xml`), **When** the scan runs, **Then** an error is stored against that file describing the unrecognised type.
4. **Given** two files in different subfolders with the same hash, **When** the scan completes, **Then** both are reported as duplicates in the summary output.
5. **Given** a file that cannot be read (e.g. permission error), **When** the scan runs, **Then** a descriptive error is logged and scanning continues with the remaining files.

---

### User Story 2 - Parse HTML Content and Extract URLs (Priority: P1)

For every HTML file that passed Step 1 (not "page not found", not an error), the app locates the body content by searching for known start/end comment markers (or end-of-file), extracts it, and stores it. It then parses all URLs from the content, recording each URL's tag type, alt text, and whether the referenced file exists on disk (for relative paths to the site's own domains).

**Why this priority**: Extracting the actual page content and URL inventory is essential for all downstream steps (renaming, rendering, recipe extraction).

**Independent Test**: Can be tested by providing a set of HTML files with known comment markers and URL references, then verifying the content table and URL table in the database.

**Acceptance Scenarios**:

1. **Given** an HTML file with `<!---TEXT START--->` and `<!---TEXT END--->` markers, **When** parsing runs, **Then** only the content between those markers is stored.
2. **Given** an HTML file with `<!---BODY TEXT START--->` and `<!---BODY TEXT END--->` end marker, **When** parsing runs, **Then** content from the start marker to end marker is stored.
3. **Given** an HTML file with `<!--PAGE CONTENT-->` marker, **When** parsing runs, **Then** content from that marker to the next end marker or end-of-file is stored.
4. **Given** an HTML file that matches none of the known templates, **When** parsing runs, **Then** the app still attempts to extract the full body content and stores it (with a note that no template was matched).
5. **Given** a relative URL referencing `foodsofengland beechmint.jpg` (with spaces in the filename) within the content, **When** URL extraction runs, **Then** the URL, tag type (e.g. `img`), alt text, and file-existence flag are stored.
6. **Given** a URL with a hash fragment like `references.htm#Garrett 1891`, **When** URL extraction runs, **Then** the full URL is stored as-is, but the file-existence check uses only the path portion (`references.htm`) before the fragment.
7. **Given** a URL pointing to an external domain (not foodsofengland.co.uk), **When** URL extraction runs, **Then** the file-existence flag is stored as null.
8. **Given** a `mailto:` link, **When** URL extraction runs, **Then** it is stored with the file-existence flag as null.

---

### User Story 3 - Determine Final File Names (Priority: P2)

After parsing, the app generates clean, consistent file names. For HTML files, it takes the title tag value, strips special characters, and kebab-cases it. For images referenced by a single HTML page, it prefixes the image name with that page's final name. For images referenced by multiple pages, it keeps the original name (kebabified). Duplicates in final names are resolved by keeping the first occurrence and logging the rest; overrides are defined as a function in code that the user can modify to resolve conflicts.

**Why this priority**: Clean naming must happen before any content can be rendered to the working directory.

**Independent Test**: Can be tested by providing a database populated from Steps 1–2 and verifying the final-name table, duplicate log, and override application.

**Acceptance Scenarios**:

1. **Given** an HTML file with title "Foods of England - African Cakes", **When** naming runs, **Then** the site prefix is stripped and the final name is `african-cakes`.
2. **Given** an HTML file with title "The Foods of England - Apple Hearthbread", **When** naming runs, **Then** the alternative prefix is also stripped and the final name is `apple-hearthbread`.
3. **Given** two HTML files that produce the same final name, **When** naming runs, **Then** the first is kept, the second is logged as a duplicate error, and the user can supply an override.
4. **Given** an image referenced by exactly one HTML file whose final name is `african-cakes`, and the image file is `foodsofengland beechmint.jpg`, **When** naming runs, **Then** the common prefix "foodsofengland" is stripped and the image final name is `african-cakes-beechmint`.
5. **Given** two images that would both become `beechmint` after stripping common parts, **When** naming runs, **Then** the common parts are retained for those images to avoid a duplicate collision.
6. **Given** an image referenced by three HTML files, **When** naming runs, **Then** the image keeps its own kebabified name (with common parts stripped if no duplicates result).
7. **Given** an override function is defined in code that maps a specific relative path to a custom final name, **When** naming runs, **Then** the override function's output replaces the generated name for that file.

---

### User Story 4 - Pre-Tidy Rendering to Working Directory (Priority: P2)

The app writes each HTML file's content to a working directory using the final file name (with `.html` extension). All internal URLs in the content are rewritten to point to the final file names determined in Step 3. Lines containing `scroll.png` are modified to include a unique identifier on the image tag. Custom HTML tags created by the site author — `<class>`, `<subclass>`, and `<locale>` — are extracted (content stored in the database) and then replaced with HTML comments so they do not cause the tidy console application to error. Additional user-defined regex transformations can be applied at this stage.

**Why this priority**: Rendering the cleaned content is required before the tidy tool can process it and before recipes can be extracted. Custom tags must be handled here because they are non-standard HTML that would cause tidy to fail.

**Independent Test**: Can be tested by running the rendering step and verifying the output files exist with correct names, rewritten URLs, scroll.png identifiers, custom tags replaced with comments, and any custom regex transformations.

**Acceptance Scenarios**:

1. **Given** an HTML file with final name `mrs-beetons-seed-cake`, **When** rendering runs, **Then** a file `mrs-beetons-seed-cake.html` is created in the working directory.
2. **Given** content containing a relative URL `../images/pie.jpg` whose final name is `mrs-beetons-seed-cake-pie.jpg`, **When** rendering runs, **Then** the URL in the output file points to `mrs-beetons-seed-cake-pie.jpg`.
3. **Given** content containing a line with `scroll.png`, **When** rendering runs, **Then** the line is modified to include a unique identifier attribute on the image tag.
4. **Given** a user-defined regex transformation (e.g. strip a specific ad block pattern), **When** rendering runs, **Then** the transformation is applied to the output.
5. **Given** content containing `<class><a href="listcakes.htm"><b><big>Cakes</big></b></a><class>` (note: closed with a second `<class>` instead of `</class>`), **When** rendering runs, **Then** the text content "Cakes" and the category link URL `listcakes.htm` are extracted and stored, and the entire custom tag block is replaced with an HTML comment.
6. **Given** content containing `<class><b>Breads</b></class>` (properly closed), **When** rendering runs, **Then** the text content "Breads" is extracted and stored, and the tag is replaced with an HTML comment.
7. **Given** content containing `<locale><i>Yorkshire</locale></i>` (with misplaced italic nesting), **When** rendering runs, **Then** the text content "Yorkshire" is extracted and stored, and the custom tag block (including the misplaced `</i>`) is replaced with an HTML comment.
8. **Given** content containing `<subclass><i>Historic</i></subclass>`, **When** rendering runs, **Then** the text content "Historic" is stored, and the tag is replaced with an HTML comment.
9. **Given** content containing Whimberry CMS tags like `<W-NoColumn>` or `<W-/NoColumn>`, **When** rendering runs, **Then** these are also replaced with HTML comments to avoid tidy errors.
10. **Given** an unrecognised non-standard tag (not in the known set), **When** rendering runs, **Then** it is left as-is.

---

### User Story 5 - Run Tidy Console Application (Priority: P3)

The tidy console application is executed on each rendered HTML file. Stdout, stderr, and exit code are captured and stored in the database. Non-zero exit codes are flagged for later inspection.

**Why this priority**: Tidy normalises the HTML, which is important for consistent downstream extraction, but the pipeline is usable without it.

**Independent Test**: Can be tested by running the tidy step against a set of rendered files and checking the database for exit codes, stdout, and stderr values.

**Acceptance Scenarios**:

1. **Given** a rendered HTML file, **When** tidy runs, **Then** the cleaned output is written to a new file and the exit code, stdout, and stderr are stored in the database.
2. **Given** tidy returns a non-zero exit code for a file, **When** the step completes, **Then** that file is flagged in the error log for inspection.
3. **Given** tidy cannot be found or fails to launch, **When** the step runs, **Then** a clear error is logged and no data is lost.

---

### User Story 6 - Post-Tidy Cleanup and Recipe Extraction (Priority: P3)

After tidy, post-tidy transformations are applied (primarily removing extraneous HTML tags). The app extracts metadata (key-value pairs) from each page and stores them. It then detects recipe blocks — identified by lines containing `<img src="scroll.png"` with the unique identifier from Step 4 — and captures each recipe's text (from the scroll image line to the next `<hr>` tag or next recipe start). Recipes are stored with their associated unique identifier. (Note: custom tag extraction has already been performed in Step 4, before tidy.)

**Why this priority**: This is the final value-extraction step. It depends on all prior steps but delivers the core structured output (metadata and recipes).

**Independent Test**: Can be tested by providing tidied HTML files with known recipe blocks, then verifying the metadata and recipe tables in the database.

**Acceptance Scenarios**:

1. **Given** a tidied HTML file with two recipe blocks separated by `<hr>`, **When** extraction runs, **Then** two separate recipe records are created, each with its unique identifier and recipe text.
2. **Given** a tidied HTML file with no `scroll.png` references, **When** extraction runs, **Then** no recipe records are created for that file, and it is still processed for metadata.
3. **Given** a recipe block that ends at the end of the file rather than an `<hr>`, **When** extraction runs, **Then** the recipe content captures everything from the scroll image to the end.
4. **Given** post-tidy transformations are configured, **When** the step runs, **Then** the transformations are applied before extraction begins.

---

### Edge Cases

- What happens when an HTML file has no `<title>` tag? (Assumption: fall back to using the file name, kebabified.)
- What happens when the same file content exists at the root and in a subfolder (identical hash)? (Handled by duplicate detection in Step 1.)
- What happens when an HTML file contains multiple template start markers? (Assumption: use the first matched start marker and its corresponding end marker.)
- What happens when an image file is referenced in no HTML file at all? (Handled: keep its original name, kebabified, and log a warning to the console.)
- What happens when the tidy application is not installed on the system? (The step logs a clear error and the pipeline can be re-run after tidy is installed.)
- What happens when an HTML file is extremely large (e.g. hundreds of MB)? (The app should stream/process without loading the entire file into memory where possible.)
- What happens when a URL in the content is malformed? (Log a warning and skip that URL entry.)
- What happens when an unrecognised custom HTML tag is encountered (not `<class>`, `<subclass>`, or `<locale>`)? (Assumption: leave it in place; only the known custom tags are extracted and stripped.)
- What happens when an HTML page is a full cookbook transcription (e.g. `book1658compleat.htm`) with recipes in plain prose but no `scroll.png` markers? (Those pages will have no extracted recipes; they are still processed for content, URLs, and tags.)
- What happens when a `<class>` tag is "closed" with another `<class>` instead of `</class>`? (Handled: the parser supports both variants.)
- What happens when image filenames contain spaces (e.g. `foodsofengland beechmint.jpg`)? (Handled: spaces are converted to hyphens during kebabification.)

## Requirements *(mandatory)*

### Functional Requirements

#### Step 1: File Identification

- **FR-001**: The app MUST recursively scan a user-specified root folder and all subfolders.
- **FR-002**: The app MUST compute a content hash for every file encountered.
- **FR-003**: The app MUST classify each file as HTML (file extension contains "htm"), image (common image extensions: jpg, jpeg, png, gif, bmp, webp, svg, ico), or unknown.
- **FR-004**: For HTML files, the app MUST extract the `<title>` tag value, if present.
- **FR-005**: For HTML files with a title of "page not found" (case-insensitive), the app MUST mark them as excluded from subsequent steps.
- **FR-006**: For files that are neither HTML nor image, the app MUST record an error describing the unrecognised file type and exclude them from subsequent steps.
- **FR-007**: The app MUST store scan results in a database table keyed by relative file path, with columns for file hash, file type, optional title tag, and optional error message.
- **FR-008**: The app MUST print a report of duplicate files (identical hashes) at the end of Step 1.

#### Step 2: HTML Content Parsing

- **FR-009**: For each HTML file not excluded in Step 1, the app MUST locate body content using known comment markers: `<!---TEXT START--->`, `<!---BODY TEXT START--->`, or `<!--PAGE CONTENT-->` as start markers and `<!---TEXT END--->`, `<!---BODY TEXT END--->`, or end-of-file as end markers.
- **FR-010**: If no known comment markers are found, the app MUST still attempt to extract the body content (the full `<body>` content or the whole file if no body tag) and note that no template was matched.
- **FR-011**: The app MUST store the extracted content in a database table keyed by relative file path.
- **FR-012**: The app MUST parse all URLs from the extracted content, recording: the URL, the HTML tag type (e.g. `a`, `img`, `link`, `script`), and any `alt` text if present.
- **FR-013**: For each URL, if it appears to be a relative path or points to `www.foodsofengland.co.uk` or `foodsofengland.co.uk`, the app MUST determine whether the referenced file exists on disk and store: `true` (definitely exists), `false` (definitely does not exist), or `null` (ambiguous or external domain). For URLs with hash fragments (e.g. `references.htm#Garrett 1891`), the file-existence check MUST use only the path portion before the fragment.
- **FR-014**: URL records MUST be stored in a database table keyed by relative file path with columns for URL, tag type, alt text, and file-existence flag.

#### Step 3: Final File Names

- **FR-015**: For HTML files, the app MUST generate a final name by taking the title tag, stripping the known site prefix ("Foods of England - " or "The Foods of England - "), removing special characters, and converting to kebab-case.
- **FR-016**: If no title tag is available, the app MUST fall back to the original file name (kebabified).
- **FR-017**: For duplicate final HTML names, the app MUST keep the first occurrence, log the rest as errors, and support overrides defined as a function in code to resolve conflicts.
- **FR-018**: For images referenced by exactly one HTML file, the app MUST prefix the kebabified image file name with that HTML file's final name.
- **FR-019**: For images referenced by multiple HTML files, the app MUST use the kebabified original image file name.
- **FR-020**: For images referenced by zero HTML files, the app MUST use the kebabified original image file name and MUST log a warning to the console identifying the orphan image.
- **FR-021**: During image name generation, the app MUST strip common/known parts from image file names (e.g. site name prefixes like "foodsofengland", "500-"). If stripping a common part would cause a duplicate name collision, that part MUST be retained for the affected images.
- **FR-022**: For duplicate final image names, the app MUST keep the first occurrence, log the rest as errors, and support overrides defined as a function in code.
- **FR-023**: Final names MUST be stored in a database table keyed by relative file path with columns for the final name and file type.

#### Step 4: Pre-Tidy Rendering

- **FR-024**: The app MUST write each HTML file's extracted content to a working directory using the final name with `.html` extension.
- **FR-025**: All internal URLs in the rendered content MUST be rewritten to point to the final file names from Step 3.
- **FR-026**: Lines containing `scroll.png` MUST be modified to include a unique identifier attribute on the image tag (e.g. `id="<guid>"`).
- **FR-027**: The app MUST find and extract content from custom HTML tags created by the site author — specifically `<class>`, `<subclass>`, and `<locale>` — storing each tag's name, text content, and (for `<class>`) any category link URL, in a database table keyed by file reference.
- **FR-028**: The custom tag parser MUST handle both proper closing (e.g. `<class>...</class>`) and the site author's common pattern of using a doubled opening tag as a closer (e.g. `<class>...<class>`). It MUST also tolerate misplaced nesting of inner tags (e.g. `<locale><i>text</locale></i>`).
- **FR-029**: After extracting content from custom tags, the app MUST replace those custom tag blocks with HTML comments (preserving the tag name and content in the comment) so the tidy console application does not error on non-standard tags.
- **FR-030**: The app MUST also replace Whimberry CMS structural tags (`<W-NoColumn>`, `<W-/NoColumn>`, and similar `<W-*>` variants) with HTML comments before tidy.
- **FR-031**: The app MUST support user-defined regex-based transformations applied during this step.

#### Step 5: Tidy Console Application

- **FR-032**: The app MUST run the tidy console application on each file rendered in Step 4.
- **FR-033**: The app MUST capture and store the exit code, stdout, and stderr for each tidy execution in a database table keyed by final file name.
- **FR-034**: Non-zero tidy exit codes MUST be logged for later inspection.

#### Step 6: Information Extraction

- **FR-035**: After tidy, the app MUST apply post-tidy transformations (primarily removing extraneous HTML tags) before extraction.
- **FR-036**: The app MUST extract metadata key-value pairs from each HTML file and store them in a database table. Each file can have multiple metadata entries, including multiple entries with the same key. The specific metadata to extract will be defined during planning.
- **FR-037**: The app MUST detect recipe blocks identified by lines containing `<img src="scroll.png"` with the unique identifier added in Step 4. The recipe start is typically preceded by a coloured `<hr color="#B81010">` and followed by source attribution text.
- **FR-038**: Each recipe block MUST be captured from the scroll image line to the next plain `<hr>` tag (without a colour attribute), the start of the next recipe (next `<hr color="#B81010"><img src="scroll.png"`), or end-of-file.
- **FR-039**: Recipes MUST be stored in a database table with columns for the recipe text lines and the unique identifier from the image tag.

#### Cross-Cutting

- **FR-040**: The app MUST run each step independently, using the database as the handoff between steps.
- **FR-041**: The app MUST log all errors encountered during processing for later inspection.
- **FR-042**: The app MUST handle large numbers of files efficiently without excessive memory usage.
- **FR-043**: The app MUST continue processing remaining files when it encounters an unparseable or problematic file.
- **FR-044**: Within each pipeline step, per-file processing MUST be parallelised where appropriate using bounded channels, with all database write operations serialised to a single thread to respect SQLite's single-writer constraint.

### Key Entities

- **ScannedFile**: A file discovered during the recursive scan. Key attributes: relative path (primary key), content hash, file type (HTML/image/unknown), title tag (HTML only), error message (if applicable), excluded flag.
- **PageContent**: The extracted body content from an HTML file. Key attributes: relative file path (primary key), content text, template type (one of: "text-start" for `<!---TEXT START--->`, "body-text-start" for `<!---BODY TEXT START--->`, "page-content" for `<!--PAGE CONTENT-->`, or "none" if no template matched).
- **ParsedURL**: A URL found within extracted HTML content. Key attributes: relative file path (foreign key), URL string, tag type, alt text, file-existence flag (true/false/null).
- **FinalName**: The cleaned, deduplicated name for output. Key attributes: relative file path (primary key), final name, file type (HTML/image/other), override flag.
- **TidyResult**: Output of running the tidy console application. Key attributes: final file name (primary key), exit code, stdout, stderr.
- **CustomTagValue**: A value extracted from a site-author custom HTML tag (e.g. `<class>`, `<subclass>`, `<locale>`). Key attributes: file reference, tag name, text content, category link URL (for `<class>` tags that wrap a link).
- **ExtractedMetadata**: A key-value pair of metadata extracted from a processed HTML page. Key attributes: file reference, metadata key, metadata value. A file can have multiple entries, including multiple with the same key.
- **Recipe**: A recipe block extracted from a page. Key attributes: unique identifier (GUID from scroll.png), file reference, recipe text lines.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of files in the source folder (and subfolders) are scanned and classified in Step 1 — no files are silently skipped.
- **SC-002**: All duplicate files (by content hash) are correctly identified and reported.
- **SC-003**: All HTML files with recognised template markers have their body content correctly extracted (verified against a sample set of 20+ files).
- **SC-004**: All internal URLs in parsed HTML are catalogued with correct tag type, alt text, and file-existence determination.
- **SC-005**: Final file names are unique — zero unresolved name collisions after override application.
- **SC-006**: Rendered output files contain correctly rewritten URLs that resolve to the actual final file names.
- **SC-007**: Every `scroll.png` reference is assigned a unique identifier that survives through tidy and is present in the extraction step.
- **SC-008**: All recipe blocks in a sample set of 10+ pages with known recipes are correctly extracted and stored with their unique identifiers.
- **SC-009**: All custom HTML tags (`<class>`, `<subclass>`, `<locale>`) are extracted with their content and replaced with HTML comments before tidy runs — verified against a sample set with known custom tags. Tidy does not error on these replacements.
- **SC-010**: The full pipeline completes on the entire source folder within a reasonable time and without running out of memory.
- **SC-011**: Every error encountered during processing is logged with enough context (file path, step, error description) to diagnose the issue.

## Assumptions

- The source website is the archived content of `www.foodsofengland.co.uk` / `foodsofengland.co.uk`, saved to disk by a website scraper.
- The source files already exist on disk — the app does not download or scrape anything.
- The app is a command-line tool run locally by a single user; no multi-user, network, or authentication concerns apply.
- Common image extensions for classification are: jpg, jpeg, png, gif, bmp, webp, svg, ico.
- The tidy console application (HTML Tidy) is pre-installed on the user's system and available on the PATH.
- HTML files may be malformed or non-standard — the parser must be lenient.
- If an HTML file has no `<title>` tag, the original file name (kebabified) is used as the final name.
- If an HTML file matches multiple start comment markers, the first match is used.
- If an image is referenced by zero HTML files, it keeps its original name (kebabified).
- Steps are run sequentially (1 through 6) and each step's output is persisted to the database before the next step begins.
- The override mechanism for duplicate name resolution is a function defined in code that the user can edit to handle specific cases.
- The "tags" extracted in Step 6 are metadata key-value pairs derived from the page content (e.g. category, source, date). Each file can have multiple metadata entries, even with the same key. The specific extraction logic will be refined during planning.
- The site author created custom HTML tags (`<class>`, `<subclass>`, `<locale>`) that are not standard HTML. These may be closed properly (`</class>`) or with a doubled opening tag (`<class>...<class>`), and may have misplaced nesting of inner tags. They are extracted for their content in Step 4 (pre-tidy) and replaced with HTML comments so that the tidy console application does not error on them.
- The `<class>` tag typically wraps a link to a category list page (e.g. `<a href="listcakes.htm">Cakes</a>`). Both the text content and the category link URL are extracted.
- Whimberry CMS structural tags (`<W-NoColumn>`, `<W-/NoColumn>`) are also replaced with HTML comments before tidy.
- All title tags observed begin with "Foods of England - " or "The Foods of England - ". This prefix is stripped when generating final file names.
- Image filenames on the site may contain spaces (e.g. `foodsofengland beechmint.jpg`) and common prefixes/parts (e.g. `foodsofengland`, `500-`). Spaces are converted to hyphens, and known common parts are stripped during name generation — unless stripping would cause a duplicate.
- Some pages are full cookbook transcriptions (e.g. `book1658compleat.htm`) containing many recipes in plain prose without `scroll.png` markers. These will not have extracted recipe records.
- The pipeline uses a single local database file that persists across runs, allowing individual steps to be re-run independently.
