open System
open System.Globalization
open System.IO
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open OldSiteDownloader.CdxLookup
open OldSiteDownloader.Database
open OldSiteDownloader.DownloadCoordinator
open OldSiteDownloader.Shared
open OldSiteDownloader.Types
open Serilog
open Serilog.Extensions.Logging

let siteBase = "www.foodsofengland.co.uk"

do Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)

let ascendFileSystemUntillFindRoot (startPath: string) =
    let mutable currentPath = startPath
    
    let dirContainsSolutionFile (path: string) =
         let files = Directory.GetFiles(path, "old-site-downloader.slnx")
         
         files
         |> Seq.isEmpty
         |> not
    
    while not (dirContainsSolutionFile currentPath) do
        let parent = Directory.GetParent(currentPath)
        match parent with
        | null -> invalidOp "Could not find solution root in any parent directory."
        | info ->
            currentPath <- info.FullName
    currentPath

let appRoot = ascendFileSystemUntillFindRoot (Directory.GetCurrentDirectory())
let dataDirectory = Path.Combine(appRoot, "data")
let logsDirectory = Path.Combine(dataDirectory, "logs")
let outputDirectory = Path.Combine(appRoot, "out")
let databasePath = Path.Combine(dataDirectory, "wayback-state.db")
let maxHttpAttempts = 8
let initialHttpBackoff = TimeSpan.FromSeconds(5.0)
let rateLimitBackoff = TimeSpan.FromSeconds(10.0)
let downloadConcurrency = 16
// this gets rate limited more aggressively
let cdxLookupConcurrency = 3
let dbReaderConcurrency = 8
let retrySettings: RetrySettings = { MaxAttempts = maxHttpAttempts; InitialBackoff = initialHttpBackoff; RateLimitBackoff = rateLimitBackoff }

// ── Pure helpers ──

let ensureDirectory (path: string) =
    Directory.CreateDirectory(path) |> ignore

let writeMetadataFile (dbOptions: Microsoft.EntityFrameworkCore.DbContextOptions<WaybackDbContext>) (outputDirectory: string) =
    use db = createDbContext dbOptions

    let earliestSnapshots =
        loadEarliestSnapshotMetadata db
        |> dict

    let usedSnapshots =
        loadUsedSnapshotMetadata db
        |> dict

    let metadata = {| earliestSnapshots = earliestSnapshots; usedSnapshots = usedSnapshots |}
    let metadataPath = Path.Combine(outputDirectory, "old-site.metadata.json")
    let serializerOptions = JsonSerializerOptions(WriteIndented = true)
    let json = JsonSerializer.Serialize(metadata, serializerOptions)
    File.WriteAllText(metadataPath, json, UTF8Encoding(false))

let createHttpClient () =
    let handler = new HttpClientHandler()
    handler.AutomaticDecompression <- DecompressionMethods.All
    handler.AllowAutoRedirect <- false

    let client = new HttpClient(handler)
    client.Timeout <- TimeSpan.FromMinutes(5.0)
    client.DefaultRequestHeaders.UserAgent.ParseAdd("old-site-downloader/1.0")
    client


let createLoggerFactory (logFilePath: string) =
    let serilogLogger =
        LoggerConfiguration()
            //.MinimumLevel.Debug()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File(logFilePath, shared = true)
            .CreateLogger()

    new SerilogLoggerFactory(serilogLogger, true)

// ── Coordinator actor ──
// Orchestrates the full pipeline and tracks when every URL has been processed.

type CoordinatorPhase =
    | NotStarted
    | HarvestingSnapshots
    | CoordinatingDownloads
    | Completed

