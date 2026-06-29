module OldSiteParser.Shared.Logging

open System

/// Mutable error counter for exit code determination.
let mutable private errorCount = 0
let mutable private warningCount = 0

/// Reset counters (call at the start of a run).
let resetCounters () =
    errorCount <- 0
    warningCount <- 0

/// Get the current error count.
let getErrorCount () = errorCount

/// Get the current warning count.
let getWarningCount () = warningCount

/// Log an error to stderr with structured context.
let logError (step: string) (filePath: string) (message: string) =
    errorCount <- errorCount + 1
    eprintfn "[%s] ERROR %s: %s" step filePath message

/// Log a warning to stderr with structured context.
let logWarning (step: string) (filePath: string) (message: string) =
    warningCount <- warningCount + 1
    eprintfn "[%s] WARNING %s: %s" step filePath message

/// Log an informational message to stdout.
let logInfo (step: string) (message: string) =
    printfn "[%s] %s" step message

/// Log an orphan image warning (image not referenced by any HTML file).
let logOrphanImage (imagePath: string) =
    logWarning "Step3" imagePath "Orphan image — not referenced by any HTML file"

/// Log a non-zero tidy exit code.
let logTidyWarning (fileName: string) (exitCode: int) =
    logWarning "Step5" fileName (sprintf "Tidy exited with code %d" exitCode)

/// Print step summary (name, file count, elapsed time).
let logStepSummary (stepName: string) (fileCount: int) (elapsed: TimeSpan) =
    printfn "[%s] Processed %d files in %.1fs" stepName fileCount elapsed.TotalSeconds

/// Print final completion summary.
let logCompletionSummary (steps: int list) (elapsed: TimeSpan) =
    let stepsStr = steps |> List.map string |> String.concat ","
    let totalErrors = errorCount + warningCount
    printfn "Completed steps [%s] in %.1fs. %d errors logged." stepsStr elapsed.TotalSeconds totalErrors
