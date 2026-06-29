module OldSiteParser.Steps.Step1_IdentifyFiles

open System
open System.IO
open System.Text.Json
open OldSiteParser.Database
open OldSiteParser.Domain.Types
open OldSiteParser.Domain.Mapping
open OldSiteParser.Shared

//let htmlExtensions = set [ ".htm"; ".html" ]

let imageExtensions =
    set
        [ ".jpg"
          ".jpeg"
          ".png"
          ".gif"
          ".bmp"
          ".webp"
          ".svg"
          ".ico" ]

let classifyFile (ext: string) : FileType * string option =
    let lower = ext.ToLowerInvariant()

    //if htmlExtensions.Contains(lower) then
    if lower.Contains("htm") then
        (Html, None)
    elif imageExtensions.Contains(lower) then
        (Image, None)
    else
        (Unknown, Some(sprintf "Unrecognised file type: %s" lower))

let combineOptStrings (e1: string option) (e2: string option) : string option =
    match e1, e2 with
    | None, None -> None
    | Some msg, None -> Some msg
    | None, Some extra -> Some extra
    | Some msg, Some extra -> Some (sprintf "%s; %s" msg extra)

let lastModifiedOrMin (fullPath: string) : DateTime =
    try
        File.GetLastWriteTimeUtc(fullPath)
    with _ ->
        DateTime.MinValue

let metadataFileName = "old-site.metadata.json"

let private tryGetJsonProperty (name: string) (element: JsonElement) =
    let mutable value = Unchecked.defaultof<JsonElement>

    if element.TryGetProperty(name, &value) then
        Some value
    else
        None

let private loadSnapshotMapFromMetadata (propertyName: string) (root: JsonElement) : Map<string, string> option =
    match tryGetJsonProperty propertyName root with
    | Some snapshotMap when snapshotMap.ValueKind = JsonValueKind.Object ->
        snapshotMap.EnumerateObject()
        |> Seq.choose (fun property ->
            match property.Value.ValueKind with
            | JsonValueKind.String -> Some(property.Name, property.Value.GetString())
            | _ -> None)
        |> Seq.choose (fun (relativePath, snapshotId) ->
            if String.IsNullOrWhiteSpace(relativePath) || String.IsNullOrWhiteSpace(snapshotId) then
                None
            else
                Some(relativePath.Replace('\\', '/'), snapshotId))
        |> Map.ofSeq
        |> Some
    | _ -> None

let loadSnapshotMetadata (sourceFolder: string) : Map<string, string> * Map<string, string> =
    let metadataPath = Path.Combine(sourceFolder, metadataFileName)

    if not (File.Exists(metadataPath)) then
        Map.empty, Map.empty
    else
        try
            use document = JsonDocument.Parse(File.ReadAllText(metadataPath))

            let earliestSnapshots =
                match loadSnapshotMapFromMetadata "earliestSnapshots" document.RootElement with
                | Some snapshots -> snapshots
                | None ->
                    Logging.logError "Step1" metadataFileName "Metadata file is missing earliestSnapshots object."
                    Map.empty

            let usedSnapshots =
                match loadSnapshotMapFromMetadata "usedSnapshots" document.RootElement with
                | Some snapshots -> snapshots
                | None -> Map.empty

            earliestSnapshots, usedSnapshots
        with ex ->
            Logging.logError "Step1" metadataFileName (sprintf "Could not load metadata file: %s" ex.Message)
            Map.empty, Map.empty

let loadEarliestSnapshotMetadata (sourceFolder: string) : Map<string, string> =
    loadSnapshotMetadata sourceFolder |> fst

