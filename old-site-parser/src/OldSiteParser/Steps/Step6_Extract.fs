module OldSiteParser.Steps.Step6_Extract

open System
open System.IO
open System.Linq
open System.Text.RegularExpressions
open OldSiteParser.Database
open OldSiteParser.Domain.Types
open OldSiteParser.Shared

// ==========================================================================
// METADATA EXTRACTORS
// Each function takes (relativePath, content) and returns a list of (key, value) pairs.
// Add new extractors here — they are all called automatically.
// ==========================================================================

/// Extract page creation date from <!--New Page created on...-->  comments.
let extractCreationDate (content: string) : (string * string) list =
    let pattern = @"<!--\s*New Page created on\s+(.+?)-->"
    let m = Regex.Match(content, pattern, RegexOptions.IgnoreCase)

    if m.Success then
        [ ("created_date", m.Groups.[1].Value.Trim()) ]
    else
        []

/// Extract [Unverified] markers.
let extractUnverifiedMarker (content: string) : (string * string) list =
    if content.Contains("[Unverified]", StringComparison.OrdinalIgnoreCase) then
        [ ("unverified", "true") ]
    else
        []

/// Extract source references (lines starting with "Source:" or "Sources:").
let extractSourceReferences (content: string) : (string * string) list =
    let pattern = @"(?:Source|Sources)\s*:\s*(.+?)(?:<br|<p|</p|\n)"
    let matches = Regex.Matches(content, pattern, RegexOptions.IgnoreCase)

    [ for m in matches do
          let value = Regex.Replace(m.Groups.[1].Value, @"<[^>]+>", "").Trim()

          if not (String.IsNullOrWhiteSpace(value)) then
              yield ("source_ref", value) ]

/// All metadata extractor functions. Add new extractors to this list.
let metadataExtractors: (string -> (string * string) list) list =
    [ extractCreationDate
      extractUnverifiedMarker
      extractSourceReferences ]

let private splitRawLines (content: string) : string array =
    Regex.Matches(content, @".*?(?:\r\n|\n|\r|$)")
    |> Seq.cast<Match>
    |> Seq.map (fun m -> m.Value)
    |> Seq.filter (fun line -> line <> "")
    |> Seq.toArray

let private stripLineEnding (line: string) =
    line.TrimEnd('\r', '\n')

let private isBlankLine (line: string) =
    let trimmed = stripLineEnding line |> fun value -> value.Trim()
    String.IsNullOrWhiteSpace(trimmed)
    || Regex.IsMatch(trimmed, @"^(?:<br\s*/?>)+$", RegexOptions.IgnoreCase)

let private stripFirstLineTitleTag (content: string) : string =
    let lines = splitRawLines content

    if lines.Length > 0 && Regex.IsMatch(stripLineEnding lines.[0], @"^<title>.*</title>$", RegexOptions.IgnoreCase) then
        lines |> Array.skip 1 |> String.concat ""
    else
        content

