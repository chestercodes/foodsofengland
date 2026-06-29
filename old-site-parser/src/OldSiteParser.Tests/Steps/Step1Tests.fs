module OldSiteParser.Tests.Steps.Step1Tests

open System
open System.IO
open Xunit
open FsUnit.Xunit
open OldSiteParser.Steps.Step1_IdentifyFiles
open OldSiteParser.Domain.Types

// --- classifyFile ---

[<Theory>]
[<InlineData(".htm")>]
[<InlineData(".html")>]
[<InlineData(".HTM")>]
[<InlineData(".HTML")>]
let ``classifyFile returns Html for html extensions`` (ext: string) =
    let (fileType, err) = classifyFile ext
    fileType |> should equal Html
    err |> should equal None

[<Theory>]
[<InlineData(".jpg")>]
[<InlineData(".jpeg")>]
[<InlineData(".png")>]
[<InlineData(".gif")>]
[<InlineData(".bmp")>]
[<InlineData(".webp")>]
[<InlineData(".svg")>]
[<InlineData(".ico")>]
[<InlineData(".JPG")>]
let ``classifyFile returns Image for image extensions`` (ext: string) =
    let (fileType, err) = classifyFile ext
    fileType |> should equal Image
    err |> should equal None

[<Theory>]
[<InlineData(".txt")>]
[<InlineData(".pdf")>]
[<InlineData(".css")>]
[<InlineData(".js")>]
let ``classifyFile returns Unknown for unrecognised extensions`` (ext: string) =
    let (fileType, err) = classifyFile ext
    fileType |> should equal Unknown
    err.IsSome |> should equal true

// --- htmlExtensions / imageExtensions ---

//[<Fact>]
//let ``htmlExtensions contains expected values`` () =
//    htmlExtensions |> Set.contains ".htm" |> should equal true
//    htmlExtensions |> Set.contains ".html" |> should equal true
//    htmlExtensions |> Set.count |> should equal 2

[<Fact>]
let ``imageExtensions contains expected values`` () =
    imageExtensions |> Set.contains ".jpg" |> should equal true
    imageExtensions |> Set.contains ".png" |> should equal true
    imageExtensions |> Set.contains ".gif" |> should equal true
    imageExtensions |> Set.count |> should equal 8

// --- scanFile ---

let private withTempFolder (action: string -> unit) =
    let folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(folder) |> ignore
    try
        action folder
    finally
        Directory.Delete(folder, true)

[<Fact>]
let ``scanFile classifies an HTML file and extracts title`` () =
    withTempFolder (fun folder ->
        let filePath = Path.Combine(folder, "test.htm")
        File.WriteAllText(filePath, "<html><head><title>Foods of England - Test Page</title></head><body>Hello</body></html>")

        let result = scanFile folder Map.empty filePath
        result.FileType |> should equal Html
        result.EarliestSnapshot |> should equal None
        result.TitleTag |> should equal (Some "Foods of England - Test Page")
        result.IsExcluded |> should equal false
        result.Error |> should equal None

        let (RelativePath rp) = result.Path
        rp |> should equal "test.htm"
    )

[<Fact>]
let ``scanFile classifies an image file`` () =
    withTempFolder (fun folder ->
        let filePath = Path.Combine(folder, "photo.jpg")
        File.WriteAllBytes(filePath, [| 0xFFuy; 0xD8uy; 0xFFuy |])

        let result = scanFile folder Map.empty filePath
        result.FileType |> should equal Image
        result.TitleTag |> should equal None
        result.IsExcluded |> should equal false
    )

[<Fact>]
let ``scanFile marks unknown file type as excluded`` () =
    withTempFolder (fun folder ->
        let filePath = Path.Combine(folder, "readme.txt")
        File.WriteAllText(filePath, "hello")

        let result = scanFile folder Map.empty filePath
        result.FileType |> should equal Unknown
        result.IsExcluded |> should equal true
    )

[<Fact>]
let ``scanFile marks page not found as excluded`` () =
    withTempFolder (fun folder ->
        let filePath = Path.Combine(folder, "missing.htm")
        File.WriteAllText(filePath, "<html><head><title>Page Not Found</title></head><body></body></html>")

        let result = scanFile folder Map.empty filePath
        result.FileType |> should equal Html
        result.IsExcluded |> should equal true
    )

[<Fact>]
let ``scanFile marks image file with page not found text as excluded`` () =
    withTempFolder (fun folder ->
        let filePath = Path.Combine(folder, "photo.jpg")
        File.WriteAllText(filePath, "Page not found")

        let result = scanFile folder Map.empty filePath
        result.FileType |> should equal Image
        result.IsExcluded |> should equal true
        result.HasPageNotFoundTitle |> should equal None
    )

[<Fact>]
let ``scanFile computes a non-empty hash`` () =
    withTempFolder (fun folder ->
        let filePath = Path.Combine(folder, "data.htm")
        File.WriteAllText(filePath, "<html><body>Content</body></html>")

        let result = scanFile folder Map.empty filePath
        let (FileHash h) = result.Hash
        h |> should not' (equal "")
        h.Length |> should equal 64 // SHA-256 hex
    )

