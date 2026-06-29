module OldSiteParser.Steps.Step8_OutputTables

open System
open System.IO
open System.Linq
open System.Text.Json
open OldSiteParser.Database
open OldSiteParser.Shared

let private firstValueOrNull (values: string seq) =
    values
    |> Seq.tryFind (fun value -> not (String.IsNullOrWhiteSpace(value)))
    |> Option.defaultValue null

let private removeExistingSiteData (siteDataCtx: SiteDataContext) =
    if siteDataCtx.NonHtmlFiles.Any() then
        Logging.logInfo "Step8" "Clearing existing site data NonHtmlFiles..."
        siteDataCtx.NonHtmlFiles.RemoveRange(siteDataCtx.NonHtmlFiles) |> ignore

    if siteDataCtx.Posts.Any() then
        Logging.logInfo "Step8" "Clearing existing site data Posts..."
        siteDataCtx.Posts.RemoveRange(siteDataCtx.Posts) |> ignore

    if siteDataCtx.PostTags.Any() then
        Logging.logInfo "Step8" "Clearing existing site data PostTags..."
        siteDataCtx.PostTags.RemoveRange(siteDataCtx.PostTags) |> ignore

    if siteDataCtx.RelatedPages.Any() then
        Logging.logInfo "Step8" "Clearing existing site data RelatedPages..."
        siteDataCtx.RelatedPages.RemoveRange(siteDataCtx.RelatedPages) |> ignore

    if siteDataCtx.PostIngredients.Any() then
        Logging.logInfo "Step8" "Clearing existing site data PostIngredients..."
        siteDataCtx.PostIngredients.RemoveRange(siteDataCtx.PostIngredients) |> ignore

    if siteDataCtx.PostReceipts.Any() then
        Logging.logInfo "Step8" "Clearing existing site data PostReceipts..."
        siteDataCtx.PostReceipts.RemoveRange(siteDataCtx.PostReceipts) |> ignore

    siteDataCtx.SaveChanges() |> ignore