let private tryExtractSynonym (content: string) : string option * string =
    let lines = splitRawLines content
    let maxCandidateIndex = min 2 (lines.Length - 1)

    let lineEnding (line: string) =
        if line.EndsWith("\r\n", StringComparison.Ordinal) then
            "\r\n"
        elif line.EndsWith("\n", StringComparison.Ordinal) then
            "\n"
        elif line.EndsWith("\r", StringComparison.Ordinal) then
            "\r"
        else
            ""

    let synonymIndex =
        [ 0 .. maxCandidateIndex ]
        |> List.tryFind (fun index ->
            let trimmed = stripLineEnding lines.[index] |> fun value -> value.Trim()
            Regex.IsMatch(trimmed, @"^\(?\s*or\b", RegexOptions.IgnoreCase))

    match synonymIndex with
    | None -> None, content
    | Some index ->
        let originalLine = lines.[index]
        let rawLine = stripLineEnding originalLine
        let suffix = lineEnding originalLine
        let withoutPrefix = Regex.Replace(rawLine, @"^\s*\(?\s*or\b\s*", "", RegexOptions.IgnoreCase)

        let openBraceIndex = withoutPrefix.IndexOf('{')
        let closeBraceIndex =
            if openBraceIndex >= 0 then
                withoutPrefix.IndexOf('}', openBraceIndex + 1)
            else
                -1

        let closingParenIndex = withoutPrefix.IndexOf(')')
        let firstBreakMatch = Regex.Match(withoutPrefix, @"<br\s*/?>", RegexOptions.IgnoreCase)

        let synonymEndExclusive =
            if closeBraceIndex >= 0 then
                closeBraceIndex + 1
            elif closingParenIndex >= 0 then
                closingParenIndex + 1
            elif firstBreakMatch.Success then
                firstBreakMatch.Index
            else
                withoutPrefix.Length

        let synonymRaw = withoutPrefix.Substring(0, synonymEndExclusive).Trim()
        let synonym = Regex.Replace(synonymRaw, @"\)\s*$", "").Trim()

        let mutable remainder = withoutPrefix.Substring(synonymEndExclusive)

        if remainder.StartsWith(")", StringComparison.Ordinal) then
            remainder <- remainder.Substring(1)

        let rewrittenLine = remainder.TrimStart()
        let removeSynonymLine = isBlankLine rewrittenLine

        let indexesToRemove =
            if removeSynonymLine then
                if index + 1 < lines.Length && isBlankLine lines.[index + 1] then
                    Set.ofList [ index; index + 1 ]
                else
                    Set.singleton index
            else
                Set.empty

        let cleanedContent =
            lines
            |> Array.mapi (fun lineIndex line ->
                if lineIndex = index && not removeSynonymLine then
                    lineIndex, (rewrittenLine + suffix)
                else
                    lineIndex, line)
            |> Array.filter (fun (lineIndex, _) -> not (indexesToRemove.Contains lineIndex))
            |> Array.map snd
            |> String.concat ""

        Some synonym, cleanedContent

// ==========================================================================
// RECEIPT EXTRACTION
// ==========================================================================

let private trimLeadingReceiptBreaks (text: string) : string =
    let trimmed = text.TrimStart()
    Regex.Replace(trimmed, @"^(?:<br>\s*)+", "", RegexOptions.IgnoreCase)

let private parseReceiptBlock (receiptId: string) (block: string) : Step6ReceiptEntity =
    let firstBreak = block.IndexOf("<br>", StringComparison.OrdinalIgnoreCase)

    let source, receiptText =
        if firstBreak >= 0 then
            let sourceText =
                block.Substring(0, firstBreak)
                |> fun value -> Regex.Replace(value, @"^<img\b[^>]*scroll\.png[^>]*>", "", RegexOptions.IgnoreCase)
                |> fun value -> value.Trim()

            let bodyText =
                block.Substring(firstBreak + 4)
                |> trimLeadingReceiptBreaks

            sourceText, bodyText
        else
            let sourceText =
                Regex.Replace(block, @"^<img\b[^>]*scroll\.png[^>]*>", "", RegexOptions.IgnoreCase)
                |> fun value -> value.Trim()

            sourceText, ""

    { ReceiptId = receiptId
      ReceiptSource = source
      RelativePath = ""
      ReceiptText = receiptText }

let private normalizeRelationDescription (text: string) =
    text
    |> fun value -> Regex.Replace(value, @"</?b>", "", RegexOptions.IgnoreCase)
    |> fun value -> Regex.Replace(value, @"<br\s*/?>", "", RegexOptions.IgnoreCase)
    |> fun value -> value.TrimStart()

let private isRelatedDescriptionStart (text: string) =
    text.StartsWith("(see", StringComparison.OrdinalIgnoreCase)
    || text.StartsWith("(for", StringComparison.OrdinalIgnoreCase)
    || Regex.IsMatch(text, @"^<i>See also:</i>", RegexOptions.IgnoreCase)
    || (text.StartsWith("for", StringComparison.OrdinalIgnoreCase)
        && text.Contains("see", StringComparison.OrdinalIgnoreCase))
    || text.StartsWith("see", StringComparison.OrdinalIgnoreCase)

let private tryGetDescriptionFromLine (line: string) : string option =
    let anchorMatches = Regex.Matches(line, @"<a\s+href=""([^""]+)"">(.*?)</a>", RegexOptions.IgnoreCase)

    let candidate =
        if anchorMatches.Count > 0 then
            line.Substring(0, anchorMatches.[0].Index)
        else
            line

    let normalized = normalizeRelationDescription candidate

    if isRelatedDescriptionStart (normalized.TrimEnd()) then
        Some normalized
    else
        None

