module OldSiteDownloader.DownloadCoordinator

open System
open System.Collections.Generic
open System.Globalization
open System.IO
open Microsoft.Extensions.Logging
open OldSiteDownloader.DownloadRunner
open OldSiteDownloader.Shared
open OldSiteDownloader.Types

type DownloadCoordinatorConfig = {
    OutputDirectory: string
}

type DownloadCoordinatorDependencies = {
    RetrySettings: RetrySettings
    HttpClient: System.Net.Http.HttpClient
    Logger: AppLogger
    DbReader: MailboxProcessor<DbReaderMsg>
    DbWriter: MailboxProcessor<DbWriterMsg>
    MainCoordinator: MailboxProcessor<CoordinatorMsg>
}

let private parseSnapshotTimestamp (timestamp: string) =
        DateTime.ParseExact(timestamp, "yyyyMMddHHmmss", CultureInfo.InvariantCulture)

let latestSnapshot = "20220929040443"
let latestSnapshotDate = parseSnapshotTimestamp latestSnapshot

let private sanitizeSegment (value: string) =
    let invalidCharacters = Path.GetInvalidFileNameChars() |> Set.ofArray

    let cleaned =
        value
        |> Seq.map (fun character -> if invalidCharacters.Contains(character) || character = ':' then '_' else character)
        |> Seq.toArray
        |> String
        |> fun segment -> segment.Trim()

    if String.IsNullOrWhiteSpace(cleaned) then "_" else cleaned

let private insertVariantSuffix (fileName: string) (suffix: string) =
    if String.IsNullOrWhiteSpace(suffix) then
        fileName
    else
        let extension = Path.GetExtension(fileName)
        let stem = Path.GetFileNameWithoutExtension(fileName)

        if String.IsNullOrWhiteSpace(extension) then
            fileName + suffix
        else
            stem + suffix + extension

let private combineSegments (segments: string list) =
    match segments with
    | [] -> ""
    | head :: tail -> tail |> List.fold (fun current segment -> Path.Combine(current, segment)) head

let private buildDownloadPlan outputDirectory (OriginalUrl originalUrl) =
    match Uri.TryCreate(originalUrl, UriKind.Absolute) with
    | false, _ -> invalidOp $"Cannot build output path for invalid url '{originalUrl}'."
    | true, uri ->
        let segments =
            uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            |> Array.map sanitizeSegment
            |> Array.toList

        let needsIndexDocument =
            String.IsNullOrWhiteSpace(uri.AbsolutePath)
            || uri.AbsolutePath = "/"
            || uri.AbsolutePath.EndsWith("/", StringComparison.Ordinal)
            || (Path.GetFileName(uri.AbsolutePath) |> Path.HasExtension |> not)

        let relativeSegments, fileName =
            if needsIndexDocument then
                segments, "index.html"
            elif List.isEmpty segments then
                [], "index.bin"
            else
                segments |> List.take (segments.Length - 1), segments |> List.last

        let querySuffix = ""
        let schemeSuffix = ""
        let portSuffix = ""

        let finalFileName =
            fileName
            |> insertVariantSuffix querySuffix
            |> insertVariantSuffix schemeSuffix
            |> insertVariantSuffix portSuffix

        let relativePath =
            combineSegments (relativeSegments @ [ finalFileName ])
            |> fun x -> x.Replace('\\', '/')

        { OutputPath = Path.Combine(outputDirectory, relativePath)
          RelativePath = RelativePath relativePath }

let private selectSnapshotToDownload
    (logger: AppLogger)
    (latestSnapshot: string)
    (latestSnapshotDate: DateTime)
    (originalUrlStr: string)
    (snapshots: (MimeType * SnapshotStatusCode * SnapshotTimestamp) list) =
    let successfulFileSnapshots =
        snapshots
        |> List.map (fun (MimeType mimeType, SnapshotStatusCode statusCode, SnapshotTimestamp timestamp) ->
            {| MimeType = mimeType
               StatusCode = statusCode
               Timestamp = timestamp
               TimestampVal = DateTime.ParseExact(timestamp, "yyyyMMddHHmmss", CultureInfo.InvariantCulture) |}
        )
        |> List.filter (fun snapshot ->
            snapshot.MimeType <> "warc/revisit"
            && snapshot.MimeType <> "unk"
            && snapshot.StatusCode.StartsWith("2", StringComparison.Ordinal)
        )
        |> List.sortByDescending (fun snapshot -> snapshot.TimestampVal)

    let snapshotsOlderOrEqualToLatest =
        successfulFileSnapshots
        |> List.filter (fun snapshot -> snapshot.TimestampVal <= latestSnapshotDate)

    let snapshotsNewerThanLatest =
        successfulFileSnapshots
        |> List.filter (fun snapshot -> snapshot.TimestampVal > latestSnapshotDate)
        |> List.sortBy (fun snapshot -> snapshot.TimestampVal)

    match snapshotsOlderOrEqualToLatest with
    | firstSameOrOlder :: _ -> Some (SnapshotTimestamp firstSameOrOlder.Timestamp)
    | [] ->
        match snapshotsNewerThanLatest with
        | [] -> None
        | firstNewer :: _ ->
            logger.LogInformation(
                "No snapshots older than or equal to latest known snapshot {LatestSnapshot} for {OriginalUrl}; considering newer snapshot from {SnapshotTimestamp}.",
                latestSnapshot,
                originalUrlStr,
                firstNewer.Timestamp)
            Some (SnapshotTimestamp firstNewer.Timestamp)

