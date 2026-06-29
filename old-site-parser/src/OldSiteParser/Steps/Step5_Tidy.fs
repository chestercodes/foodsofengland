module OldSiteParser.Steps.Step5_Tidy

open System
open System.Diagnostics
open System.IO
open System.Linq
open System.Threading.Tasks
open OldSiteParser.Database
open OldSiteParser.Shared

// tidy is useful for checking for malformed HTML and fixing it where possible
// the stored stderr can be used to see the sort of errors that the html has on mass 

let private runTidy (inputPath: string) : (string * int * string * string) option =
    try
        let tidyConfigContents = @"wrap: 0
doctype: omit
tidy-mark: false
omit-optional-tags: true
"
        let tidyConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tidy.config")
        if not (File.Exists(tidyConfigPath)) then
            File.WriteAllText(tidyConfigPath, tidyConfigContents)
        let tidiedPath = inputPath + ".tidied"
        let psi = ProcessStartInfo()
        psi.FileName <- "tidy"
        psi.Arguments <- sprintf "-config \"%s\" -o \"%s\" -q -utf8 \"%s\"" tidyConfigPath tidiedPath inputPath
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false
        psi.CreateNoWindow <- true

        use proc = Process.Start(psi)
        if isNull proc then
            failwith "Failed to start tidy process"

        let stdoutTask = proc.StandardOutput.ReadToEndAsync()
        let stderrTask = proc.StandardError.ReadToEndAsync()

        proc.WaitForExit()
        Task.WaitAll(stdoutTask :> Task, stderrTask :> Task)

        let stdout = stdoutTask.Result
        let stderr = stderrTask.Result

        let exitCode = proc.ExitCode

        Some(tidiedPath, exitCode, stdout, stderr)
    with ex ->
        Logging.logError "Step5" inputPath (sprintf "Tidy failed to launch: %s" ex.Message)
        None

let removeExistingStepData (ctx: PipelineDbContext) =
    if ctx.TidyResults.Any() then
        Logging.logInfo "Step5" "Clearing existing TidyResults..."
        ctx.TidyResults.RemoveRange(ctx.TidyResults) |> ignore
    else
        Logging.logInfo "Step5" "No existing TidyResults to clear."
    
    ctx.SaveChanges() |> ignore


let run (workingDir: string) (ctx: PipelineDbContext) =
    let stopwatch = Diagnostics.Stopwatch.StartNew()
    Logging.logInfo "Step5" "Running HTML Tidy..."

    removeExistingStepData ctx

    let htmlFiles =
        if Directory.Exists(workingDir) then
            Directory.GetFiles(workingDir, "*.html")
        else
            Logging.logError "Step5" workingDir "Working directory does not exist"
            [||]

    Logging.logInfo "Step5" (sprintf "Processing %d files with tidy" htmlFiles.Length)

    ChannelProcessing.runParallel
        htmlFiles
        (fun filePath ->
            let fileName = Path.GetFileName(filePath)
            let result = runTidy filePath

            match result with
            | Some(tidiedPath, exitCode, stdout, stderr) ->
                if exitCode <> 0 then
                    Logging.logTidyWarning fileName exitCode
                let output =
                    if File.Exists(tidiedPath) then
                        File.ReadAllText(tidiedPath)
                    else
                        // if tidy fails, still want the transformations done by the pre tidy step
                        File.ReadAllText(filePath)
                { FinalName = fileName
                  ExitCode = exitCode
                  Stdout = stdout
                  Stderr = stderr
                  Output = output }
            | None ->
                { FinalName = fileName
                  ExitCode = -1
                  Stdout = ""
                  Stderr = "Tidy process could not be started"
                  Output = "" })
        (fun entity -> ctx.TidyResults.Add(entity) |> ignore)
        Environment.ProcessorCount
    
    printfn "Saving changes..."
    ctx.SaveChanges() |> ignore
    printfn "Saved changes to db."

    stopwatch.Stop()
    Logging.logStepSummary "Step5" htmlFiles.Length stopwatch.Elapsed
