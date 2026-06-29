module OldSiteDownloader.Types

open System
open System.Collections.Generic
open Microsoft.Extensions.Logging

type AppLogger = ILogger
type AppLoggerFactory = ILoggerFactory

[<Struct>]
type ArchivedUrlId = ArchivedUrlId of int

[<Struct>]
type OriginalUrl = OriginalUrl of string

[<Struct>]
type SnapshotTimestamp = SnapshotTimestamp of string

[<Struct>]
type MimeType = MimeType of string

[<Struct>]
type SnapshotStatusCode = SnapshotStatusCode of string

[<Struct>]
type ContentDigest = ContentDigest of string

[<Struct>]
type RelativePath = RelativePath of string

[<Struct>]
type ErrorMessage = ErrorMessage of string

[<RequireQualifiedAccess>]
type SnapshotDiscoveryState =
    | Pending
    | Completed
    | Failed
    | NoSnapshots
    | NotASiteUrl

module SnapshotDiscoveryState =
    let toDbString = function
        | SnapshotDiscoveryState.Pending -> "Pending"
        | SnapshotDiscoveryState.Completed -> "Completed"
        | SnapshotDiscoveryState.Failed -> "Failed"
        | SnapshotDiscoveryState.NoSnapshots -> "NoSnapshots"
        | SnapshotDiscoveryState.NotASiteUrl -> "NotASiteUrl"

    let fromDbString = function
        | "Pending" -> SnapshotDiscoveryState.Pending
        | "Completed" -> SnapshotDiscoveryState.Completed
        | "Failed" -> SnapshotDiscoveryState.Failed
        | "NoSnapshots" -> SnapshotDiscoveryState.NoSnapshots
        | "NotASiteUrl" -> SnapshotDiscoveryState.NotASiteUrl
        | other -> invalidOp $"Unknown SnapshotDiscoveryState: '{other}'"

[<RequireQualifiedAccess>]
type DownloadState =
    | Pending
    | Completed
    | Failed
    | SkippedUnsupported
    | SkippedNoSuitableSnapshot

module DownloadState =
    let toDbString = function
        | DownloadState.Pending -> "Pending"
        | DownloadState.Completed -> "Completed"
        | DownloadState.Failed -> "Failed"
        | DownloadState.SkippedUnsupported -> "SkippedUnsupported"
        | DownloadState.SkippedNoSuitableSnapshot -> "SkippedNoSuitableSnapshot"

    let fromDbString = function
        | "Pending" -> DownloadState.Pending
        | "Completed" -> DownloadState.Completed
        | "Failed" -> DownloadState.Failed
        | "SkippedUnsupported" -> DownloadState.SkippedUnsupported
        | "SkippedNoSuitableSnapshot" -> DownloadState.SkippedNoSuitableSnapshot
        | other -> invalidOp $"Unknown DownloadState: '{other}'"

type CdxRow =
    { Fields: Dictionary<string, string> }

type CdxBatch =
    { Rows: CdxRow list
      ResumeKey: string option }

type DownloadPlan =
    { OutputPath: string
      RelativePath: RelativePath }

type DbReaderMsg =
    | HasAnyUrls of replyChannel: AsyncReplyChannel<bool>
    | LoadUrlsForSnapshotDiscovery of replyChannel: AsyncReplyChannel<(ArchivedUrlId * OriginalUrl * SnapshotDiscoveryState) list>
    | LoadUrlsForDownload of replyChannel: AsyncReplyChannel<(ArchivedUrlId * OriginalUrl * DownloadState) list>
    | LoadSnapshotsForUrl of archivedUrlId: ArchivedUrlId * replyChannel: AsyncReplyChannel<(MimeType * SnapshotStatusCode * SnapshotTimestamp) list>
    | GetSnapshotCountForUrl of archivedUrlId: ArchivedUrlId * replyChannel: AsyncReplyChannel<int>
    | GetSummary of replyChannel: AsyncReplyChannel<int * int>

type DbWriterMsg =
    | StoreDiscoveredUrls of rows: CdxRow list * replyChannel: AsyncReplyChannel<ArchivedUrlId list>
    | StoreSnapshots of archivedUrlId: ArchivedUrlId * originalUrl: OriginalUrl * rows: CdxRow list * replyChannel: AsyncReplyChannel<Result<unit, string>>
    | MarkSnapshotDiscoveryState of archivedUrlId: ArchivedUrlId * state: SnapshotDiscoveryState * error: ErrorMessage * replyChannel: AsyncReplyChannel<unit>
    | MarkDownloadSkipped of archivedUrlId: ArchivedUrlId * snapshotId: SnapshotTimestamp * state: DownloadState * error: ErrorMessage * replyChannel: AsyncReplyChannel<unit>
    | MarkDownloadCompleted of archivedUrlId: ArchivedUrlId * snapshotId: SnapshotTimestamp * relativePath: RelativePath * downloadedTextEncoding: string option * replyChannel: AsyncReplyChannel<unit>
    | MarkDownloadFailed of archivedUrlId: ArchivedUrlId * snapshotId: SnapshotTimestamp * relativePath: RelativePath * error: ErrorMessage * replyChannel: AsyncReplyChannel<unit>

type CdxLookupMsg =
    | DiscoverUrls of replyChannel: AsyncReplyChannel<CdxRow list>
    | FetchSnapshots of originalUrl: OriginalUrl * replyChannel: AsyncReplyChannel<Result<CdxRow list, string>>

type DownloadCoordinatorMsg =
    | ProcessDownloadTarget of archivedUrlId: ArchivedUrlId * originalUrl: OriginalUrl * downloadState: DownloadState
    | DownloadRunCompleted of archivedUrlId: ArchivedUrlId

type DownloadRunnerMsg =
    | RunDownload of archivedUrlId: ArchivedUrlId * originalUrl: OriginalUrl * timestamp: SnapshotTimestamp * plan: DownloadPlan

type CoordinatorMsg =
    | StartPipeline
    | UrlDiscoveryDone
    | SnapshotHarvestDone of archivedUrlId: ArchivedUrlId * originalUrl: OriginalUrl
    | DownloadTargetProcessed of archivedUrlId: ArchivedUrlId
    | AllSnapshotsHarvested
    | Shutdown
