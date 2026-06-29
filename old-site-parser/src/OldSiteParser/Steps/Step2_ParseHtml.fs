module OldSiteParser.Steps.Step2_ParseHtml

open System
open System.IO
open System.Linq
open OldSiteParser.Database
open OldSiteParser.Domain.Types
open OldSiteParser.Domain.Mapping
open OldSiteParser.Shared

type Step2Result = { PageContent: Step2RawContentEntity option; ParsedUrls: Step2ParsedUrlEntity list }

let processFile (sourceFolder: string) (entity: Step1ScannedFileEntity) : Step2Result =
    let fullPath = Path.Combine(sourceFolder, entity.RelativePath)
    let relativePath = entity.RelativePath

    try
        let fileContent = File.ReadAllText(fullPath)
        let (content, templateType) = HtmlParsing.extractBodyContent fileContent

        let pageContent: PageContentRecord =
            { Path = RelativePath relativePath
              Content = content
              Template = stringToTemplateType templateType }

        let pageContentEntity = pageContentToEntity pageContent

        // Extract URLs from the content
        let urls = HtmlParsing.extractUrls content

        let urlEntities =
            urls
            |> List.map (fun urlInfo ->
                let isLocal = HtmlParsing.isLocalUrl (unwrapRawUrl urlInfo.RawUrl)

                let serverPath =
                    if isLocal then
                        let (RawUrl rawUrl) = urlInfo.RawUrl
                        Some (Uri.EscapeDataString(rawUrl))
                    else
                        None

                let diskPath =
                    serverPath
                    |> Option.map (fun sp ->
                        sp
                        |> HtmlParsing.stripFragment
                        |> HtmlParsing.stripQueryParams
                        |> HtmlParsing.extractPathPortion)

                let prefixesToAdd = ["500-"; "thumb-"; "300-"]
                let prefixesToRemove = ["500-"; "thumb-"; "300-"]
                let prefixReplacements =
                    [ ("500-", "300-")
                      ("thumb-", "300-")
                      ("300-", "500-") ]

                let fileExists, renamedUrl =
                    match diskPath with
                    | Some dp ->
                        let fileDir = Path.GetDirectoryName(fullPath)
                        let resolvedPath = Path.GetFullPath(Path.Combine(fileDir, dp))

                        if File.Exists(resolvedPath) then
                            Some Exists, None
                        else
                            let fileName = Path.GetFileName(resolvedPath)
                            let dir = Path.GetDirectoryName(resolvedPath)

                            let addPrefixMatch =
                                prefixesToAdd
                                |> List.tryFind (fun prefix ->
                                    File.Exists(Path.Combine(dir, prefix + fileName)))
                                |> Option.map (fun prefix -> prefix + fileName)

                            let removePrefixMatch =
                                prefixesToRemove
                                |> List.tryFind (fun prefix ->
                                    fileName.StartsWith(prefix) &&
                                    File.Exists(Path.Combine(dir, fileName.Substring(prefix.Length))))
                                |> Option.map (fun prefix -> fileName.Substring(prefix.Length))
                            
                            let prefixReplacementMatch =
                                prefixReplacements
                                |> List.tryFind (fun (oldPrefix, newPrefix) ->
                                    fileName.StartsWith(oldPrefix) &&
                                    File.Exists(Path.Combine(dir, newPrefix + fileName.Substring(oldPrefix.Length))))
                                |> Option.map (fun (oldPrefix, newPrefix) -> newPrefix + fileName.Substring(oldPrefix.Length))

                            match   addPrefixMatch
                                    |> Option.orElse removePrefixMatch
                                    |> Option.orElse prefixReplacementMatch with
                            | Some newFileName -> Some Exists, Some newFileName
                            | None -> Some DoesNotExist, None
                    | None -> None, None

                let parsedUrl: ParsedUrl =
                    { SourcePath = RelativePath relativePath
                      RawUrl = urlInfo.RawUrl
                      ServerPath = serverPath
                      DiskPath = diskPath
                      RenamedUrl = renamedUrl
                      TagType = urlInfo.TagType
                      AltText = urlInfo.AltText
                      FileExists = fileExists }

                parsedUrlToEntity parsedUrl)

        { PageContent = Some pageContentEntity
          ParsedUrls = urlEntities }
    with ex ->
        Logging.logError "Step2" relativePath (sprintf "Failed to parse: %s" ex.Message)
        { PageContent = None; ParsedUrls = [] }

let removeExistingStepData (ctx: PipelineDbContext) =
    if ctx.RawPageContents.Any() then
        Logging.logInfo "Step2" "Clearing existing PageContents..."
        ctx.RawPageContents.RemoveRange(ctx.RawPageContents) |> ignore
    else
        Logging.logInfo "Step2" "No existing PageContents to clear."
    
    if ctx.ParsedUrls.Any() then
        Logging.logInfo "Step2" "Clearing existing ParsedUrls..."
        ctx.ParsedUrls.RemoveRange(ctx.ParsedUrls) |> ignore
    else
        Logging.logInfo "Step2" "No existing ParsedUrls to clear."
    
    ctx.SaveChanges() |> ignore

let run (sourceFolder: string) (ctx: PipelineDbContext) =
    let stopwatch = Diagnostics.Stopwatch.StartNew()
    Logging.logInfo "Step2" "Parsing HTML content and extracting URLs..."

    removeExistingStepData ctx

    let htmlFiles =
        ctx.ScannedFiles
            .Where(fun f -> f.FileType = "html" && not f.IsExcluded)
            .ToArray()

    Logging.logInfo "Step2" (sprintf "Processing %d HTML files" htmlFiles.Length)

    ChannelProcessing.runParallel
        htmlFiles
        (fun entity -> processFile sourceFolder entity)
        (fun result ->
            match result.PageContent with
            | Some pc -> ctx.RawPageContents.Add(pc) |> ignore
            | None -> ()
            for url in result.ParsedUrls do
                ctx.ParsedUrls.Add(url) |> ignore)
        Environment.ProcessorCount

    ctx.SaveChanges() |> ignore

    let contentCount = ctx.RawPageContents.Count()
    let urlCount = ctx.ParsedUrls.Count()

    Logging.logInfo "Step2" (sprintf "Extracted content from %d files, %d URLs found" contentCount urlCount)

    stopwatch.Stop()
    Logging.logStepSummary "Step2" htmlFiles.Length stopwatch.Elapsed