let run (siteDataDatabasePath: string) (_workingDir: string) (ctx: PipelineDbContext) =
    let stopwatch = Diagnostics.Stopwatch.StartNew()
    Logging.logInfo "Step8" (sprintf "Materializing site data database at %s" siteDataDatabasePath)

    let siteDataDir = Path.GetDirectoryName(siteDataDatabasePath)
    if not (String.IsNullOrWhiteSpace(siteDataDir)) then
        Directory.CreateDirectory(siteDataDir) |> ignore

    use siteDataCtx = SiteDataContextFactory.create siteDataDatabasePath
    removeExistingSiteData siteDataCtx

    let scannedFiles =
        ctx.ScannedFiles
            .Where(fun file -> not file.IsExcluded)
            .ToArray()

    let finalNames = ctx.FinalNames.ToArray()
    let preTidyMetadata = ctx.PreTidyMetadata.ToArray()
    let postTidyMetadata = ctx.PostTidyMetadata.ToArray()
    let contents = ctx.Step6Contents.ToArray()
    let receipts = ctx.Receipts.ToArray()
    let relatedPages = ctx.RelatedPages.ToArray()
    let receiptIngredients = ctx.Step7ReceiptIngredients.ToArray()

    let finalNameByRelativePath =
        finalNames
        |> Array.map (fun entry -> entry.RelativePath, entry)
        |> Map.ofArray

    let finalNameLookup =
        let byOriginal = finalNames |> Array.map (fun entry -> entry.RelativePath, entry)
        let byFinal = finalNames |> Array.map (fun entry -> entry.FinalName, entry)
        Array.append byOriginal byFinal
        |> Map.ofArray

    let headingByRelativePath =
        preTidyMetadata
        |> Array.filter (fun entry -> String.Equals(entry.TagName, "heading", StringComparison.OrdinalIgnoreCase))
        |> Array.groupBy (fun entry -> entry.RelativePath)
        |> Array.map (fun (relativePath, entries) -> relativePath, (entries |> Seq.map (fun entry -> entry.TextContent) |> firstValueOrNull))
        |> Map.ofArray

    let synonymByFinalPath =
        postTidyMetadata
        |> Array.filter (fun entry -> String.Equals(entry.MetadataKey, "synonym", StringComparison.OrdinalIgnoreCase))
        |> Array.groupBy (fun entry -> entry.RelativePath)
        |> Array.map (fun (finalPath, entries) -> finalPath, (entries |> Seq.map (fun entry -> entry.MetadataValue) |> firstValueOrNull))
        |> Map.ofArray

    let contentByFinalPath =
        contents
        |> Array.map (fun entry -> entry.FinalName, entry.Content)
        |> Map.ofArray

    let receiptCountByFinalPath =
        receipts
        |> Array.groupBy (fun entry -> entry.RelativePath)
        |> Array.map (fun (finalPath, entries) -> finalPath, entries.Length)
        |> Map.ofArray

    // Determine target directory for static site data files
    let dataDir =
        if not (String.IsNullOrWhiteSpace(siteDataDir)) && (siteDataDir.Contains("old-site-parser") || siteDataDir.Contains("out")) then
            Path.GetFullPath(Path.Combine(siteDataDir, "..", "..", "site", "data"))
        else
            Path.Combine(siteDataDir, "site", "data")

    Logging.logInfo "Step8" (sprintf "Writing site data files to %s" dataDir)
    if Directory.Exists(dataDir) then
        Directory.Delete(dataDir, true)
    Directory.CreateDirectory(dataDir) |> ignore

    let ensureParentDir (filePath: string) =
        let dir = Path.GetDirectoryName(filePath)
        if not (String.IsNullOrWhiteSpace(dir)) then
            Directory.CreateDirectory(dir) |> ignore

    // Pre-aggregate data for files output
    let postTags =
        preTidyMetadata
        |> Array.filter (fun entry -> not (String.Equals(entry.TagName, "heading", StringComparison.OrdinalIgnoreCase)))
        |> Array.choose (fun tag ->
            match finalNameLookup |> Map.tryFind tag.RelativePath with
            | Some finalName when String.Equals(finalName.FileType, "html", StringComparison.OrdinalIgnoreCase) ->
                Some (finalName.FinalName, {| tagName = tag.TagName; tagValue = tag.TextContent |})
            | _ -> None)
        |> Array.groupBy fst
        |> Array.map (fun (k, g) -> k, g |> Array.map snd)
        |> Map.ofArray

    let postRelatedPages =
        relatedPages
        |> Array.map (fun relatedPage ->
            relatedPage.SourcePage,
            {| relationDescription = relatedPage.RelationDescription
               relatedPageText = relatedPage.RelatedPageText
               relatedPageUrl = relatedPage.RelatedPageUrl |})
        |> Array.groupBy fst
        |> Array.map (fun (k, g) -> k, g |> Array.map snd)
        |> Map.ofArray

    let postIngredients =
        receiptIngredients
        |> Array.distinctBy (fun entry -> entry.RelativePath, entry.IngredientName)
        |> Array.map (fun ingredient -> ingredient.RelativePath, ingredient.IngredientName)
        |> Array.groupBy fst
        |> Array.map (fun (k, g) -> k, g |> Array.map snd)
        |> Map.ofArray

    let postReceipts =
        receipts
        |> Array.map (fun receipt ->
            receipt.RelativePath,
            {| receiptId = receipt.ReceiptId
               receiptSource = receipt.ReceiptSource
               receiptText = receipt.ReceiptText |})
        |> Array.groupBy fst
        |> Array.map (fun (k, g) -> k, g |> Array.map snd)
        |> Map.ofArray

    let jsonOptions = JsonSerializerOptions(WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    for scannedFile in scannedFiles |> Array.filter (fun file -> not (String.Equals(file.FileType, "html", StringComparison.OrdinalIgnoreCase))) do
        let finalPath =
            finalNameByRelativePath
            |> Map.tryFind scannedFile.RelativePath
            |> Option.map (fun entry -> entry.FinalName)
            |> Option.defaultValue null

        siteDataCtx.NonHtmlFiles.Add(
            { Id = 0
              InitialPath = scannedFile.RelativePath
              FinalPath = finalPath })
        |> ignore

    for finalName in finalNames |> Array.filter (fun entry -> String.Equals(entry.FileType, "html", StringComparison.OrdinalIgnoreCase)) do
        let scannedFile =
            scannedFiles
            |> Array.find (fun file -> String.Equals(file.RelativePath, finalName.RelativePath, StringComparison.OrdinalIgnoreCase))

        let rawTitle = scannedFile.TitleTag
        let title = if isNull rawTitle then null else Kebab.stripTitlePrefix rawTitle
        let heading =
            headingByRelativePath
            |> Map.tryFind finalName.FinalName
            |> Option.orElse (headingByRelativePath |> Map.tryFind finalName.RelativePath)
            |> Option.map Kebab.stripTitlePrefix
            |> Option.defaultValue null
        let synonym = synonymByFinalPath |> Map.tryFind finalName.FinalName |> Option.defaultValue null
        let content = contentByFinalPath |> Map.tryFind finalName.FinalName |> Option.defaultValue null
        let receiptCount = receiptCountByFinalPath |> Map.tryFind finalName.FinalName |> Option.defaultValue 0

        // DB Save
        siteDataCtx.Posts.Add(
            { FinalPath = finalName.FinalName
              Title = title
              EarliestSnapshot = scannedFile.EarliestSnapshot
              UsedSnapshot = scannedFile.UsedSnapshot
              ReceiptCount = receiptCount
              Heading = heading
              Synonym = synonym
              Content = content })
        |> ignore

        // Files Save
        let relativeJsonPath = 
            if finalName.FinalName.EndsWith(".html", StringComparison.OrdinalIgnoreCase) then
                finalName.FinalName.Substring(0, finalName.FinalName.Length - 5) + ".json"
            else
                finalName.FinalName + ".json"

        let htmlPath = Path.Combine(dataDir, finalName.FinalName)
        let jsonPath = Path.Combine(dataDir, relativeJsonPath)

        ensureParentDir htmlPath
        ensureParentDir jsonPath

        File.WriteAllText(htmlPath, (if isNull content then "" else content), System.Text.Encoding.UTF8)

        let tags = postTags |> Map.tryFind finalName.FinalName |> Option.defaultValue [||]
        let related = postRelatedPages |> Map.tryFind finalName.FinalName |> Option.defaultValue [||]
        let ingredients = postIngredients |> Map.tryFind finalName.FinalName |> Option.defaultValue [||]
        let receiptsForThis = postReceipts |> Map.tryFind finalName.FinalName |> Option.defaultValue [||]

        let postJsonData =
            {| finalPath = finalName.FinalName
               title = title
               earliestSnapshot = scannedFile.EarliestSnapshot
               usedSnapshot = scannedFile.UsedSnapshot
               receiptCount = receiptCount
               heading = heading
               synonym = synonym
               tags = tags
               relatedPages = related
               ingredients = ingredients
               receipts = receiptsForThis |}

        let jsonStr = JsonSerializer.Serialize(postJsonData, jsonOptions)
        File.WriteAllText(jsonPath, jsonStr, System.Text.Encoding.UTF8)

    for tag in preTidyMetadata |> Array.filter (fun entry -> not (String.Equals(entry.TagName, "heading", StringComparison.OrdinalIgnoreCase))) do
        match finalNameLookup |> Map.tryFind tag.RelativePath with
        | Some finalName when String.Equals(finalName.FileType, "html", StringComparison.OrdinalIgnoreCase) ->
            siteDataCtx.PostTags.Add(
                { Id = 0
                  FinalPath = finalName.FinalName
                  TagName = tag.TagName
                  TagValue = tag.TextContent })
            |> ignore
        | _ -> ()

    for relatedPage in relatedPages do
        siteDataCtx.RelatedPages.Add(
            { Id = 0
              FinalPath = relatedPage.SourcePage
              RelationDescription = relatedPage.RelationDescription
              RelatedPageText = relatedPage.RelatedPageText
              RelatedPageUrl = relatedPage.RelatedPageUrl })
        |> ignore

    for ingredient in receiptIngredients |> Array.distinctBy (fun entry -> entry.RelativePath, entry.IngredientName) do
        siteDataCtx.PostIngredients.Add(
            { Id = 0
              FinalPath = ingredient.RelativePath
              IngredientName = ingredient.IngredientName })
        |> ignore

    for receipt in receipts do
        siteDataCtx.PostReceipts.Add(
            { Id = 0
              FinalPath = receipt.RelativePath
              ReceiptId = receipt.ReceiptId
              ReceiptSource = receipt.ReceiptSource
              ReceiptText = receipt.ReceiptText })
        |> ignore

    // Assets json output
    let assetsData =
        scannedFiles
        |> Array.filter (fun file -> not (String.Equals(file.FileType, "html", StringComparison.OrdinalIgnoreCase)))
        |> Array.map (fun scannedFile ->
            let finalPath =
                finalNameByRelativePath
                |> Map.tryFind scannedFile.RelativePath
                |> Option.map (fun entry -> entry.FinalName)
                |> Option.defaultValue null
            {| initialPath = scannedFile.RelativePath
               finalPath = finalPath |})

    let assetsJsonPath = Path.Combine(dataDir, "assets.json")
    let assetsJsonStr = JsonSerializer.Serialize(assetsData, jsonOptions)
    File.WriteAllText(assetsJsonPath, assetsJsonStr, System.Text.Encoding.UTF8)

    siteDataCtx.SaveChanges() |> ignore

    Logging.logInfo
        "Step8"
        (sprintf
            "Stored %d non-html files, %d posts, %d post tags, %d related pages, %d post ingredients, and %d post receipts"
                (siteDataCtx.NonHtmlFiles.Count())
                (siteDataCtx.Posts.Count())
                (siteDataCtx.PostTags.Count())
                (siteDataCtx.RelatedPages.Count())
                (siteDataCtx.PostIngredients.Count())
                (siteDataCtx.PostReceipts.Count()))

    stopwatch.Stop()
    Logging.logStepSummary "Step8" (siteDataCtx.Posts.Count()) stopwatch.Elapsed