[<Fact>]
let ``scanFile handles subdirectory with forward slashes in path`` () =
    withTempFolder (fun folder ->
        let subDir = Path.Combine(folder, "sub", "dir")
        Directory.CreateDirectory(subDir) |> ignore
        let filePath = Path.Combine(subDir, "page.htm")
        File.WriteAllText(filePath, "<html><body></body></html>")

        let result = scanFile folder Map.empty filePath
        let (RelativePath rp) = result.Path
        rp |> should equal "sub/dir/page.htm"
    )

[<Fact>]
let ``scanFile handles missing file gracefully`` () =
    withTempFolder (fun folder ->
        let filePath = Path.Combine(folder, "nonexistent.htm")

        let result = scanFile folder Map.empty filePath
        result.IsExcluded |> should equal true
        result.Error.IsSome |> should equal true
    )

// --- New property tests ---

[<Fact>]
let ``scanFile sets HasFilesPrefix true for files/ path`` () =
    withTempFolder (fun folder ->
        let subDir = Path.Combine(folder, "files")
        Directory.CreateDirectory(subDir) |> ignore
        let filePath = Path.Combine(subDir, "page.htm")
        File.WriteAllText(filePath, "<html><head><title>Test</title></head><body>Hello</body></html>")

        let result = scanFile folder Map.empty filePath
        result.HasFilesPrefix |> should equal true
    )

[<Fact>]
let ``scanFile sets HasFilesPrefix false for non-files/ path`` () =
    withTempFolder (fun folder ->
        let filePath = Path.Combine(folder, "page.htm")
        File.WriteAllText(filePath, "<html><head><title>Test</title></head><body>Hello</body></html>")

        let result = scanFile folder Map.empty filePath
        result.HasFilesPrefix |> should equal false
    )

[<Fact>]
let ``scanFile sets IsRefreshedSitePage true for SEO page`` () =
    withTempFolder (fun folder ->
        let filePath = Path.Combine(folder, "seo.htm")
        File.WriteAllText(filePath, "<html><head><title>Test</title></head><body>This site is optimized with the Yoast SEO plugin</body></html>")

        let result = scanFile folder Map.empty filePath
        result.IsRefreshedSitePage |> should equal (Some true)
        result.IsExcluded |> should equal true
    )

[<Fact>]
let ``scanFile sets IsRefreshedSitePage false for normal HTML`` () =
    withTempFolder (fun folder ->
        let filePath = Path.Combine(folder, "normal.htm")
        File.WriteAllText(filePath, "<html><head><title>Test</title></head><body>Hello</body></html>")

        let result = scanFile folder Map.empty filePath
        result.IsRefreshedSitePage |> should equal (Some false)
    )

[<Fact>]
let ``scanFile sets IsRefreshedSitePage None for image`` () =
    withTempFolder (fun folder ->
        let filePath = Path.Combine(folder, "photo.jpg")
        File.WriteAllBytes(filePath, [| 0xFFuy; 0xD8uy; 0xFFuy |])

        let result = scanFile folder Map.empty filePath
        result.IsRefreshedSitePage |> should equal None
    )

[<Fact>]
let ``scanFile sets HasPageNotFoundTitle true for page not found`` () =
    withTempFolder (fun folder ->
        let filePath = Path.Combine(folder, "missing.htm")
        File.WriteAllText(filePath, "<html><head><title>Page Not Found</title></head><body></body></html>")

        let result = scanFile folder Map.empty filePath
        result.HasPageNotFoundTitle |> should equal (Some true)
    )

[<Fact>]
let ``scanFile sets HasPageNotFoundTitle false for normal HTML`` () =
    withTempFolder (fun folder ->
        let filePath = Path.Combine(folder, "normal.htm")
        File.WriteAllText(filePath, "<html><head><title>Normal Page</title></head><body>Hello</body></html>")

        let result = scanFile folder Map.empty filePath
        result.HasPageNotFoundTitle |> should equal (Some false)
    )

[<Fact>]
let ``scanFile sets HasPageNotFoundTitle None for image`` () =
    withTempFolder (fun folder ->
        let filePath = Path.Combine(folder, "photo.jpg")
        File.WriteAllBytes(filePath, [| 0xFFuy; 0xD8uy; 0xFFuy |])

        let result = scanFile folder Map.empty filePath
        result.HasPageNotFoundTitle |> should equal None
    )

[<Fact>]
let ``scanFile loads earliest snapshot from metadata map`` () =
    withTempFolder (fun folder ->
        let filePath = Path.Combine(folder, "page.htm")
        File.WriteAllText(filePath, "<html><head><title>Normal Page</title></head><body>Hello</body></html>")

        let result = scanFile folder (Map.ofList [ "page.htm", "20041231115959" ]) filePath
        result.EarliestSnapshot |> should equal (Some "20041231115959")
    )

[<Fact>]
let ``loadEarliestSnapshotMetadata reads downloader metadata file`` () =
    withTempFolder (fun folder ->
        let metadataJson =
            """{
  "earliestSnapshots": {
    "page.htm": "20010102030405",
    "nested/file.htm": "20060708091011"
  }
}"""

        File.WriteAllText(Path.Combine(folder, metadataFileName), metadataJson)

        let result = loadEarliestSnapshotMetadata folder
        result |> should equal (Map.ofList [ "nested/file.htm", "20060708091011"; "page.htm", "20010102030405" ])
    )
