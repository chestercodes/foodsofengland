module OldSiteParser.Tests.Steps.Step3Tests

open System
open Xunit
open FsUnit.Xunit
open OldSiteParser.Domain.Types
open OldSiteParser.Steps.Step3_FinalNames

let private makeScannedFile (relativePath: string) (fileType: FileType) (title: string option) : ScannedFile =
        { Path = RelativePath relativePath; Hash = FileHash (Guid.NewGuid().ToString("N")); FileType = fileType; EarliestSnapshot = None; UsedSnapshot = None; TitleTag = title; Error = None; IsExcluded = false; HasFilesPrefix = false; IsRefreshedSitePage = None; HasPageNotFoundTitle = None; LastModified = DateTime.UtcNow }

let private makeHtmlFile (relativePath: string) (title: string option) : ScannedFile =
    makeScannedFile relativePath Html title

let private makeImageFile (relativePath: string) : ScannedFile =
    makeScannedFile relativePath Image None

let private makeParsedUrl (htmlPath: string) (url: string) : ParsedUrl =
    { SourcePath = RelativePath htmlPath
      RawUrl = RawUrl url
      ServerPath = Some url
      DiskPath = Some url
      RenamedUrl = None
      TagType = "img"
      AltText = None
      FileExists = Some Exists }

// --- generateHtmlFinalNames ---

[<Fact>]
let ``generateHtmlFinalNames uses title for kebab name`` () =
    let files = [| makeHtmlFile "page.htm" (Some "Foods of England - African Cakes") |]

    let result = generateHtmlFinalNames files
    result |> should haveLength 1
    let (path, name) = result.[0]
    path |> should equal (RelativePath "page.htm")
    name |> should equal (FinalName "african-cakes.html")

[<Fact>]
let ``generateHtmlFinalNames falls back to filename when no title`` () =
    let files = [| makeHtmlFile "MyPage.htm" None |]

    let result = generateHtmlFinalNames files
    result |> should haveLength 1
    let (_, name) = result.[0]
    name |> should equal (FinalName "mypage.html")

[<Fact>]
let ``generateHtmlFinalNames skips excluded files`` () =
    let good = makeHtmlFile "good.htm" (Some "Good Page")
    let excluded = { makeHtmlFile "bad.htm" (Some "Bad Page") with IsExcluded = true }
    let files = [| good; excluded |]

    let result = generateHtmlFinalNames files
    result |> should haveLength 1
    let (path, _) = result.[0]
    path |> should equal (RelativePath "good.htm")

[<Fact>]
let ``generateHtmlFinalNames handles multiple files`` () =
    let files = [|
        makeHtmlFile "a.htm" (Some "Foods of England - Albion Pudding")
        makeHtmlFile "b.htm" (Some "Foods of England - Yorkshire Stilton")
    |]

    let result = generateHtmlFinalNames files
    result |> should haveLength 2
    let names = result |> List.map snd
    names |> should contain (FinalName "albion-pudding.html")
    names |> should contain (FinalName "yorkshire-stilton.html")

// --- generateImageFinalNames ---

[<Fact>]
let ``generateImageFinalNames prefixes with html name for single reference`` () =
    let imageFiles = [| makeImageFile "photo.jpg" |]
    let allUrls = [| makeParsedUrl "page.htm" "photo.jpg" |]
    let htmlNames = Map.ofList [ (RelativePath "page.htm", FinalName "test") ]

    let result = generateImageFinalNames allUrls imageFiles htmlNames
    result |> should haveLength 1
    let (_, name, _) = result.[0]
    name |> should haveSubstring "test"

[<Fact>]
let ``generateImageFinalNames uses own name for multi-referenced image`` () =
    let imageFiles = [| makeImageFile "shared.jpg" |]
    let allUrls = [|
        makeParsedUrl "page1.htm" "shared.jpg"
        makeParsedUrl "page2.htm" "shared.jpg"
    |]
    let htmlNames = Map.ofList [ (RelativePath "page1.htm", FinalName "page-one"); (RelativePath "page2.htm", FinalName "page-two") ]

    let result = generateImageFinalNames allUrls imageFiles htmlNames
    result |> should haveLength 1
    let (_, name, _) = result.[0]
    name |> should haveSubstring "shared"

[<Fact>]
let ``generateImageFinalNames handles orphan image`` () =
    let imageFiles = [| makeImageFile "lonely.png" |]
    let allUrls = [||]
    let htmlNames = Map.empty

    let result = generateImageFinalNames allUrls imageFiles htmlNames
    result |> should haveLength 1
    let (_, name, _) = result.[0]
    name |> should haveSubstring "lonely"