let createCoordinatorActor
    (logger: AppLogger)
    (dbWriter: MailboxProcessor<DbWriterMsg>)
    (dbReader: MailboxProcessor<DbReaderMsg>)
    (cdxLookup: MailboxProcessor<CdxLookupMsg>)
    (downloadCoordinatorFactory: MailboxProcessor<CoordinatorMsg> -> MailboxProcessor<DownloadCoordinatorMsg>)
    (completionTcs: TaskCompletionSource)
    =
    MailboxProcessor<CoordinatorMsg>.Start(fun inbox ->
        let downloadCoordinator = lazy (downloadCoordinatorFactory inbox)
        let mutable pendingSnapshotHarvests = 0
        let mutable pendingDownloadTargets = 0
        let mutable coordinatorPhase = CoordinatorPhase.NotStarted

        let isInPhase expectedPhase =
            coordinatorPhase = expectedPhase

        let ensurePhase expectedPhase messageName =
            if isInPhase expectedPhase then
                true
            else
                logger.LogWarning(
                    "Coordinator: ignoring {MessageName} while in phase {CurrentPhase}; expected {ExpectedPhase}.",
                    messageName,
                    coordinatorPhase,
                    expectedPhase)
                false

        let rec loop () = async {
            let! msg = inbox.Receive()
            match msg with
            | StartPipeline ->
                if ensurePhase CoordinatorPhase.NotStarted "StartPipeline" then
                    logger.LogInformation("Coordinator: starting pipeline.")

                    // Phase 1: URL discovery
                    let! hasUrls = dbReader.PostAndAsyncReply DbReaderMsg.HasAnyUrls
                    if hasUrls then
                        logger.LogInformation("Coordinator: URLs already discovered; skipping URL discovery.")
                    else
                        logger.LogInformation("Coordinator: discovering URLs via CDX.")
                        let! rows = cdxLookup.PostAndAsyncReply DiscoverUrls
                        let! _ids = dbWriter.PostAndAsyncReply(fun rc -> StoreDiscoveredUrls(rows, rc))
                        ()

                    coordinatorPhase <- CoordinatorPhase.HarvestingSnapshots

                    // Phase 2: Snapshot harvesting – kick off all in parallel via CdxLookup
                    let! urls = dbReader.PostAndAsyncReply DbReaderMsg.LoadUrlsForSnapshotDiscovery
                    logger.LogInformation("Coordinator: inspecting snapshot state for {UrlCount} archived urls.", urls.Length)

                    let mutable harvestsStarted = 0

                    for archivedUrlId, originalUrl, snapshotDiscoveryState in urls do
                        let (OriginalUrl originalUrlStr) = originalUrl
                        if not (UrlFilter.isSupportedStaticSiteUrl originalUrlStr) then
                            dbWriter.PostAndReply(fun rc -> MarkSnapshotDiscoveryState(archivedUrlId, SnapshotDiscoveryState.NotASiteUrl, ErrorMessage "Skipped snapshot discovery because URL is not a supported static site URL.", rc))
                            dbWriter.PostAndReply(fun rc -> MarkDownloadSkipped(archivedUrlId, SnapshotTimestamp "N/A", DownloadState.SkippedUnsupported, ErrorMessage "Skipped download because URL is not a supported static site URL.", rc))
                            logger.LogInformation("Skipping snapshot discovery for {OriginalUrl}; URL is not a supported site URL.", originalUrlStr)
                        elif snapshotDiscoveryState = SnapshotDiscoveryState.NotASiteUrl then
                            logger.LogInformation("Skipping snapshot discovery for {OriginalUrl}; previously recorded as unsupported site URL.", originalUrlStr)
                        else
                            let! existingCount = dbReader.PostAndAsyncReply(fun rc -> DbReaderMsg.GetSnapshotCountForUrl(archivedUrlId, rc))

                            if existingCount > 0 then
                                logger.LogInformation("Skipping snapshot discovery for {OriginalUrl}; {SnapshotCount} snapshots already stored.", originalUrlStr, existingCount)
                            elif snapshotDiscoveryState = SnapshotDiscoveryState.NoSnapshots then
                                logger.LogInformation("Skipping snapshot discovery for {OriginalUrl}; previously recorded no snapshots.", originalUrlStr)
                            else
                                harvestsStarted <- harvestsStarted + 1
                                pendingSnapshotHarvests <- pendingSnapshotHarvests + 1

                                // Kick off the CDX lookup; on reply, post result back to coordinator
                                let harvest = async {
                                    logger.LogInformation("Fetching snapshots for {OriginalUrl}", originalUrlStr)
                                    let! result = cdxLookup.PostAndAsyncReply(fun rc -> FetchSnapshots(originalUrl, rc))

                                    match result with
                                    | Ok rows ->
                                        let! storeResult = dbWriter.PostAndAsyncReply(fun rc -> StoreSnapshots(archivedUrlId, originalUrl, rows, rc))
                                        match storeResult with
                                        | Ok () ->
                                            logger.LogInformation("Stored {SnapshotCount} snapshots for {OriginalUrl}", rows.Length, originalUrlStr)
                                        | Error errMsg ->
                                            dbWriter.PostAndReply(fun rc -> MarkSnapshotDiscoveryState(archivedUrlId, SnapshotDiscoveryState.Failed, ErrorMessage errMsg, rc))
                                            logger.LogError("Snapshot storage failed for {OriginalUrl}: {Error}", originalUrlStr, errMsg)
                                    | Error errMsg ->
                                        dbWriter.PostAndReply(fun rc -> MarkSnapshotDiscoveryState(archivedUrlId, SnapshotDiscoveryState.Failed, ErrorMessage errMsg, rc))
                                        logger.LogError("Snapshot discovery failed for {OriginalUrl}: {Error}", originalUrlStr, errMsg)

                                    inbox.Post(SnapshotHarvestDone(archivedUrlId, originalUrl))
                                }
                                Async.Start harvest

                    if harvestsStarted = 0 then
                        logger.LogInformation("Coordinator: no snapshot harvests needed.")
                        inbox.Post AllSnapshotsHarvested

                return! loop ()

            | SnapshotHarvestDone (archivedUrlId, originalUrl) ->
                if ensurePhase CoordinatorPhase.HarvestingSnapshots "SnapshotHarvestDone" then
                    let (OriginalUrl originalUrlStr) = originalUrl
                    pendingSnapshotHarvests <- pendingSnapshotHarvests - 1
                    logger.LogDebug("Coordinator: snapshot harvest done for {OriginalUrl}. {Remaining} remaining.", originalUrlStr, pendingSnapshotHarvests)
                    if pendingSnapshotHarvests = 0 then
                        inbox.Post AllSnapshotsHarvested
                return! loop ()

            | AllSnapshotsHarvested ->
                if ensurePhase CoordinatorPhase.HarvestingSnapshots "AllSnapshotsHarvested" then
                    logger.LogInformation("Coordinator: all snapshot harvests complete. Starting download phase.")
                    coordinatorPhase <- CoordinatorPhase.CoordinatingDownloads

                    let! targets = dbReader.PostAndAsyncReply LoadUrlsForDownload
                    logger.LogInformation("Coordinator: {TargetCount} download targets.", targets.Length)

                    pendingDownloadTargets <- targets.Length

                    for archivedUrlId, originalUrl, downloadState in targets do
                        downloadCoordinator.Value.Post(ProcessDownloadTarget(archivedUrlId, originalUrl, downloadState))

                    if targets.Length = 0 then
                        inbox.Post Shutdown
                else
                    ()
                return! loop ()

            | DownloadTargetProcessed _archivedUrlId ->
                if ensurePhase CoordinatorPhase.CoordinatingDownloads "DownloadTargetProcessed" then
                    pendingDownloadTargets <- pendingDownloadTargets - 1
                    logger.LogDebug("Coordinator: download target processed. {Remaining} remaining.", pendingDownloadTargets)
                    if pendingDownloadTargets = 0 then
                        inbox.Post Shutdown
                return! loop ()

            | UrlDiscoveryDone ->
                logger.LogWarning("Coordinator: UrlDiscoveryDone is not used in phase {CurrentPhase}.", coordinatorPhase)
                return! loop ()

            | Shutdown ->
                match coordinatorPhase with
                | CoordinatorPhase.CoordinatingDownloads
                | CoordinatorPhase.Completed ->
                    let! urlCount, snapshotCount = dbReader.PostAndAsyncReply DbReaderMsg.GetSummary
                    logger.LogInformation(
                        "Archive sync complete. Urls: {UrlCount}, Snapshots: {SnapshotCount}.",
                        urlCount,
                        snapshotCount)
                    coordinatorPhase <- CoordinatorPhase.Completed
                    completionTcs.TrySetResult() |> ignore
                    // Actor stops – no recursive call
                | _ ->
                    logger.LogWarning("Coordinator: ignoring Shutdown while in phase {CurrentPhase}.", coordinatorPhase)
                    return! loop ()
        }
        loop ()
    )