let private normalizeRelatedPageText (description: string) (isDescriptionLine: bool) (text: string) =
    if isDescriptionLine && description.TrimStart().StartsWith("See:", StringComparison.OrdinalIgnoreCase) then
        Regex.Replace(text, @"^<b>(.*?)</b>$", "$1", RegexOptions.IgnoreCase)
    else
        text

let private isAnchorOnlyRelatedLine (line: string) : bool =
    not (line.Contains("<img", StringComparison.OrdinalIgnoreCase))
    && Regex.IsMatch(line, @"<a\s+href=""[^""]+"">", RegexOptions.IgnoreCase)

let private isRelatedPagesLine (line: string) : bool =
    let trimmed = line.Trim()

    String.IsNullOrWhiteSpace(trimmed)
    || Option.isSome (tryGetDescriptionFromLine trimmed)
    || isAnchorOnlyRelatedLine trimmed
    || Regex.IsMatch(trimmed, @"^(?:</?b>)?<br\s*/?>(?:</?b>)?$", RegexOptions.IgnoreCase)

let private parseRelatedPages (relativePath: RelativePath) (content: string) : string * Step6RelatedPageEntity list =
    let sourcePage =
        match relativePath with
        | RelativePath path -> path

    let lines = splitRawLines content
    let anchorsPattern = @"<a\s+href=""([^""]+)"">(.*?)</a>"

    let rec findBlockStart index =
        if index >= lines.Length then
            None
        else
            match tryGetDescriptionFromLine (stripLineEnding lines.[index]) with
            | Some _ -> Some index
            | None -> findBlockStart (index + 1)

    let addAnchors description isDescriptionLine matches results =
        matches
        |> Seq.cast<Match>
        |> Seq.fold (fun acc anchor ->
            { Id = 0
              SourcePage = sourcePage
              RelationDescription = description
              RelatedPageText = normalizeRelatedPageText description isDescriptionLine anchor.Groups.[2].Value
              RelatedPageUrl = anchor.Groups.[1].Value }
            :: acc) results

    match findBlockStart 0 with
    | None -> content, []
    | Some startIndex ->
        let mutable currentDescription = ""
        let mutable results = []
        let mutable endIndex = startIndex
        let mutable keepParsing = true

        while endIndex < lines.Length && keepParsing do
            let line = stripLineEnding lines.[endIndex]
            let trimmed = line.Trim()

            if String.IsNullOrWhiteSpace(trimmed) || Regex.IsMatch(trimmed, @"^(?:</?b>)?<br\s*/?>(?:</?b>)?$", RegexOptions.IgnoreCase) then
                endIndex <- endIndex + 1
            else
                let descriptionFromLine = tryGetDescriptionFromLine trimmed
                let matches = Regex.Matches(trimmed, anchorsPattern, RegexOptions.IgnoreCase)

                match descriptionFromLine, matches.Count > 0, isAnchorOnlyRelatedLine trimmed with
                | Some description, true, _ ->
                    currentDescription <- description
                    results <- addAnchors currentDescription true matches results
                    endIndex <- endIndex + 1
                | Some description, false, _ ->
                    currentDescription <- description
                    endIndex <- endIndex + 1
                | None, true, true when not (String.IsNullOrWhiteSpace(currentDescription)) ->
                    results <- addAnchors currentDescription false matches results
                    endIndex <- endIndex + 1
                | _ ->
                    keepParsing <- false

        while endIndex > startIndex && isBlankLine lines.[endIndex - 1] do
            endIndex <- endIndex - 1

        if List.isEmpty results then
            content, []
        else
            let preservedLeadingBreak =
                let firstRemovedLine = stripLineEnding lines.[startIndex]
                let leadingBreakMatch = Regex.Match(firstRemovedLine, @"^\s*(?:</?b>)?(<br\s*/?>)", RegexOptions.IgnoreCase)
                let trailingBreakMatch = Regex.Match(firstRemovedLine, @"(<br\s*/?>)\s*$", RegexOptions.IgnoreCase)
                let hasInlineAnchor = Regex.IsMatch(firstRemovedLine, anchorsPattern, RegexOptions.IgnoreCase)
                let hasStandaloneDescription =
                    firstRemovedLine
                    |> tryGetDescriptionFromLine
                    |> Option.isSome

                if leadingBreakMatch.Success then
                    leadingBreakMatch.Groups.[1].Value + "\n"
                elif hasStandaloneDescription && not hasInlineAnchor && trailingBreakMatch.Success then
                    trailingBreakMatch.Groups.[1].Value + "\n"
                else
                    ""

            let contentWithoutRelatedPages =
                (lines |> Array.take startIndex |> String.concat "")
                + preservedLeadingBreak
                + (lines |> Array.skip endIndex |> String.concat "")

            contentWithoutRelatedPages, (results |> List.rev)

