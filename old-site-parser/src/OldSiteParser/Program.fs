open System
open System.Linq
open System.IO
open OldSiteParser.Database
open OldSiteParser.Shared
open OldSiteParser.Steps

type RunConfig = { SourceFolder: string; Database: string; SiteDataDatabase: string; WorkingDir: string; Steps: int list }


let ascendFileSystemUntillFindRoot (startPath: string) =
    let mutable currentPath = startPath
    
    let dirIsRoot (path: string) =
         let dir = DirectoryInfo(path)
         dir.Name = "foodsofengland"
    
    while not (dirIsRoot currentPath) do
        let parent = Directory.GetParent(currentPath)
        match parent with
        | null -> invalidOp "Could not find root in any parent directory."
        | info ->
            currentPath <- info.FullName
    currentPath

let repoRoot = ascendFileSystemUntillFindRoot "."
let projectDir = Path.Combine(repoRoot, "old-site-parser")
let defaultSource = Path.Combine(repoRoot, "old-site-downloader/out")
let outDir = Path.Combine(projectDir, "out")
let args =
    {   SourceFolder = defaultSource
        Database = Path.Combine(outDir, "old-site.db")
        SiteDataDatabase = Path.Combine(outDir, "site-data.db")
        WorkingDir = Path.Combine(projectDir, "work")
        //Steps = [ 3; 4; 5; 6; 7; 8 ]
        //Steps = [ 1; 2; 3; 4; 5;]
        Steps = [ 1; 2; 3; 4; 5; 6; 7; 8 ]
    }

/// Validate that prerequisite database tables are populated before running a step.
let private validatePrerequisites (ctx: PipelineDbContext) (step: int) : Result<unit, string> =
    match step with
    | 1 -> Ok()
    | 2 ->
        if ctx.ScannedFiles.Any() then Ok()
        else Error "Step 2 requires Step 1 to have populated ScannedFiles. Run --step 1 first."
    | 3 ->
        if ctx.RawPageContents.Any() then Ok()
        else Error "Step 3 requires Step 2 to have populated PageContents. Run --step 2 first."
    | 4 ->
        if ctx.FinalNames.Any() then Ok()
        else Error "Step 4 requires Step 3 to have populated FinalNames. Run --step 3 first."
    | 5 -> Ok() // Step 5 just needs files in the working dir
    | 6 ->
        if ctx.TidyResults.Any() then Ok()
        else Error "Step 6 requires Step 5 to have populated TidyResults. Run --step 5 first."
    | 7 ->
        if ctx.Receipts.Any() then Ok()
        else Error "Step 7 requires Step 6 to have populated Receipts. Run --step 6 first."
    | 8 ->
        if ctx.Step6Contents.Any() then Ok()
        else Error "Step 8 requires Step 6 to have populated Step6Contents. Run --step 6 first."
    | _ -> Error(sprintf "Unknown step: %d" step)

let private runStep
    (step: int)
    (sourceFolder: string)
    (_databasePath: string)
    (siteDataDatabasePath: string)
    (workingDir: string)
    (ctx: PipelineDbContext)
    : bool =
    match validatePrerequisites ctx step with
    | Error msg ->
        Logging.logError (sprintf "Step%d" step) "" msg
        false
    | Ok() ->
        try
            let preTidyWd = Path.Combine(workingDir, "pretidy")
            let step6Wd = Path.Combine(workingDir, "step6")
            let step7Wd = Path.Combine(workingDir, "step7")
            let step8Wd = Path.Combine(workingDir, "step8")
            match step with
            | 1 -> Step1_IdentifyFiles.run sourceFolder ctx
            | 2 -> Step2_ParseHtml.run sourceFolder ctx
            | 3 -> Step3_FinalNames.run sourceFolder ctx
            | 4 -> Step4_PreTidy.run preTidyWd ctx
            | 5 -> Step5_Tidy.run preTidyWd ctx
            | 6 -> Step6_Extract.run step6Wd ctx
            | 7 -> Step7_Ingredients.run step7Wd ctx
            | 8 -> Step8_OutputTables.run siteDataDatabasePath step8Wd ctx
            | _ -> Logging.logError "Program" "" (sprintf "Unknown step: %d" step)

            true
        with ex ->
            Logging.logError (sprintf "Step%d" step) "" (sprintf "Fatal error: %s" (ex.ToString()))
            false


[<EntryPoint>]
let main (argv: string array) =
    

    let overallStopwatch = Diagnostics.Stopwatch.StartNew()
    Logging.resetCounters ()
    let absSourceFolder = System.IO.Path.GetFullPath(args.SourceFolder)
    if not (IO.Directory.Exists(absSourceFolder)) then
        eprintfn "Error: Source folder does not exist: %s" absSourceFolder
        2
    else
        let ctx = ContextFactory.create args.Database

        let mutable allSuccess = true
        let mutable completedSteps = []

        for step in args.Steps do
            if allSuccess then
                let success = runStep step args.SourceFolder args.Database args.SiteDataDatabase args.WorkingDir ctx

                if success then
                    completedSteps <- step :: completedSteps
                else
                    allSuccess <- false

        overallStopwatch.Stop()
        Logging.logCompletionSummary (completedSteps |> List.rev) overallStopwatch.Elapsed

        ctx.Dispose()

        let errorCount = Logging.getErrorCount ()

        if not allSuccess then 2
        elif errorCount > 0 then 1
        else 0
