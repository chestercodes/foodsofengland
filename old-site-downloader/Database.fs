module OldSiteDownloader.Database

open System
open System.Globalization
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.Logging
open OldSiteDownloader.Types

// ── Entity types ──

[<AllowNullLiteral>]
type ArchivedUrl() =
    member val Id = 0 with get, set
    member val OriginalUrl = "" with get, set
    member val SnapshotDiscoveryState = "Pending" with get, set
    member val SnapshotCount = 0 with get, set
    member val EarliestSnapshotId = "" with get, set
    member val DownloadState = "Pending" with get, set
    member val DownloadedRelativePath = "" with get, set
    member val DownloadedSnapshotId = "" with get, set
    member val DownloadedTextEncoding: string = null with get, set
    member val LastError = "" with get, set

[<AllowNullLiteral>]
type ArchivedSnapshot() =
    member val Id = 0 with get, set
    member val ArchivedUrlId = 0 with get, set
    member val Timestamp = "" with get, set
    member val OriginalUrl = "" with get, set
    member val MimeType = "" with get, set
    member val StatusCode = "" with get, set
    member val Digest = "" with get, set
    member val Length = Nullable<int64>() with get, set

type WaybackDbContext(options: DbContextOptions<WaybackDbContext>) =
    inherit DbContext(options)

    [<DefaultValue>]
    val mutable private archivedUrls: DbSet<ArchivedUrl>

    [<DefaultValue>]
    val mutable private archivedSnapshots: DbSet<ArchivedSnapshot>

    member this.ArchivedUrls
        with get () = this.archivedUrls
        and set value = this.archivedUrls <- value

    member this.ArchivedSnapshots
        with get () = this.archivedSnapshots
        and set value = this.archivedSnapshots <- value

    override _.OnModelCreating(modelBuilder: ModelBuilder) =
        modelBuilder.Entity<ArchivedUrl>().HasIndex([| "OriginalUrl" |]).IsUnique() |> ignore
        modelBuilder.Entity<ArchivedUrl>().Property("OriginalUrl").IsRequired() |> ignore

        modelBuilder.Entity<ArchivedSnapshot>().Property("Timestamp").IsRequired() |> ignore

// ── Helpers ──

let parseNullableInt64 (value: string) =
    match Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture) with
    | true, parsed -> Nullable(parsed)
    | _ -> Nullable()

let createDbOptions (databasePath: string) (loggerFactory: AppLoggerFactory) =
    let builder = new DbContextOptionsBuilder<WaybackDbContext>()
    builder.UseSqlite("Data Source=" + databasePath) |> ignore
    builder.UseLoggerFactory(loggerFactory) |> ignore
    builder.EnableDetailedErrors() |> ignore
    builder.EnableSensitiveDataLogging() |> ignore

    builder.Options

let createDbContext (options: DbContextOptions<WaybackDbContext>) =
    new WaybackDbContext(options)

let newArchivedUrl (OriginalUrl originalUrl) =
    let entity = ArchivedUrl()
    entity.OriginalUrl <- originalUrl
    entity.SnapshotDiscoveryState <- "Pending"
    entity.DownloadState <- "Pending"
    entity

let newArchivedSnapshot (ArchivedUrlId archivedUrlId) (SnapshotTimestamp timestamp) (OriginalUrl originalUrl) (MimeType mimeType) (SnapshotStatusCode statusCode) (ContentDigest digest) contentLength =
    let entity = ArchivedSnapshot()
    entity.ArchivedUrlId <- archivedUrlId
    entity.Timestamp <- timestamp
    entity.OriginalUrl <- originalUrl
    entity.MimeType <- mimeType
    entity.StatusCode <- statusCode
    entity.Digest <- digest
    entity.Length <- contentLength
    entity

// ── DB query helpers ──

let tryFindSnapshot (db: WaybackDbContext) (archivedUrlId: int) (timestamp: string) =
    query {
        for item in db.ArchivedSnapshots do
        where (item.ArchivedUrlId = archivedUrlId && item.Timestamp = timestamp)
        select item
    }
    |> Seq.tryHead

let loadArchivedUrlById (db: WaybackDbContext) (archivedUrlId: int) =
    query {
        for item in db.ArchivedUrls do
        where (item.Id = archivedUrlId)
        select item
        exactlyOne
    }

let countArchivedUrls (db: WaybackDbContext) =
    query { for item in db.ArchivedUrls do count }

let countSnapshots (db: WaybackDbContext) =
    query { for item in db.ArchivedSnapshots do count }

let loadEarliestSnapshotMetadata (db: WaybackDbContext) =
    query {
        for item in db.ArchivedUrls do
        where (
            item.DownloadState = "Completed"
            && item.DownloadedRelativePath <> ""
            && item.EarliestSnapshotId <> "")
        select (item.DownloadedRelativePath, item.EarliestSnapshotId)
    }
    |> Seq.toList