let scanFileWithSnapshots (sourceFolder: string) (earliestSnapshots: Map<string, string>) (usedSnapshots: Map<string, string>) (fullPath: string) : ScannedFile =
    let relativePath =
        Path.GetRelativePath(sourceFolder, fullPath).Replace('\\', '/')

    let earliestSnapshot = earliestSnapshots |> Map.tryFind relativePath
    let usedSnapshot = usedSnapshots |> Map.tryFind relativePath

    try
        let hash = Hashing.computeFileHash fullPath
        let ext = Path.GetExtension(fullPath)
        let (fileType, errorMsg) = classifyFile ext

        let titleTag =
            match fileType with
            | Html -> HtmlParsing.extractTitle fullPath
            | _ -> None

        let imageIs404 =
            match fileType with
            | Image ->
                try
                    let contents = File.ReadAllText(fullPath)
                    contents.Contains("Page not found", StringComparison.OrdinalIgnoreCase)
                with _ -> false
            | _ -> false

        let (fileIsSeoOptimised, extraError) =
            match fileType with
            | Html ->
                // the revamp of the site, which we dont want to include, used this yoast plugin
                let contents = File.ReadAllText(fullPath)

                if contents.Contains("This site is optimized with the Yoast SEO plugin") then
                    true, Some "File contains SEO plugin text"
                else
                    false, None
            | Image ->
                if imageIs404 then
                    false, Some "Image file is actually a 404 Page not found"
                else
                    false, None
            | _ -> false, None

        let errorMsg = combineOptStrings errorMsg extraError

        let isPageNotFound =
            match titleTag with
            | Some title -> title.Contains("page not found", StringComparison.OrdinalIgnoreCase)
            | None -> false

        let hasFilesPrefix = relativePath.StartsWith("files/", StringComparison.OrdinalIgnoreCase)

        let isRefreshedSitePage =
            match fileType with
            | Html -> Some fileIsSeoOptimised
            | _ -> None

        let hasPageNotFoundTitle =
            match fileType with
            | Html -> Some isPageNotFound
            | _ -> None

        // i've checked and all of the files with "files/" are duplicates
        // ive checked and all of the files with SEO plugin text are from the revamp and we dont want to include them
        let isExcluded = hasFilesPrefix || fileIsSeoOptimised || isPageNotFound || fileType = Unknown || imageIs404

        { Path = RelativePath relativePath
          Hash = FileHash hash
          FileType = fileType
          EarliestSnapshot = earliestSnapshot
          UsedSnapshot = usedSnapshot
          TitleTag = titleTag
          Error = errorMsg
          IsExcluded = isExcluded
          HasFilesPrefix = hasFilesPrefix
          IsRefreshedSitePage = isRefreshedSitePage
          HasPageNotFoundTitle = hasPageNotFoundTitle
          LastModified = lastModifiedOrMin fullPath }
    with ex ->
        Logging.logError "Step1" relativePath (sprintf "Cannot read file: %s" ex.Message)

        { Path = RelativePath relativePath
          Hash = FileHash ""
          FileType = Unknown
          EarliestSnapshot = earliestSnapshot
          UsedSnapshot = usedSnapshot
          TitleTag = None
          Error = Some(sprintf "Cannot read file: %s" ex.Message)
          IsExcluded = true
          HasFilesPrefix = relativePath.StartsWith("files/", StringComparison.OrdinalIgnoreCase)
          IsRefreshedSitePage = None
          HasPageNotFoundTitle = None
          LastModified = lastModifiedOrMin fullPath }

let scanFile (sourceFolder: string) (earliestSnapshots: Map<string, string>) (fullPath: string) : ScannedFile =
    scanFileWithSnapshots sourceFolder earliestSnapshots Map.empty fullPath

let reportDuplicates (files: Step1ScannedFileEntity seq) : AnalysisLogEntity list =
    let groups =
        files
        |> Seq.filter (fun f -> not (f.IsExcluded))
        |> Seq.filter (fun f -> not (String.IsNullOrEmpty(f.FileHash)))
        |> Seq.groupBy (fun f -> f.FileHash)
        |> Seq.filter (fun (_, group) -> Seq.length group > 1)
        |> Seq.toList

    if groups.IsEmpty then
        Logging.logInfo "Step1" "No duplicate files found."
        []
    else
        Logging.logInfo "Step1" (sprintf "Found %d groups of duplicate files:" groups.Length)

        [ for (hash, group) in groups do
            let paths = group |> Seq.map (fun f -> f.RelativePath) |> Seq.toList
            printfn "  Hash %s:" (hash.Substring(0, 12))

            for p in paths do
                printfn "    - %s" p

            yield { Id = 0; Step = "Step1"; Category = "duplicate"; RelativePath = null
                    Message = sprintf "Hash %s shared by: %s" (hash.Substring(0, 12)) (String.Join(", ", paths)) } ]

let reportFilesPrefixOrphans (files: Step1ScannedFileEntity seq) : AnalysisLogEntity list =
    let allPaths = files |> Seq.map (fun f -> f.RelativePath.ToLower()) |> Set.ofSeq

    let filesPrefixFiles =
        files |> Seq.filter (fun f -> f.HasFilesPrefix) |> Seq.toList

    let orphans =
        filesPrefixFiles
        |> List.filter (fun f ->
            let withoutPrefix = f.RelativePath.Substring(6).ToLower() // strip "files/" and convert to lowercase
            not (allPaths.Contains(withoutPrefix)))

    if orphans.IsEmpty then
        Logging.logInfo "Step1" "No files/ prefix orphans found."
    else
        Logging.logInfo "Step1" (sprintf "Found %d files with files/ prefix that have no corresponding non-prefixed file:" orphans.Length)
        for o in orphans do
            printfn "  - %s" o.RelativePath

    [ for o in orphans do
        yield { Id = 0; Step = "Step1"; Category = "files-prefix-orphan"; RelativePath = o.RelativePath
                Message = sprintf "File has files/ prefix but no corresponding file without prefix" } ]

