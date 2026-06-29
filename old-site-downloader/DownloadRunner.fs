module OldSiteDownloader.DownloadRunner

open System
open System.IO
open System.Net
open System.Net.Http
open System.Text
open System.Text.RegularExpressions
open Microsoft.Extensions.Logging
open OldSiteDownloader.Shared
open OldSiteDownloader.Types

let private ensureDirectory (path: string) =
    if Directory.Exists(path) then
        ()
    else
        Directory.CreateDirectory(path) |> ignore

let private utf8WithoutBom = new UTF8Encoding(false)

let private htmlCharsetRegex =
    Regex("(?is)(<meta\b[^>]*?charset\s*=\s*[\"']?)([^\"'\s/>]+)", RegexOptions.Compiled)

let private htmlContentTypeCharsetRegex =
    Regex("(?is)(<meta\b[^>]*?content\s*=\s*[\"'][^\"']*?charset\s*=\s*)([^\"'\s;]+)", RegexOptions.Compiled)

let private xmlEncodingRegex =
    Regex("(?is)(<\?xml\b[^>]*?encoding\s*=\s*[\"'])([^\"']+)", RegexOptions.Compiled)

let private tryGetEncodingByName (name: string) =
    if String.IsNullOrWhiteSpace(name) then
        None
    else
        let normalized = name.Trim().Trim('"', '\'', ';').ToLowerInvariant()

        let aliases =
            dict [
                "utf8", "utf-8"
                "unicode-1-1-utf-8", "utf-8"
                "latin1", "iso-8859-1"
                "latin-1", "iso-8859-1"
                "cp1252", "windows-1252"
                "x-sjis", "shift_jis"
            ]

        let candidate =
            match aliases.TryGetValue(normalized) with
            | true, value -> value
            | _ -> normalized

        try
            Some (Encoding.GetEncoding(candidate, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback))
        with
        | :? ArgumentException -> None
        | :? NotSupportedException -> None

let private tryGetEncodingFromBom (bytes: byte array) =
    if bytes.Length >= 3 && bytes[0] = 0xEFuy && bytes[1] = 0xBBuy && bytes[2] = 0xBFuy then
        Some Encoding.UTF8
    elif bytes.Length >= 2 && bytes[0] = 0xFFuy && bytes[1] = 0xFEuy then
        Some Encoding.Unicode
    elif bytes.Length >= 2 && bytes[0] = 0xFEuy && bytes[1] = 0xFFuy then
        Some Encoding.BigEndianUnicode
    elif bytes.Length >= 4 && bytes[0] = 0xFFuy && bytes[1] = 0xFEuy && bytes[2] = 0x00uy && bytes[3] = 0x00uy then
        Some Encoding.UTF32
    elif bytes.Length >= 4 && bytes[0] = 0x00uy && bytes[1] = 0x00uy && bytes[2] = 0xFEuy && bytes[3] = 0xFFuy then
        Some (UTF32Encoding(true, true) :> Encoding)
    else
        None

let private sniffDeclaredEncodingFromContent (bytes: byte array) =
    let sampleLength = min bytes.Length 8192
    let sample = Encoding.ASCII.GetString(bytes, 0, sampleLength)

    [ htmlCharsetRegex.Match(sample)
      htmlContentTypeCharsetRegex.Match(sample)
      xmlEncodingRegex.Match(sample) ]
    |> List.tryPick (fun m ->
        if m.Success && m.Groups.Count > 2 then
            tryGetEncodingByName m.Groups[2].Value
        else
            None)

let private isValidUtf8 (bytes: byte array) =
    try
        let encoding = UTF8Encoding(false, true)
        encoding.GetString(bytes) |> ignore
        true
    with
    | :? DecoderFallbackException -> false

let private isTextualMediaType (mediaType: string) =
    if String.IsNullOrWhiteSpace(mediaType) then
        false
    else
        let normalized = mediaType.Trim().ToLowerInvariant()
        normalized.StartsWith("text/", StringComparison.Ordinal)
        || normalized.EndsWith("+xml", StringComparison.Ordinal)
        || normalized = "application/xml"
        || normalized = "application/xhtml+xml"
        || normalized = "application/json"
        || normalized = "application/javascript"
        || normalized = "application/x-javascript"
        || normalized = "application/ecmascript"
        || normalized = "image/svg+xml"

let private tryDecodeWithEncoding (encoding: Encoding) (bytes: byte array) =
    try
        Some (encoding.GetString(bytes))
    with
    | :? DecoderFallbackException -> None

let private detectTextEncoding (response: HttpResponseMessage) (bytes: byte array) =
    let headerEncoding =
        response.Content.Headers.ContentType
        |> Option.ofObj
        |> Option.bind (fun ct -> ct.CharSet |> Option.ofObj)
        |> Option.bind tryGetEncodingByName

    headerEncoding
    |> Option.orElseWith (fun () -> tryGetEncodingFromBom bytes)
    |> Option.orElseWith (fun () -> sniffDeclaredEncodingFromContent bytes)
    |> Option.orElseWith (fun () -> if isValidUtf8 bytes then Some (UTF8Encoding(false, true) :> Encoding) else None)
    |> Option.defaultValue (Encoding.GetEncoding("windows-1252", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback))

