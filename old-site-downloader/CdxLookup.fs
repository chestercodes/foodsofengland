module OldSiteDownloader.CdxLookup

open System
open System.Collections.Generic
open System.Net.Http
open System.Text.Json
open Microsoft.Extensions.Logging
open OldSiteDownloader.Shared
open OldSiteDownloader.Types

let private cdxEndpoint = "https://web.archive.org/cdx/search/cdx"
let private cdxBatchSize = 1000

let private buildQueryString (pairs: (string * string) list) =
    pairs
    |> List.filter (fun (_, value) -> not (String.IsNullOrWhiteSpace(value)))
    |> List.map (fun (key, value) -> $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}")
    |> String.concat "&"

let private fetchCdxBatch retrySettings (httpClient: HttpClient) (logger: AppLogger) (parameters: (string * string) list) (resumeKey: string option) =
    task {
        let sendWithRetry = HttpRetry.sendWithRetry retrySettings

        let query =
            [ "output", "json"
              "showResumeKey", "true"
              "limit", string cdxBatchSize
              "gzip", "false" ]
            @ parameters
            @ (match resumeKey with
               | Some value -> [ "resumeKey", value ]
               | None -> [])

        let requestUri = cdxEndpoint + "?" + buildQueryString query
        logger.LogDebug("Requesting CDX batch: {RequestUri}", requestUri)

        use! response = sendWithRetry httpClient logger "CDX request" requestUri
        response.EnsureSuccessStatusCode() |> ignore

        let! payload = response.Content.ReadAsStringAsync()
        use document = JsonDocument.Parse(payload)
        let elements = document.RootElement.EnumerateArray() |> Seq.toArray

        if elements.Length = 0 then
            return { Rows = []; ResumeKey = None }
        else
            let header =
                elements[0].EnumerateArray()
                |> Seq.choose (fun item -> item.GetString() |> Option.ofObj)
                |> Seq.toArray

            let rows = ResizeArray<CdxRow>()
            let mutable expectingResumeKey = false
            let mutable nextResumeKey = None

            for index in 1 .. elements.Length - 1 do
                let item = elements[index]

                if item.ValueKind = JsonValueKind.Array then
                    let values = item.EnumerateArray() |> Seq.toArray

                    if values.Length = 0 then
                        expectingResumeKey <- true
                    elif expectingResumeKey && values.Length = 1 then
                        nextResumeKey <- values[0].GetString() |> Option.ofObj
                        expectingResumeKey <- false
                    else
                        let dictionary = Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)

                        for columnIndex in 0 .. min (header.Length - 1) (values.Length - 1) do
                            dictionary[header[columnIndex]] <- values[columnIndex].ToString()

                        rows.Add({ Fields = dictionary })

            return
                { Rows = rows |> Seq.toList
                  ResumeKey = nextResumeKey }
    }

let private fetchAllCdxRows retrySettings (httpClient: HttpClient) (logger: AppLogger) (parameters: (string * string) list) =
    task {
        let allRows = ResizeArray<CdxRow>()
        let mutable resumeKey = None
        let mutable keepGoing = true

        while keepGoing do
            let! batch = fetchCdxBatch retrySettings httpClient logger parameters resumeKey
            allRows.AddRange(batch.Rows)

            match batch.ResumeKey with
            | Some value when batch.Rows.Length > 0 -> resumeKey <- Some value
            | _ -> keepGoing <- false

        return allRows |> Seq.toList
    }

let createCdxLookupActor
    (retrySettings: RetrySettings)
    (siteBase: string)
    (httpClient: HttpClient)
    (logger: AppLogger) =
    MailboxProcessor<CdxLookupMsg>.Start(fun inbox ->
        let rec loop () = async {
            let! msg = inbox.Receive()
            match msg with
            | DiscoverUrls replyChannel ->
                let! rows =
                    fetchAllCdxRows retrySettings httpClient logger
                        [ "url", siteBase
                          "matchType", "host"
                          "collapse", "urlkey"
                          "fl", "original" ]
                    |> Async.AwaitTask
                logger.LogInformation("CDX returned {UrlCount} unique urls.", rows.Length)
                replyChannel.Reply(rows)
                return! loop ()

            | FetchSnapshots (originalUrl, replyChannel) ->
                let (OriginalUrl originalUrlStr) = originalUrl
                try
                    let! rows =
                        fetchAllCdxRows retrySettings httpClient logger
                            [ "url", originalUrlStr
                              "fl", "timestamp,original,mimetype,statuscode,digest,length" ]
                        |> Async.AwaitTask
                    replyChannel.Reply(Ok rows)
                with ex ->
                    replyChannel.Reply(Error (ex.ToString()))
                return! loop ()
        }
        loop ()
    )

let createCdxLookupPool
    (createWorker: unit -> MailboxProcessor<CdxLookupMsg>)
    (snapshotLookupConcurrency: int) =
    let actors = Array.init snapshotLookupConcurrency (fun _ -> createWorker ())
    let mutable counter = 0

    MailboxProcessor<CdxLookupMsg>.Start(fun inbox ->
        let rec loop () = async {
            let! msg = inbox.Receive()
            match msg with
            | DiscoverUrls _ ->
                actors.[0].Post(msg)
            | FetchSnapshots _ ->
                let index = counter % actors.Length
                counter <- counter + 1
                actors.[index].Post(msg)
            return! loop ()
        }
        loop ()
    )