let reportRefreshedPageOrphans (files: Step1ScannedFileEntity seq) : AnalysisLogEntity list =
    let filesByDir =
        files
        |> Seq.groupBy (fun f ->
            let idx = f.RelativePath.LastIndexOf('/')
            if idx >= 0 then f.RelativePath.Substring(0, idx) else "")
        |> Map.ofSeq

    let refreshedFiles =
        files
        |> Seq.filter (fun f -> f.IsRefreshedSitePage.HasValue && f.IsRefreshedSitePage.Value)
        |> Seq.toList

    let orphans =
        refreshedFiles
        |> List.filter (fun f ->
            let dir =
                let idx = f.RelativePath.LastIndexOf('/')
                if idx >= 0 then f.RelativePath.Substring(0, idx) else ""
            let siblings = filesByDir |> Map.tryFind dir |> Option.defaultValue Seq.empty
            // check if there's a non-refreshed .htm file in the same directory
            let hasHtmSibling =
                siblings
                |> Seq.exists (fun s ->
                    s.RelativePath <> f.RelativePath
                    && s.FileType = "html"
                    && (not s.IsRefreshedSitePage.HasValue || not s.IsRefreshedSitePage.Value))
            not hasHtmSibling)

    if orphans.IsEmpty then
        Logging.logInfo "Step1" "No refreshed page orphans found."
    else
        Logging.logInfo "Step1" (sprintf "Found %d refreshed (SEO) pages with no non-refreshed .htm sibling:" orphans.Length)
        for o in orphans do
            printfn "  - %s" o.RelativePath

    [ for o in orphans do
        yield { Id = 0; Step = "Step1"; Category = "refreshed-page-orphan"; RelativePath = o.RelativePath
                Message = sprintf "Refreshed site page with no non-refreshed .htm file in same directory" } ]

open System.Linq
let removeExistingStepData (ctx: PipelineDbContext) =
    let existingLogCount = ctx.AnalysisLogs.Count()
    if existingLogCount > 0 then
        Logging.logInfo "Step1" (sprintf "Removing %d existing analysis log records from database..." existingLogCount)
        ctx.AnalysisLogs.RemoveRange(ctx.AnalysisLogs) |> ignore
        ctx.SaveChanges() |> ignore

    let existingCount = ctx.ScannedFiles.Count()
    if existingCount > 0 then
        Logging.logInfo "Step1" (sprintf "Removing %d existing scanned file records from database..." existingCount)
        ctx.ScannedFiles.RemoveRange(ctx.ScannedFiles) |> ignore
        ctx.SaveChanges() |> ignore
    else
        Logging.logInfo "Step1" "No existing scanned file records found in database."

let run (sourceFolder: string) (ctx: PipelineDbContext) =
    let stopwatch = Diagnostics.Stopwatch.StartNew()
    Logging.logInfo "Step1" "Scanning files..."

    removeExistingStepData ctx

    let earliestSnapshots, usedSnapshots = loadSnapshotMetadata sourceFolder

    let allFiles =
        Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories)
        |> Seq.filter (fun filePath ->
            not (String.Equals(Path.GetFileName(filePath), metadataFileName, StringComparison.OrdinalIgnoreCase)))
        |> Seq.toArray

    Logging.logInfo "Step1" (sprintf "Found %d files to scan" allFiles.Length)

    ChannelProcessing.runParallel
        allFiles
        (fun filePath ->
            let scanned = scanFileWithSnapshots sourceFolder earliestSnapshots usedSnapshots filePath
            scannedFileToEntity scanned)
        (fun entity ->
            printfn "Scanned: %s (Type: %s, Excluded: %b)" entity.RelativePath entity.FileType entity.IsExcluded
            ctx.ScannedFiles.Add(entity) |> ignore)
        Environment.ProcessorCount

    ctx.SaveChanges() |> ignore

    // Report duplicates
    let allEntities = ctx.ScannedFiles |> Seq.toArray
    let duplicateLogs = reportDuplicates allEntities
    let filesPrefixLogs = reportFilesPrefixOrphans allEntities
    let refreshedPageLogs = reportRefreshedPageOrphans allEntities

    let allLogs = duplicateLogs @ filesPrefixLogs @ refreshedPageLogs
    if not allLogs.IsEmpty then
        for log in allLogs do
            ctx.AnalysisLogs.Add(log) |> ignore
        ctx.SaveChanges() |> ignore
        Logging.logInfo "Step1" (sprintf "Wrote %d analysis log entries to database." allLogs.Length)

    let htmlCount =
        allEntities |> Seq.filter (fun f -> f.FileType = "html") |> Seq.length

    let imageCount =
        allEntities
        |> Seq.filter (fun f -> f.FileType = "image")
        |> Seq.length

    let excludedCount =
        allEntities |> Seq.filter (fun f -> f.IsExcluded) |> Seq.length

    Logging.logInfo
        "Step1"
        (sprintf "HTML: %d, Images: %d, Excluded: %d" htmlCount imageCount excludedCount)

    stopwatch.Stop()
    Logging.logStepSummary "Step1" allFiles.Length stopwatch.Elapsed