let createDownloadCoordinatorActor
    (config: DownloadCoordinatorConfig)
    (dependencies: DownloadCoordinatorDependencies) =
    
    let noSnapshotTimestamp = SnapshotTimestamp "N/A"
    
    MailboxProcessor<DownloadCoordinatorMsg>.Start(fun inbox ->
        let runner = lazy (createDownloadRunnerActor dependencies.RetrySettings dependencies.HttpClient dependencies.Logger dependencies.DbWriter inbox)

        let rec loop () = async {
            let! msg = inbox.Receive()
            match msg with
            | ProcessDownloadTarget (archivedUrlId, originalUrl, downloadState) ->
                let (OriginalUrl originalUrlStr) = originalUrl
                let latestSnapshotDate = parseSnapshotTimestamp latestSnapshot
                if not (UrlFilter.isSupportedStaticSiteUrl originalUrlStr) then
                    dependencies.DbWriter.PostAndReply(fun rc -> MarkDownloadSkipped(archivedUrlId, noSnapshotTimestamp, DownloadState.SkippedUnsupported, ErrorMessage "Skipped because the URL does not look like a standard static site asset.", rc))
                    dependencies.Logger.LogInformation("Skipping unsupported URL {OriginalUrl}", originalUrlStr)
                    dependencies.MainCoordinator.Post(DownloadTargetProcessed archivedUrlId)
                else
                    let plan = buildDownloadPlan config.OutputDirectory originalUrl
                    let (RelativePath relativePathStr) = plan.RelativePath
                    let outputPath = Path.Combine(config.OutputDirectory, relativePathStr)

                    if File.Exists(outputPath) then
                        if downloadState = DownloadState.Completed then
                            dependencies.Logger.LogInformation("File already exists for {OriginalUrl} at {OutputPath}; skipping download.", originalUrlStr, relativePathStr)
                        else
                            let error = "File already exists but download state is not completed. Must be duplicate final names for url " + originalUrlStr
                            let errorMessage = ErrorMessage error
                            dependencies.DbWriter.PostAndReply(fun rc -> MarkDownloadFailed(archivedUrlId, noSnapshotTimestamp, plan.RelativePath, errorMessage, rc))
                            dependencies.Logger.LogError(error)
                        dependencies.MainCoordinator.Post(DownloadTargetProcessed archivedUrlId)
                    else
                        let! snapshots = dependencies.DbReader.PostAndAsyncReply(fun rc -> LoadSnapshotsForUrl(archivedUrlId, rc))

                        match selectSnapshotToDownload dependencies.Logger latestSnapshot latestSnapshotDate originalUrlStr snapshots with
                        | None ->
                            dependencies.DbWriter.PostAndReply(fun rc -> MarkDownloadSkipped(archivedUrlId, noSnapshotTimestamp, DownloadState.SkippedNoSuitableSnapshot, ErrorMessage "No suitable snapshot found; skipping download.", rc))
                            dependencies.Logger.LogWarning("No suitable snapshot found for {OriginalUrl}; skipping download.", originalUrlStr)
                            dependencies.MainCoordinator.Post(DownloadTargetProcessed archivedUrlId)
                        | Some timestamp ->
                            runner.Value.Post(RunDownload(archivedUrlId, originalUrl, timestamp, plan))
                            do! inbox.Scan(function
                                | DownloadRunCompleted _ -> Some (async { return () })
                                | _ -> None)
                            dependencies.MainCoordinator.Post(DownloadTargetProcessed archivedUrlId)

                return! loop ()

            | DownloadRunCompleted _ ->
                // Handled via inbox.Scan; should not normally reach here
                return! loop ()
        }

        loop ()
    )

let createDownloadCoordinatorPool
    (createWorker: unit -> MailboxProcessor<DownloadCoordinatorMsg>)
    (concurrency: int) =
    let actors = Array.init concurrency (fun _ -> createWorker ())
    let mutable counter = 0

    MailboxProcessor<DownloadCoordinatorMsg>.Start(fun inbox ->
        let rec loop () = async {
            let! msg = inbox.Receive()
            match msg with
            | ProcessDownloadTarget _ ->
                let index = counter % actors.Length
                counter <- counter + 1
                actors.[index].Post(msg)
            | DownloadRunCompleted _ ->
                ()
            return! loop ()
        }
        loop ()
    )