let loadUsedSnapshotMetadata (db: WaybackDbContext) =
    query {
        for item in db.ArchivedUrls do
        where (
            item.DownloadState = "Completed"
            && item.DownloadedRelativePath <> ""
            && item.DownloadedSnapshotId <> "")
        select (item.DownloadedRelativePath, item.DownloadedSnapshotId)
    }
    |> Seq.toList

// ── DatabaseReader actor ──

let createDbReaderActor (dbOptions: DbContextOptions<WaybackDbContext>) (logger: AppLogger) =
    MailboxProcessor<DbReaderMsg>.Start(fun inbox ->
        let rec loop () = async {
            let! msg = inbox.Receive()
            match msg with
            | HasAnyUrls replyChannel ->
                use db = createDbContext dbOptions
                let count = countArchivedUrls db
                replyChannel.Reply(count > 0)
                return! loop ()

            | LoadUrlsForSnapshotDiscovery replyChannel ->
                use db = createDbContext dbOptions
                let urls =
                    query {
                        for item in db.ArchivedUrls do
                        where (item.SnapshotDiscoveryState <> "Completed" && item.SnapshotDiscoveryState <> "NotASiteUrl")
                        select (item.Id, item.OriginalUrl, item.SnapshotDiscoveryState)
                    }
                    |> Seq.toList
                    |> List.map (fun (id, url, state) -> (ArchivedUrlId id, OriginalUrl url, SnapshotDiscoveryState.fromDbString state))
                replyChannel.Reply(urls)
                return! loop ()

            | LoadUrlsForDownload replyChannel ->
                use db = createDbContext dbOptions
                let targets =
                    query {
                        for item in db.ArchivedUrls do
                        where (item.SnapshotDiscoveryState = "Completed" && item.SnapshotCount > 0)
                        select (item.Id, item.OriginalUrl, item.DownloadState)
                    }
                    |> Seq.toList
                    |> List.map (fun (id, url, state) ->
                        (ArchivedUrlId id, OriginalUrl url, DownloadState.fromDbString state))
                replyChannel.Reply(targets)
                return! loop ()

            | LoadSnapshotsForUrl (ArchivedUrlId archivedUrlIdVal, replyChannel) ->
                use db = createDbContext dbOptions
                let snapshots =
                    query {
                        for item in db.ArchivedSnapshots do
                        where (item.ArchivedUrlId = archivedUrlIdVal)
                        select (item.MimeType, item.StatusCode, item.Timestamp)
                    }
                    |> Seq.toList
                    |> List.map (fun (m, s, t) -> (MimeType m, SnapshotStatusCode s, SnapshotTimestamp t))
                replyChannel.Reply(snapshots)
                return! loop ()

            | GetSnapshotCountForUrl (ArchivedUrlId archivedUrlIdVal, replyChannel) ->
                use db = createDbContext dbOptions
                let count =
                    query {
                        for item in db.ArchivedSnapshots do
                        where (item.ArchivedUrlId = archivedUrlIdVal)
                        count
                    }
                replyChannel.Reply(count)
                return! loop ()

            | GetSummary replyChannel ->
                use db = createDbContext dbOptions
                let urlCount = countArchivedUrls db
                let snapshotCount = countSnapshots db
                replyChannel.Reply((urlCount, snapshotCount))
                return! loop ()
        }
        loop ()
    )

let createDbReaderPool
    (createWorker: unit -> MailboxProcessor<DbReaderMsg>)
    (concurrency: int) =
    if concurrency < 1 then
        invalidArg (nameof concurrency) "DbReader pool concurrency must be at least 1."

    let actors = Array.init concurrency (fun _ -> createWorker ())
    let mutable counter = 0

    MailboxProcessor<DbReaderMsg>.Start(fun inbox ->
        let rec loop () = async {
            let! msg = inbox.Receive()
            let index = counter % actors.Length
            counter <- counter + 1
            actors.[index].Post(msg)
            return! loop ()
        }
        loop ()
    )

// ── DatabaseWriter actor ──