[<Fact>]
let ``generateImageFinalNames keeps extension and lowercases it`` () =
    let imageFiles = [| makeImageFile "UPPERCASE.JPG" |]
    let allUrls = [||]
    let htmlNames = Map.empty

    let result = generateImageFinalNames allUrls imageFiles htmlNames
    result |> should haveLength 1
    let (_, FinalName nameStr, _) = result.[0]
    nameStr |> should equal "uppercase.jpg"

// --- HTML duplicate naming ---

[<Fact>]
let ``HTML duplicate gets suffix before html extension`` () =
    let htmlFiles = [|
        makeHtmlFile "page1.htm" (Some "Foods of England - Scones")
        makeHtmlFile "page2.htm" (Some "Foods of England - Scones")
    |]

    let htmlNamePairs = generateHtmlFinalNames htmlFiles
    let resolved = resolveHtmlDuplicates htmlNamePairs

    let names = resolved |> List.map (fun (_, name, _, _) -> let (FinalName n) = name in n)
    names |> should contain "scones.html"
    names |> should contain "scones-2.html"
    names |> List.iter (fun n -> n.Contains(".html-") |> should equal false)

[<Fact>]
let ``HTML triple duplicate gets correct suffixes`` () =
    let htmlFiles = [|
        makeHtmlFile "a.htm" (Some "Foods of England - Scones")
        makeHtmlFile "b.htm" (Some "Foods of England - Scones")
        makeHtmlFile "c.htm" (Some "Foods of England - Scones")
    |]

    let htmlNamePairs = generateHtmlFinalNames htmlFiles
    let resolved = resolveHtmlDuplicates htmlNamePairs

    let names = resolved |> List.map (fun (_, name, _, _) -> let (FinalName n) = name in n)
    names |> should contain "scones.html"
    names |> should contain "scones-2.html"
    names |> should contain "scones-3.html"

// --- Image duplicate naming ---

[<Fact>]
let ``image duplicate retains file extension`` () =
    let htmlFiles = [| makeHtmlFile "woolton.htm" (Some "Foods of England - Woolton Pie") |]
    let imageFiles = [| makeImageFile "woolton.jpg"; makeImageFile "500-woolton.jpg" |]
    let allUrls = [|
        makeParsedUrl "woolton.htm" "woolton.jpg"
        makeParsedUrl "woolton.htm" "500-woolton.jpg"
    |]

    let htmlNamePairs = generateHtmlFinalNames htmlFiles
    let resolvedHtml = resolveHtmlDuplicates htmlNamePairs
    let htmlNamesMap = resolvedHtml |> List.map (fun (path, name, _, _) -> path, name) |> Map.ofList

    let imageNamePairs = generateImageFinalNames allUrls imageFiles htmlNamesMap
    let resolvedImages = resolveImageDuplicates imageNamePairs

    let names = resolvedImages |> List.map (fun (_, name, _, _, _) -> let (FinalName n) = name in n)
    names |> List.iter (fun n -> n.EndsWith(".jpg") |> should equal true)
    names |> List.iter (fun n -> n.Contains("500-") |> should equal false)
    names |> should contain "woolton-pie-woolton.jpg"
    names |> should contain "woolton-pie-woolton-2.jpg"

[<Fact>]
let ``image name from duplicate HTML page does not contain html extension`` () =
    let htmlFiles = [|
        makeHtmlFile "scones1.htm" (Some "Foods of England - Scones")
        makeHtmlFile "scones2.htm" (Some "Foods of England - Scones")
    |]
    let imageFiles = [| makeImageFile "800px-Pile_of_scones.jpg" |]
    let allUrls = [| makeParsedUrl "scones2.htm" "800px-Pile_of_scones.jpg" |]

    let htmlNamePairs = generateHtmlFinalNames htmlFiles
    let resolvedHtml = resolveHtmlDuplicates htmlNamePairs
    let htmlNamesMap = resolvedHtml |> List.map (fun (path, name, _, _) -> path, name) |> Map.ofList

    let imageNamePairs = generateImageFinalNames allUrls imageFiles htmlNamesMap
    let resolvedImages = resolveImageDuplicates imageNamePairs

    let imageNames = resolvedImages |> List.map (fun (_, name, _, _, _) -> let (FinalName n) = name in n)
    imageNames |> should haveLength 1
    imageNames.[0].Contains(".html") |> should equal false
    imageNames.[0].EndsWith(".jpg") |> should equal true

[<Fact>]
let ``applyOverrides returns None by default`` () =
    let result = applyOverrides (RelativePath "any/path.htm")
    result |> should equal None

[<Fact>]
let ``applyOverrides returns references.html for references.htm`` () =
    let result = applyOverrides (RelativePath "references.htm")
    result |> should equal (Some (FinalName "references.html"))
