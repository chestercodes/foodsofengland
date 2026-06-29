module OldSiteParser.Steps.Step3_FinalNames

open System
open System.IO
open System.Linq
open OldSiteParser.Database
open OldSiteParser.Domain.Types
open OldSiteParser.Domain.Mapping
open OldSiteParser.Shared

/// User-editable override function for name conflict resolution.
/// Edit this function to map specific relative paths to custom final names.
/// Return the generatedName unchanged if no override is needed.
let applyOverrides (path: RelativePath) : FinalName option =
    match path with
    | RelativePath p when String.Equals(p, "references.htm", StringComparison.OrdinalIgnoreCase) -> Some (FinalName "references.html")
    | _ -> None
let generateHtmlFinalNames (htmlFiles: ScannedFile[]) : (RelativePath * FinalName) list =
    htmlFiles
    |> Array.filter (fun f -> not f.IsExcluded)
    |> Array.map (fun f ->
        let withOverride = applyOverrides f.Path
        match withOverride with
        | Some (overridden) -> (f.Path, overridden)
        | None ->
            let baseName =
                match f.TitleTag with
                | Some t ->
                    let stripped = Kebab.stripTitlePrefix t
                    Kebab.toKebabCase stripped
                | None ->
                    Kebab.kebabifyFileName f.Path
            let finalName = FinalName (sprintf "%s.html" baseName)
            (f.Path, finalName))
    |> Array.toList

let generateImageFinalNames (allUrls: ParsedUrl[]) (imageFiles: ScannedFile[]) (htmlNames: Map<RelativePath, FinalName>) : (RelativePath * FinalName * AnalysisLogEntity option) list =
    
    let returnNonNullOrWhitespace opt =
        match opt with
        | Some s -> if String.IsNullOrWhiteSpace s then None else Some s
        | None -> None
    
    let returnUrlOrNone (parsedUrl: ParsedUrl) (img: ScannedFile) : string option =
        let (RelativePath imgPath) = img.Path
        let (RelativePath sourcePath) = parsedUrl.SourcePath
        if parsedUrl.TagType = "img" then
            match returnNonNullOrWhitespace parsedUrl.RenamedUrl with
            | Some url ->
                // if found in renamed url, return the source HTML page path
                if url.Equals(imgPath, StringComparison.OrdinalIgnoreCase) then Some sourcePath else None
            | None ->
                match returnNonNullOrWhitespace parsedUrl.DiskPath with
                | Some url ->
                    // if found in disk path, return the source HTML page path
                    if url.Equals(imgPath, StringComparison.OrdinalIgnoreCase) then Some sourcePath else None
                | None -> None
        else None
        
    // For each image, find referencing HTML pages via URL matching
    let imageRefCounts =
        imageFiles
        |> Array.map (fun img ->
            // Count references: look for URLs that end with this filename
            let refCount =
                allUrls
                |> Array.choose (fun u -> returnUrlOrNone u img)
                |> Array.distinct

            (img, refCount))

    imageRefCounts
    |> Array.map (fun (img, referencingPages) ->
        let rawName = Kebab.kebabifyFileName img.Path
        let (RelativePath imgPath) = img.Path
        let ext = Path.GetExtension( imgPath).ToLowerInvariant()

        let (baseName, orphanLineToStore) =
            match referencingPages.Length with
            | 0 ->
                Logging.logOrphanImage imgPath
                let infoToStore = {
                    Id = 0; Step = "Step3"; Category = "image-not-referenced"; RelativePath = imgPath
                    Message = sprintf "Image not referenced: %s" imgPath }
                Kebab.stripCommonParts rawName, Some infoToStore
            | 1 ->
                let refPath = referencingPages.[0]
                let removeHtmlExt htmlName =
                    let (FinalName htmlNameStr) = htmlName
                    if htmlNameStr.EndsWith(".html", StringComparison.OrdinalIgnoreCase) then
                        FinalName (htmlNameStr.Substring(0, htmlNameStr.Length - 5))
                    else
                        htmlName
                match htmlNames |> Map.tryFind (RelativePath refPath) with
                | Some htmlName ->
                    let htmlMinusExt = removeHtmlExt htmlName |> fun (FinalName n) -> n
                    let newName = sprintf "%s-%s" htmlMinusExt (Kebab.stripCommonParts rawName)
                    newName, None
                | None -> Kebab.stripCommonParts rawName, None
            | _ -> Kebab.stripCommonParts rawName, None

        let finalNameWithExt =
            match ext with
            | null | "" -> baseName
            | _ -> sprintf "%s%s" baseName ext

        (img.Path, FinalName finalNameWithExt, orphanLineToStore)
        )
    |> Array.toList

/// Split a name into (stem, extension). If no extension, returns (name, "").
let private splitExtension (name: string) (ext: string) : string * string =
    if not (String.IsNullOrEmpty ext) && name.EndsWith(ext, StringComparison.OrdinalIgnoreCase) then
        name.Substring(0, name.Length - ext.Length), ext
    else
        name, ""

/// Find a unique name by appending -2, -3, etc. before the extension.
let findNameWithSuffix (baseName: FinalName) (ext: string) (existingNames: System.Collections.Generic.HashSet<FinalName>) : FinalName =
    let (FinalName baseStr) = baseName
    let (stem, suffix_ext) = splitExtension baseStr ext
    let mutable suffix = 2
    let mutable newName = FinalName (sprintf "%s-%d%s" stem suffix suffix_ext)
    while existingNames.Contains(newName) do
        suffix <- suffix + 1
        newName <- FinalName (sprintf "%s-%d%s" stem suffix suffix_ext)
    newName