let createDbWriterActor (dbOptions: DbContextOptions<WaybackDbContext>) (logger: AppLogger) =
    MailboxProcessor<DbWriterMsg>.Start(fun inbox ->
        let rec loop () = async {
            let! msg = inbox.Receive()
            match msg with
            | StoreDiscoveredUrls (rows, replyChannel) ->
                use db = createDbContext dbOptions
                for row in rows do
                    let originalUrl = row.Fields["original"]
                    match Uri.TryCreate(originalUrl, UriKind.Absolute) with
                    | false, _ ->
                        logger.LogWarning("Skipping invalid discovered url: {OriginalUrl}", originalUrl)
                    | true, _ ->
                        let entity = newArchivedUrl (OriginalUrl originalUrl)
                        db.ArchivedUrls.Add(entity) |> ignore
                db.SaveChanges() |> ignore
                let allUrls =
                    query {
                        for item in db.ArchivedUrls do
                        select item.Id
                    }
                    |> Seq.toList
                    |> List.map ArchivedUrlId
                replyChannel.Reply(allUrls)
                logger.LogInformation("Stored {InsertedCount} archived urls.", rows.Length)
                return! loop ()

            | MarkSnapshotDiscoveryState (ArchivedUrlId archivedUrlIdVal, state, ErrorMessage errorVal, replyChannel) ->
                use db = createDbContext dbOptions
                let entity = loadArchivedUrlById db archivedUrlIdVal
                entity.SnapshotDiscoveryState <- SnapshotDiscoveryState.toDbString state
                entity.LastError <- errorVal
                db.SaveChanges() |> ignore
                replyChannel.Reply()
                return! loop ()

            | StoreSnapshots (archivedUrlId, originalUrl, rows, replyChannel) ->
                let (ArchivedUrlId archivedUrlIdVal) = archivedUrlId
                try
                    use db = createDbContext dbOptions
                    let archivedUrl = loadArchivedUrlById db archivedUrlIdVal
                    let earliestSnapshotId =
                        rows
                        |> List.choose (fun row ->
                            match row.Fields.TryGetValue("timestamp") with
                            | true, timestamp when not (String.IsNullOrWhiteSpace(timestamp)) -> Some timestamp
                            | _ -> None)
                        |> List.sort
                        |> List.tryHead
                        |> Option.defaultValue ""

                    for row in rows do
                        let timestamp = row.Fields["timestamp"]
                        let mimeType = if row.Fields.ContainsKey("mimetype") then row.Fields["mimetype"] else ""
                        let statusCode = if row.Fields.ContainsKey("statuscode") then row.Fields["statuscode"] else ""
                        let digest = if row.Fields.ContainsKey("digest") then row.Fields["digest"] else ""
                        let contentLength = if row.Fields.ContainsKey("length") then parseNullableInt64 row.Fields["length"] else Nullable()

                        match tryFindSnapshot db archivedUrlIdVal timestamp with
                        | Some existing ->
                            existing.MimeType <- mimeType
                            existing.StatusCode <- statusCode
                            existing.Digest <- digest
                            existing.Length <- contentLength
                        | None ->
                            db.ArchivedSnapshots.Add(newArchivedSnapshot archivedUrlId (SnapshotTimestamp timestamp) originalUrl (MimeType mimeType) (SnapshotStatusCode statusCode) (ContentDigest digest) contentLength) |> ignore

                    archivedUrl.SnapshotDiscoveryState <- "Completed"
                    archivedUrl.SnapshotCount <- rows.Length
                    archivedUrl.EarliestSnapshotId <- earliestSnapshotId
                    archivedUrl.LastError <- ""
                    db.SaveChanges() |> ignore

                    replyChannel.Reply(Ok ())
                with ex ->
                    replyChannel.Reply(Error (ex.ToString()))
                return! loop ()

            | MarkDownloadSkipped (ArchivedUrlId archivedUrlIdVal, SnapshotTimestamp snapshotIdVal, state, ErrorMessage errorVal, replyChannel) ->
                use db = createDbContext dbOptions
                let entity = loadArchivedUrlById db archivedUrlIdVal
                entity.DownloadedSnapshotId <- snapshotIdVal
                entity.DownloadState <- DownloadState.toDbString state
                entity.DownloadedTextEncoding <- null
                entity.LastError <- errorVal
                db.SaveChanges() |> ignore
                replyChannel.Reply()
                return! loop ()

            | MarkDownloadCompleted (ArchivedUrlId archivedUrlIdVal, SnapshotTimestamp snapshotIdVal, RelativePath relativePathVal, downloadedTextEncoding, replyChannel) ->
                use db = createDbContext dbOptions
                let entity = loadArchivedUrlById db archivedUrlIdVal
                entity.DownloadedSnapshotId <- snapshotIdVal
                entity.DownloadState <- "Completed"
                entity.LastError <- ""
                entity.DownloadedRelativePath <- relativePathVal
                entity.DownloadedTextEncoding <- Option.toObj downloadedTextEncoding
                db.SaveChanges() |> ignore
                replyChannel.Reply()
                return! loop ()

            | MarkDownloadFailed (ArchivedUrlId archivedUrlIdVal, SnapshotTimestamp snapshotIdVal, RelativePath relativePathVal, ErrorMessage errorVal, replyChannel) ->
                use db = createDbContext dbOptions
                let entity = loadArchivedUrlById db archivedUrlIdVal
                entity.DownloadedSnapshotId <- snapshotIdVal
                entity.DownloadState <- "Failed"
                entity.DownloadedRelativePath <- relativePathVal
                entity.DownloadedTextEncoding <- null
                entity.LastError <- errorVal
                db.SaveChanges() |> ignore
                replyChannel.Reply()
                return! loop ()
        }
        loop ()
    )