// ── Entry point ──

let runAsync () =
    task {
        ensureDirectory dataDirectory
        ensureDirectory logsDirectory
        ensureDirectory outputDirectory

        let timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
        let logFilePath = Path.Combine(logsDirectory, "run-" + timestamp + ".log")
        use loggerFactory = createLoggerFactory logFilePath
        let logger = loggerFactory.CreateLogger("WaybackDownloader")
        let dbOptions = createDbOptions databasePath loggerFactory

        use initDb = createDbContext dbOptions
        initDb.Database.EnsureCreated() |> ignore

        logger.LogInformation("Starting archive download for {SiteBase}.", siteBase)
        logger.LogInformation("SQLite database: {DatabasePath}", databasePath)
        logger.LogInformation("Output directory: {OutputDirectory}", outputDirectory)
        logger.LogInformation("Log file: {LogFilePath}", logFilePath)

        use httpClient = createHttpClient ()

        let completionTcs = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)

        let dbReader =
            createDbReaderPool
                (fun () -> createDbReaderActor dbOptions logger)
                dbReaderConcurrency
        let dbWriter = createDbWriterActor dbOptions logger
        let cdxLookup =
            createCdxLookupPool
                (fun () -> createCdxLookupActor retrySettings siteBase httpClient logger)
                cdxLookupConcurrency

        let downloadCoordinatorConfig: DownloadCoordinatorConfig =
            { OutputDirectory = outputDirectory }

        let coordinator =
            createCoordinatorActor logger dbWriter dbReader cdxLookup
                (fun coordInbox ->
                    let dependencies: DownloadCoordinatorDependencies =
                        { RetrySettings = retrySettings
                          HttpClient = httpClient
                          Logger = logger
                          DbReader = dbReader
                          DbWriter = dbWriter
                          MainCoordinator = coordInbox }

                    createDownloadCoordinatorPool
                        (fun () -> createDownloadCoordinatorActor downloadCoordinatorConfig dependencies)
                        downloadConcurrency)
                completionTcs

        try
            coordinator.Post StartPipeline
            do! completionTcs.Task
            writeMetadataFile dbOptions outputDirectory
        with ex ->
            logger.LogError(ex, "Archive sync failed.")
            return raise ex
    }

[<EntryPoint>]
let main _ =
    try
        runAsync().GetAwaiter().GetResult()
        0
    with ex ->
        Console.Error.WriteLine(ex)
        1