/// Find receipt blocks bounded by scroll.png with id attributes, ending at plain <hr> or next receipt.
let extractReceipts (relativePath: RelativePath) (content: string) : Step6ReceiptEntity list =
    let receiptPath =
        match relativePath with
        | RelativePath path -> path

    // Find all scroll.png img tags with id attributes
    let scrollPattern =
        @"<img\s+id=""([^""]+)""\s+[^>]*scroll\.png[^>]*>"

    let scrollMatches =
        Regex.Matches(content, scrollPattern, RegexOptions.IgnoreCase)
        |> Seq.cast<Match>
        |> Seq.toArray

    if scrollMatches.Length = 0 then
        []
    else
        let mutable results = []

        for i in 0 .. scrollMatches.Length - 1 do
            let m = scrollMatches.[i]
            let receiptId = m.Groups.[1].Value
            let startIdx = m.Index

            // Find end: next plain <hr> (without color), next receipt start, or EOF
            let contentAfterStart = content.Substring(startIdx)

            // Look for end markers after the scroll image
            let plainHrPattern = @"<hr\s*/?>"

            let nextReceiptStart =
                if i < scrollMatches.Length - 1 then
                    Some(scrollMatches.[i + 1].Index - startIdx)
                else
                    None

            // Find first plain <hr> after this receipt start
            let plainHrMatch =
                Regex.Match(
                    contentAfterStart.Substring(min 100 contentAfterStart.Length),
                    plainHrPattern,
                    RegexOptions.IgnoreCase
                )

            let plainHrIdx =
                if plainHrMatch.Success then
                    Some(plainHrMatch.Index + min 100 contentAfterStart.Length)
                else
                    None

            // Find the nearest end boundary
            let endIdx =
                [ plainHrIdx; nextReceiptStart ]
                |> List.choose id
                |> List.sort
                |> List.tryHead
                |> Option.defaultValue contentAfterStart.Length

            let receiptBlock = contentAfterStart.Substring(0, endIdx).Trim()
            let parsedReceipt = parseReceiptBlock receiptId receiptBlock

            results <-
                { parsedReceipt with
                    RelativePath = receiptPath }
                :: results

        results |> List.rev

type Step6Result = {
    RelativePath: RelativePath;
    ContentEntity: Step6ContentEntity;
    Metadata: Step6PostTidyMetadataEntity list;
    Receipts: Step6ReceiptEntity list;
    RelatedPages: Step6RelatedPageEntity list;
    Content: string }

let unwrapRelativePath (RelativePath p) = p

/// Process a single HTML file: apply post-tidy transforms, extract metadata and receipts.
/// File I/O happens in parallel; returned entities are written to DB sequentially.
let processHtmlFile
    (tidiedContent: string)
    (relativePath: RelativePath)
    : Step6Result =
    try
        let mutable content = tidiedContent
        let mutable metadataFromContentCleanup: (string * string) list = []

        content <- stripFirstLineTitleTag content

        let synonym, contentWithoutSynonym = tryExtractSynonym content
        content <- contentWithoutSynonym

        match synonym with
        | Some value when not (String.IsNullOrWhiteSpace(value)) ->
            metadataFromContentCleanup <- [ ("synonym", value) ]
        | _ -> ()

        let contentWithoutRelatedPages, relatedPages = parseRelatedPages relativePath content
        content <- contentWithoutRelatedPages

        // Extract metadata
        let metadataEntities =
            (metadataFromContentCleanup
             @ (metadataExtractors |> List.collect (fun extractor -> extractor content)))
            |> List.map (fun (key, value) ->
                { Id = 0
                  RelativePath = unwrapRelativePath relativePath
                  MetadataKey = key
                  MetadataValue = value } : Step6PostTidyMetadataEntity)

        // Extract receipts
        let receiptEntities =
            extractReceipts relativePath content

        let contentEntity =
            { FinalName = unwrapRelativePath relativePath
              Content = content } : Step6ContentEntity
        
        { ContentEntity = contentEntity
          RelativePath = relativePath
          Content = content
          Metadata = metadataEntities
          Receipts = receiptEntities
          RelatedPages = relatedPages }
    with ex ->
        Logging.logError "Step6" (unwrapRelativePath relativePath) (sprintf "Extraction failed: %s" ex.Message)
        { ContentEntity = { FinalName = unwrapRelativePath relativePath; Content = tidiedContent }
          RelativePath = relativePath
          Metadata = []
          Receipts = []
          Content = tidiedContent
          RelatedPages = [] }

