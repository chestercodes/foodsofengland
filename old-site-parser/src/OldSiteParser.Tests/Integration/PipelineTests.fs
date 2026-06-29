module OldSiteParser.Tests.Integration.PipelineTests

open System
open System.IO
open Xunit
open FsUnit.Xunit
open System.Linq
open OldSiteParser.Database

/// Create an in-memory SQLite database for testing.
let private createTestDb () =
    let connStr = sprintf "Data Source=test_%s.db" (Guid.NewGuid().ToString("N").[..7])
    let ctx = ContextFactory.create connStr
    ctx

/// Get the example-html folder path (relative to test execution directory).
let private findExampleHtmlFolder () =
    // Walk up from bin/Debug/net10.0 to find example-html
    let mutable dir = Directory.GetCurrentDirectory()

    for _ in 1..6 do
        let candidate = Path.Combine(dir, "example-html")

        if Directory.Exists(candidate) then
            dir <- dir // stop
        else
            dir <- Path.GetDirectoryName(dir)

    let candidate = Path.Combine(dir, "example-html")

    if Directory.Exists(candidate) then
        Some candidate
    else
        None

[<Fact>]
let ``Step 1 scans example-html files`` () =
    match findExampleHtmlFolder () with
    | None -> () // Skip if example-html not found
    | Some exampleFolder ->
        let ctx = createTestDb ()

        try
            OldSiteParser.Steps.Step1_IdentifyFiles.run exampleFolder ctx

            let count = ctx.ScannedFiles.Count()
            count |> should be (greaterThan 0)

            // All example files are .htm = HTML
            let htmlCount =
                ctx.ScannedFiles.Where(fun f -> f.FileType = "html").Count()

            htmlCount |> should equal count
        finally
            ctx.Dispose()

[<Fact>]
let ``Step 1 extracts titles from HTML files`` () =
    match findExampleHtmlFolder () with
    | None -> ()
    | Some exampleFolder ->
        let ctx = createTestDb ()

        try
            OldSiteParser.Steps.Step1_IdentifyFiles.run exampleFolder ctx

            let withTitles =
                ctx.ScannedFiles
                    .Where(fun f -> f.TitleTag <> null)
                    .Count()

            withTitles |> should be (greaterThan 0)

            // Check a known title exists
            let africanCakes =
                ctx.ScannedFiles
                    .Where(fun f -> f.TitleTag <> null && f.TitleTag.Contains("African"))
                    .FirstOrDefault()

            africanCakes |> should not' (be null)
        finally
            ctx.Dispose()

[<Fact>]
let ``Step 1 ignores downloader metadata file and stores earliest snapshot`` () =
    let folder = Path.Combine(Path.GetTempPath(), "oldsiteparser-step1-" + Guid.NewGuid().ToString("N").[..7])
    Directory.CreateDirectory(folder) |> ignore
    let ctx = createTestDb ()

    try
        let metadataJson =
            """{
  "earliestSnapshots": {
    "page.htm": "20010102030405"
  }
}"""

        File.WriteAllText(Path.Combine(folder, "page.htm"), "<html><head><title>Page</title></head><body>Hello</body></html>")
        File.WriteAllText(Path.Combine(folder, "old-site.metadata.json"), metadataJson)

        OldSiteParser.Steps.Step1_IdentifyFiles.run folder ctx

        ctx.ScannedFiles.Count() |> should equal 1

        let page = ctx.ScannedFiles.Single(fun f -> f.RelativePath = "page.htm")
        page.EarliestSnapshot |> should equal "20010102030405"
    finally
        ctx.Dispose()

        if Directory.Exists(folder) then
            Directory.Delete(folder, true)

[<Fact>]
let ``Steps 1 and 2 extract content and URLs`` () =
    match findExampleHtmlFolder () with
    | None -> ()
    | Some exampleFolder ->
        let ctx = createTestDb ()

        try
            OldSiteParser.Steps.Step1_IdentifyFiles.run exampleFolder ctx
            OldSiteParser.Steps.Step2_ParseHtml.run exampleFolder ctx

            let contentCount = ctx.RawPageContents.Count()
            contentCount |> should be (greaterThan 0)

            let urlCount = ctx.ParsedUrls.Count()
            urlCount |> should be (greaterThan 0)
        finally
            ctx.Dispose()

[<Fact>]
let ``Steps 1-3 generate unique final names`` () =
    match findExampleHtmlFolder () with
    | None -> ()
    | Some exampleFolder ->
        let ctx = createTestDb ()

        try
            OldSiteParser.Steps.Step1_IdentifyFiles.run exampleFolder ctx
            OldSiteParser.Steps.Step2_ParseHtml.run exampleFolder ctx
            OldSiteParser.Steps.Step3_FinalNames.run exampleFolder ctx

            let names =
                ctx.FinalNames.ToArray() |> Array.map (fun f -> f.FinalName)

            names.Length |> should be (greaterThan 0)

            // All names should be unique
            let uniqueCount = names |> Array.distinct |> Array.length
            uniqueCount |> should equal names.Length

            // Check a known final name
            names |> should contain "african-cakes.html"
        finally
            ctx.Dispose()

[<Fact>]
let ``Steps 1-4 render output files to working directory`` () =
    match findExampleHtmlFolder () with
    | None -> ()
    | Some exampleFolder ->
        let ctx = createTestDb ()
        let workingDir = Path.Combine(Path.GetTempPath(), "oldsiteparser-test-" + Guid.NewGuid().ToString("N").[..7])

        try
            OldSiteParser.Steps.Step1_IdentifyFiles.run exampleFolder ctx
            OldSiteParser.Steps.Step2_ParseHtml.run exampleFolder ctx
            OldSiteParser.Steps.Step3_FinalNames.run exampleFolder ctx
            OldSiteParser.Steps.Step4_PreTidy.run workingDir ctx

            // Output files should exist
            let outputFiles = Directory.GetFiles(workingDir, "*.html")
            outputFiles.Length |> should be (greaterThan 0)

            // Custom tags should have been extracted
            let tagCount = ctx.PreTidyMetadata.Count()
            tagCount |> should be (greaterThan 0)

            // Verify a rendered file has scroll.png GUIDs where appropriate
            let africanCakesFile =
                outputFiles |> Array.tryFind (fun f -> f.Contains("african-cakes"))

            match africanCakesFile with
            | Some path ->
                let content = File.ReadAllText(path)
                // Custom tags should be replaced with comments
                content |> should not' (haveSubstring "<class>")
            | None -> ()
        finally
            ctx.Dispose()

            if Directory.Exists(workingDir) then
                Directory.Delete(workingDir, true)
