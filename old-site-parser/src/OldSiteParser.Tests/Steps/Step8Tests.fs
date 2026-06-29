module OldSiteParser.Tests.Steps.Step8Tests

open System
open Xunit
open FsUnit.Xunit
open System.Linq
open OldSiteParser.Database
open OldSiteParser.Steps.Step8_OutputTables

let private createTestDb () =
    let dbPath = sprintf "step8_test_%s.db" (Guid.NewGuid().ToString("N").[..7])
    ContextFactory.create dbPath

let private createSiteDataDbPath () =
  IO.Path.Combine(IO.Path.GetTempPath(), sprintf "site-data-%s.db" (Guid.NewGuid().ToString("N").[..7]))

[<Fact>]
let ``Step8 materializes combined output tables`` () =
    let ctx = createTestDb ()
    let siteDataDbPath = createSiteDataDbPath ()

    try
        ctx.ScannedFiles.Add(
            { RelativePath = "posts/apple-pie.htm"
              FileHash = "hash-1"
              FileType = "html"
              EarliestSnapshot = "20010102030405"
              UsedSnapshot = "20220929040443"
              TitleTag = "Foods of England - Apple Pie"
              ErrorMessage = null
              IsExcluded = false
              HasFilesPrefix = false
              IsRefreshedSitePage = Nullable()
              HasPageNotFoundTitle = Nullable()
              LastModified = DateTime(2024, 1, 1) })
        |> ignore

        ctx.ScannedFiles.Add(
            { RelativePath = "images/apple.jpg"
              FileHash = "hash-2"
              FileType = "image"
              EarliestSnapshot = null
              UsedSnapshot = "20220929040443"
              TitleTag = null
              ErrorMessage = null
              IsExcluded = false
              HasFilesPrefix = false
              IsRefreshedSitePage = Nullable()
              HasPageNotFoundTitle = Nullable()
              LastModified = DateTime(2024, 1, 1) })
        |> ignore

        ctx.FinalNames.Add(
            { RelativePath = "posts/apple-pie.htm"
              FinalName = "apple-pie.html"
              FileType = "html"
              DuplicateOf = null })
        |> ignore

        ctx.FinalNames.Add(
            { RelativePath = "images/apple.jpg"
              FinalName = "apple-pie-apple.jpg"
              FileType = "image"
              DuplicateOf = null })
        |> ignore

        ctx.PreTidyMetadata.AddRange(
            [| { Id = 0; RelativePath = "posts/apple-pie.htm"; TagName = "heading"; TextContent = "Apple pie" }
               { Id = 0; RelativePath = "posts/apple-pie.htm"; TagName = "class"; TextContent = "Pies" }
               { Id = 0; RelativePath = "posts/apple-pie.htm"; TagName = "reference"; TextContent = "mrs-b" }
               { Id = 0; RelativePath = "posts/apple-pie.htm"; TagName = "keyword"; TextContent = "apple pie" } |])
        |> ignore

        ctx.PostTidyMetadata.Add(
            { Id = 0
              RelativePath = "apple-pie.html"
              MetadataKey = "synonym"
              MetadataValue = "Apple tart" })
        |> ignore

        ctx.Step6Contents.Add(
            { FinalName = "apple-pie.html"
              Content = "Main content" })
        |> ignore

        ctx.Receipts.Add(
            { ReceiptId = "apple-pie.html-1"
              ReceiptSource = "Mrs Beeton"
              RelativePath = "apple-pie.html"
              ReceiptText = "Use 2 apples and 1 cup of sugar" })
        |> ignore

        ctx.RelatedPages.Add(
            { Id = 0
              SourcePage = "apple-pie.html"
              RelationDescription = "See also:"
              RelatedPageText = "Apple dumpling"
              RelatedPageUrl = "apple-dumpling.html" })
        |> ignore

        ctx.Step7ReceiptIngredients.AddRange(
            [| { Id = 0; RelativePath = "apple-pie.html"; ReceiptId = "apple-pie.html-1"; IngredientName = "apples" }
               { Id = 0; RelativePath = "apple-pie.html"; ReceiptId = "apple-pie.html-1"; IngredientName = "sugar" }
               { Id = 0; RelativePath = "apple-pie.html"; ReceiptId = "apple-pie.html-1"; IngredientName = "sugar" } |])
        |> ignore

        ctx.SaveChanges() |> ignore

        run siteDataDbPath (IO.Path.Combine(IO.Path.GetTempPath(), "oldsiteparser-step8-" + Guid.NewGuid().ToString("N").[..7])) ctx

        use siteDataCtx = SiteDataContextFactory.create siteDataDbPath

        siteDataCtx.NonHtmlFiles.Count() |> should equal 1
        siteDataCtx.Posts.Count() |> should equal 1
        siteDataCtx.PostTags.Count() |> should equal 3
        siteDataCtx.RelatedPages.Count() |> should equal 1
        siteDataCtx.PostIngredients.Count() |> should equal 2
        siteDataCtx.PostReceipts.Count() |> should equal 1

        let post = siteDataCtx.Posts.Single(fun p -> p.FinalPath = "apple-pie.html")
        post.Title |> should equal "Apple Pie"
        post.EarliestSnapshot |> should equal "20010102030405"
        post.UsedSnapshot |> should equal "20220929040443"
        post.ReceiptCount |> should equal 1
        post.Heading |> should equal "Apple pie"
        post.Synonym |> should equal "Apple tart"
        post.Content |> should equal "Main content"

        let nonHtml = siteDataCtx.NonHtmlFiles.Single()
        nonHtml.InitialPath |> should equal "images/apple.jpg"
        nonHtml.FinalPath |> should equal "apple-pie-apple.jpg"

        let tagNames = siteDataCtx.PostTags.ToArray() |> Array.map (fun tag -> tag.TagName) |> Array.sort
        tagNames |> should equal [| "class"; "keyword"; "reference" |]

        // File checks
        let dbDir = IO.Path.GetDirectoryName(siteDataDbPath)
        let dataDir = IO.Path.Combine(dbDir, "site", "data")
        IO.Directory.Exists(dataDir) |> should equal true

        let htmlPath = IO.Path.Combine(dataDir, "apple-pie.html")
        let jsonPath = IO.Path.Combine(dataDir, "apple-pie.json")
        let assetsPath = IO.Path.Combine(dataDir, "assets.json")

        IO.File.Exists(htmlPath) |> should equal true
        IO.File.Exists(jsonPath) |> should equal true
        IO.File.Exists(assetsPath) |> should equal true

        let htmlContent = IO.File.ReadAllText(htmlPath)
        htmlContent |> should equal "Main content"

        let jsonContent = IO.File.ReadAllText(jsonPath)
        jsonContent.Contains("\"title\": \"Apple Pie\"") |> should equal true
        jsonContent.Contains("\"finalPath\": \"apple-pie.html\"") |> should equal true
        jsonContent.Contains("\"tagName\": \"class\"") |> should equal true
        jsonContent.Contains("\"tagValue\": \"Pies\"") |> should equal true
        jsonContent.Contains("\"relationDescription\": \"See also:\"") |> should equal true
        jsonContent.Contains("\"receiptSource\": \"Mrs Beeton\"") |> should equal true

        let assetsContent = IO.File.ReadAllText(assetsPath)
        assetsContent.Contains("\"initialPath\": \"images/apple.jpg\"") |> should equal true
        assetsContent.Contains("\"finalPath\": \"apple-pie-apple.jpg\"") |> should equal true
    finally
        ctx.Dispose()