let removeExistingStepData (ctx: PipelineDbContext) (workingDir: string) =
    if Directory.Exists(workingDir) then
        Logging.logInfo "Step6" (sprintf "Clearing existing Step6 data for working directory: %s" workingDir)
        let files = Directory.GetFiles(workingDir, "*", SearchOption.AllDirectories)
        for file in files do
            File.Delete(file)
    else
        Directory.CreateDirectory(workingDir) |> ignore

    if ctx.Step6Contents.Any() then
        Logging.logInfo "Step6" "Clearing existing Step6Contents..."
        ctx.Step6Contents.RemoveRange(ctx.Step6Contents) |> ignore
    else
        Logging.logInfo "Step6" "No existing Step6Contents to clear."

    if ctx.Receipts.Any() then
        Logging.logInfo "Step6" "Clearing existing Receipts..."
        ctx.Receipts.RemoveRange(ctx.Receipts) |> ignore
    else
        Logging.logInfo "Step6" "No existing Receipts to clear."

    if ctx.PostTidyMetadata.Any() then
        Logging.logInfo "Step6" "Clearing existing PostTidyMetadata..."
        ctx.PostTidyMetadata.RemoveRange(ctx.PostTidyMetadata) |> ignore
    else
        Logging.logInfo "Step6" "No existing PostTidyMetadata to clear."

    if ctx.RelatedPages.Any() then
        Logging.logInfo "Step6" "Clearing existing RelatedPages..."
        ctx.RelatedPages.RemoveRange(ctx.RelatedPages) |> ignore
    else
        Logging.logInfo "Step6" "No existing RelatedPages to clear."
    
    ctx.SaveChanges() |> ignore

let run (workingDir: string) (ctx: PipelineDbContext) =
    let stopwatch = Diagnostics.Stopwatch.StartNew()
    Logging.logInfo "Step6" "Extracting metadata, receipts, and related pages..."

    removeExistingStepData ctx workingDir

    let htmlFiles =
        ctx.TidyResults.ToArray()

    ChannelProcessing.runParallel
        htmlFiles
        (fun tidy ->
            let path = Path.Combine(workingDir, (sprintf "%s.orig.htm" tidy.FinalName))
            File.WriteAllText(path, tidy.Output)
            processHtmlFile tidy.Output (RelativePath tidy.FinalName)
        )
        (fun result ->
            let path = Path.Combine(workingDir, result.RelativePath |> unwrapRelativePath)
            File.WriteAllText(path, result.Content)

            ctx.Step6Contents.Add(result.ContentEntity) |> ignore
            for m in result.Metadata do
                ctx.PostTidyMetadata.Add(m) |> ignore
            for r in result.Receipts do
                ctx.Receipts.Add(r) |> ignore
            for relatedPage in result.RelatedPages do
                ctx.RelatedPages.Add(relatedPage) |> ignore)
        Environment.ProcessorCount

    ctx.SaveChanges() |> ignore

    let step6ContentCount = ctx.Step6Contents.Count()
    let metadataCount = ctx.PostTidyMetadata.Count()
    let receiptCount = ctx.Receipts.Count()
    let relatedPageCount = ctx.RelatedPages.Count()

    Logging.logInfo
        "Step6"
        (sprintf
            "Stored %d updated contents, %d metadata entries, %d receipts, and %d related pages"
            step6ContentCount
            metadataCount
            receiptCount
            relatedPageCount)

    stopwatch.Stop()
    Logging.logStepSummary "Step6" htmlFiles.Length stopwatch.Elapsed
