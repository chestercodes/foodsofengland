module OldSiteDownloader.Shared

open System
open System.IO
open System.Net
open System.Net.Http
open System.Threading.Tasks
open Microsoft.Extensions.Logging

type RetrySettings =
    { MaxAttempts: int
      InitialBackoff: TimeSpan
      RateLimitBackoff: TimeSpan }

module HttpRetry =
    let isTransientStatusCode (statusCode: HttpStatusCode) =
        let numericStatusCode = int statusCode
        statusCode = HttpStatusCode.RequestTimeout
        || statusCode = HttpStatusCode.TooManyRequests
        || numericStatusCode >= 500

    let calculateRetryDelay (settings: RetrySettings) (statusCode: HttpStatusCode option) attemptNumber =
        let backoff =
            match statusCode with
            | Some sc when sc = HttpStatusCode.TooManyRequests -> settings.RateLimitBackoff
            | _ -> settings.InitialBackoff

        let exponent = max 0 (attemptNumber - 1)
        let multiplier = pown 2.0 exponent
        let baseDelay = TimeSpan.FromMilliseconds(backoff.TotalMilliseconds * multiplier)
        let jitterMilliseconds = Random.Shared.Next(0, 500)
        baseDelay + TimeSpan.FromMilliseconds(float jitterMilliseconds)

    let sendWithRetry (settings: RetrySettings) (httpClient: HttpClient) (logger: ILogger) (operationName: string) (requestUri: string) =
        task {
            let mutable attemptNumber = 1
            let mutable completed = false
            let mutable finalResponse = None

            while not completed do
                try
                    let! response = httpClient.GetAsync(requestUri)

                    if isTransientStatusCode response.StatusCode && attemptNumber < settings.MaxAttempts then
                        let delay = calculateRetryDelay settings (Some response.StatusCode) attemptNumber
                        logger.LogWarning(
                            "{OperationName} returned transient status {StatusCode} for {RequestUri} on attempt {AttemptNumber}/{MaxAttempts}. Retrying in {DelayMs}ms.",
                            operationName,
                            int response.StatusCode,
                            requestUri,
                            attemptNumber,
                            settings.MaxAttempts,
                            int delay.TotalMilliseconds)
                        response.Dispose()
                        do! Task.Delay(delay)
                        attemptNumber <- attemptNumber + 1
                    else
                        finalResponse <- Some response
                        completed <- true
                with
                | :? HttpRequestException as ex when attemptNumber < settings.MaxAttempts ->
                    let delay = calculateRetryDelay settings None attemptNumber
                    logger.LogWarning(
                        ex,
                        "{OperationName} failed for {RequestUri} on attempt {AttemptNumber}/{MaxAttempts}. Retrying in {DelayMs}ms.",
                        operationName,
                        requestUri,
                        attemptNumber,
                        settings.MaxAttempts,
                        int delay.TotalMilliseconds)
                    do! Task.Delay(delay)
                    attemptNumber <- attemptNumber + 1
                | :? TaskCanceledException as ex when attemptNumber < settings.MaxAttempts ->
                    let delay = calculateRetryDelay settings None attemptNumber
                    logger.LogWarning(
                        ex,
                        "{OperationName} timed out for {RequestUri} on attempt {AttemptNumber}/{MaxAttempts}. Retrying in {DelayMs}ms.",
                        operationName,
                        requestUri,
                        attemptNumber,
                        settings.MaxAttempts,
                        int delay.TotalMilliseconds)
                    do! Task.Delay(delay)
                    attemptNumber <- attemptNumber + 1

            return finalResponse.Value
        }

module UrlFilter =
    let blockedDynamicExtensions =
        set [ ".ashx"; ".asmx"; ".aspx"; ".axd"; ".cgi"; ".fcgi"; ".php"; ".pl" ]

    let blockedPathPrefixes =
        [ "/.well-known"
          "/cgi-bin/" ]

    let blockedPathParts =
        [ "%2F%2F%2F%2F%2F%2F"
          "%3Dipc"
          "y=ipc"
          "/wp-json/"
          "/wp-content/"
          "/wp-includes/" ]

    let isSupportedStaticSiteUrl (originalUrl: string) =
        match Uri.TryCreate(originalUrl, UriKind.Absolute) with
        | false, _ -> false
        | true, uri ->
            let path = uri.AbsolutePath
            let extension = Path.GetExtension(path).ToLowerInvariant()
            let hasQuery = not (String.IsNullOrWhiteSpace(uri.Query))
            let hasFragment = not (String.IsNullOrWhiteSpace(uri.Fragment))

            let blockedByPath =
                blockedPathPrefixes
                |> List.exists (fun prefix -> path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                || blockedPathParts
                    |> List.exists (fun part -> path.Contains(part, StringComparison.OrdinalIgnoreCase))

            let isBlockedExtension = blockedDynamicExtensions.Contains(extension)

            not hasQuery
            && not hasFragment
            && not blockedByPath
            && not isBlockedExtension