let private normalizeTextForUtf8 (mediaType: string) (text: string) =
    let normalizedMediaType =
        if String.IsNullOrWhiteSpace(mediaType) then ""
        else mediaType.Trim().ToLowerInvariant()

    if normalizedMediaType = "text/html" || normalizedMediaType = "application/xhtml+xml" then
        text
        |> fun x -> htmlCharsetRegex.Replace(x, "$1utf-8")
        |> fun x -> htmlContentTypeCharsetRegex.Replace(x, "$1utf-8")
        |> fun x -> xmlEncodingRegex.Replace(x, "$1utf-8")
    elif normalizedMediaType.EndsWith("+xml", StringComparison.Ordinal) || normalizedMediaType = "application/xml" || normalizedMediaType = "text/xml" then
        xmlEncodingRegex.Replace(text, "$1utf-8")
    else
        text

let private convertDownloadedContentToBytes (response: HttpResponseMessage) (bytes: byte array) =
    let mediaType =
        response.Content.Headers.ContentType
        |> Option.ofObj
        |> Option.bind (fun ct -> ct.MediaType |> Option.ofObj)
        |> Option.defaultValue ""

    if isTextualMediaType mediaType then
        let sourceEncoding = detectTextEncoding response bytes

        match tryDecodeWithEncoding sourceEncoding bytes with
        | Some text ->
            let normalizedText = normalizeTextForUtf8 mediaType text
            utf8WithoutBom.GetBytes(normalizedText), Some sourceEncoding.WebName
        | None ->
            bytes, None
    else
        bytes, None

let createDownloadRunnerActor
    (retrySettings: RetrySettings)
    (httpClient: HttpClient)
    (logger: AppLogger)
    (dbWriter: MailboxProcessor<DbWriterMsg>)
    (downloadCoordinator: MailboxProcessor<DownloadCoordinatorMsg>)
    =
    let sendWithRetry = HttpRetry.sendWithRetry retrySettings

    MailboxProcessor<DownloadRunnerMsg>.Start(fun inbox ->
        let rec loop () = async {
            let! msg = inbox.Receive()
            match msg with
            | RunDownload (archivedUrlId, originalUrl, timestamp, plan) ->
                let (OriginalUrl originalUrlStr) = originalUrl
                let (SnapshotTimestamp timestampStr) = timestamp
                let (RelativePath relativePathStr) = plan.RelativePath
                let work = async {
                    try
                        try
                            ensureDirectory (Path.GetDirectoryName(plan.OutputPath))
                            let snapshotUrl = "https://web.archive.org/web/" + timestampStr + "id_/" + originalUrlStr
                            use! response = sendWithRetry httpClient logger "Snapshot download" snapshotUrl |> Async.AwaitTask
                            let! bytes = response.Content.ReadAsByteArrayAsync() |> Async.AwaitTask

                            if response.IsSuccessStatusCode then
                                let bytesToWrite, detectedEncoding = convertDownloadedContentToBytes response bytes
                                do! File.WriteAllBytesAsync(plan.OutputPath, bytesToWrite) |> Async.AwaitTask
                                dbWriter.PostAndReply(fun rc -> MarkDownloadCompleted(archivedUrlId, timestamp, plan.RelativePath, detectedEncoding, rc))
                                match detectedEncoding with
                                | Some encoding when not (String.Equals(encoding, "utf-8", StringComparison.OrdinalIgnoreCase)) ->
                                    logger.LogInformation("Saved {RelativePath} as UTF-8 after decoding {DetectedEncoding}", relativePathStr, encoding)
                                | _ ->
                                    logger.LogInformation("Saved {RelativePath}", relativePathStr)
                            else
                                let responseBody = Text.Encoding.UTF8.GetString(bytes)
                                let error = "Wayback returned " + string (int response.StatusCode) + " " + response.ReasonPhrase + ". " + responseBody
                                dbWriter.PostAndReply(fun rc -> MarkDownloadFailed(archivedUrlId, timestamp, plan.RelativePath, ErrorMessage error, rc))
                                logger.LogWarning("Download failed for {OriginalUrl} with status {StatusCode}", originalUrlStr, int response.StatusCode)
                        with ex ->
                            dbWriter.PostAndReply(fun rc -> MarkDownloadFailed(archivedUrlId, timestamp, plan.RelativePath, ErrorMessage (ex.ToString()), rc))
                            logger.LogError(ex, "Unhandled download failure for {OriginalUrl}", originalUrlStr)
                    finally
                        downloadCoordinator.Post(DownloadRunCompleted archivedUrlId)
                }
                Async.Start work
                return! loop ()
        }
        loop ()
    )