let resolveHtmlDuplicates (htmlNamePairs: (RelativePath * FinalName) list) : (RelativePath * FinalName * FinalName option * AnalysisLogEntity option) list =
    let seen = System.Collections.Generic.HashSet<FinalName>()

    htmlNamePairs
    |> List.map (fun (path, name) ->
        if seen.Contains(name) then
            let pathStr = match path with RelativePath p -> p
            let newName = findNameWithSuffix name ".html" seen
            let (FinalName newNameStr) = newName
            seen.Add(newName) |> ignore
            let nameWarning = {
                Id = 0; Step = "Step3"; Category = "multiple-html-names"; RelativePath = pathStr
                Message = sprintf "Multiple HTML names: %s, using %s" pathStr newNameStr }
            (path, newName, Some name, Some nameWarning)
        else
            seen.Add(name) |> ignore
            (path, name, None, None)
    )

let resolveImageDuplicates (imageNamePairs: (RelativePath * FinalName * AnalysisLogEntity option) list) : (RelativePath * FinalName * FinalName option * AnalysisLogEntity option * AnalysisLogEntity option) list =
    let seen = System.Collections.Generic.HashSet<FinalName>()

    imageNamePairs
    |> List.map (fun (path, name, orphanLog) ->
        let (FinalName nameStr) = name
        let (RelativePath pathStr) = path
        let (finalName, isDuplicate, duplicateWarning) =
            if seen.Contains(name) then
                let ext = Path.GetExtension(pathStr).ToLowerInvariant()
                let newName = findNameWithSuffix name ext seen
                let (FinalName newNameStr) = newName
                Logging.logError "Step3" pathStr (sprintf "Duplicate image name: %s, using: %s" nameStr newNameStr)
                let warning = {
                    Id = 0; Step = "Step3"; Category = "duplicate-image-name"; RelativePath = pathStr
                    Message = sprintf "Duplicate image name: %s, using name: %s" nameStr newNameStr }
                newName, Some name, Some warning
            else
                name, None, None

        seen.Add(finalName) |> ignore
        (path, finalName, isDuplicate, orphanLog, duplicateWarning))

let removeExistingStepData (ctx: PipelineDbContext) =
    if ctx.FinalNames.Any() then
        Logging.logInfo "Step3" "Clearing existing FinalNames..."
        ctx.FinalNames.RemoveRange(ctx.FinalNames) |> ignore
    else
        Logging.logInfo "Step3" "No existing FinalNames to clear."
    
    ctx.SaveChanges() |> ignore

let run (sourceFolder: string) (ctx: PipelineDbContext) =
    let stopwatch = Diagnostics.Stopwatch.StartNew()
    Logging.logInfo "Step3" "Generating final file names..."

    removeExistingStepData ctx

    // Generate HTML names first
    let htmlFiles =
        ctx.ScannedFiles
            .Where(fun f -> f.FileType = "html" && not f.IsExcluded)
            .ToArray()
            |> Array.map entityToScannedFile

    let htmlNamePairs = generateHtmlFinalNames htmlFiles

    let resolvedHtmlNames = resolveHtmlDuplicates htmlNamePairs
    
    let resolvedWarning  =
        resolvedHtmlNames
        |> List.choose (fun (_, _, _, warningOpt) -> warningOpt)
        |> List.toArray
    ctx.AnalysisLogs.AddRange(resolvedWarning) |> ignore

    // Build map for image name generation
    let htmlNamesMap =
        resolvedHtmlNames
        |> List.map (fun (path, name, _, _) -> path, name)
        |> Map.ofList


    let imageFiles =
        ctx.ScannedFiles
            .Where(fun f -> f.FileType = "image" && not f.IsExcluded)
            .ToArray()
            |> Array.map entityToScannedFile

    let allUrls = ctx.ParsedUrls.ToArray() |> Array.map entityToParsedUrl

    // Generate image names
    let imageNamePairs = generateImageFinalNames allUrls imageFiles htmlNamesMap

    // Check for duplicate image names and handle common-part stripping collision
    let resolvedImageNames = resolveImageDuplicates imageNamePairs

    // Store image analysis logs (orphan + duplicate warnings)
    let imageWarnings =
        resolvedImageNames
        |> List.collect (fun (_, _, _, orphanLog, duplicateWarning) ->
            [orphanLog; duplicateWarning] |> List.choose id)
        |> List.toArray
    ctx.AnalysisLogs.AddRange(imageWarnings) |> ignore

    // Store all final names
    for (path, name, duplicateOf, _) in resolvedHtmlNames do
        let (RelativePath pathStr) = path
        let (FinalName nameStr) = name
        let entity: Step3FinalNameEntity =
            { RelativePath = pathStr
              FinalName = nameStr
              FileType = "html"
              DuplicateOf = duplicateOf |> Option.map (fun (FinalName n) -> n) |> Option.defaultValue null }

        ctx.FinalNames.Add(entity) |> ignore

    for (path, name, duplicateOf, _, _) in resolvedImageNames do
        let (RelativePath pathStr) = path
        let (FinalName nameStr) = name
        let entity: Step3FinalNameEntity =
            { RelativePath = pathStr
              FinalName = nameStr
              FileType = "image"
              DuplicateOf = duplicateOf |> Option.map (fun (FinalName n) -> n) |> Option.defaultValue null }

        ctx.FinalNames.Add(entity) |> ignore

    ctx.SaveChanges() |> ignore

    let totalNames = ctx.FinalNames.Count()
    Logging.logInfo "Step3" (sprintf "Generated %d final names" totalNames)

    stopwatch.Stop()

    Logging.logStepSummary
        "Step3"
        (resolvedHtmlNames.Length + resolvedImageNames.Length)
        stopwatch.Elapsed
