module OldSiteParser.Tests.Steps.Step2Tests

open System
open System.IO
open Xunit
open FsUnit.Xunit
open OldSiteParser.Database
open OldSiteParser.Steps.Step2_ParseHtml

let private withTempFolder (action: string -> unit) =
    let folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(folder) |> ignore
    try
        action folder
    finally
        Directory.Delete(folder, true)

let private makeScannedFileEntity (relativePath: string) : Step1ScannedFileEntity = { RelativePath = relativePath
                                                                                      FileHash = "abc123"
                                                                                      FileType = "html"
                                                                                      EarliestSnapshot = null
                                                                                      UsedSnapshot = null
                                                                                      TitleTag = "Test Title"
                                                                                      ErrorMessage = null
                                                                                      IsExcluded = false
                                                                                      HasFilesPrefix = false
                                                                                      IsRefreshedSitePage = Nullable<bool>()
                                                                                      HasPageNotFoundTitle = Nullable<bool>()
                                                                                      LastModified = DateTime.UtcNow }

// --- processFile ---

[<Fact>]
let ``processFile extracts body content and stores page content`` () =
    withTempFolder (fun folder ->
        let filePath = Path.Combine(folder, "page.htm")
        File.WriteAllText(filePath, """<html><body>
<!---BODY TEXT START--->
<p>Hello world</p>
<!---BODY TEXT END--->
</body></html>""")

        let entity = makeScannedFileEntity "page.htm"
        let result = processFile folder entity

        result.PageContent.IsSome |> should equal true
        let page = result.PageContent.Value
        page.RelativePath |> should equal "page.htm"
        page.Content |> should haveSubstring "Hello world"
        page.TemplateType |> should equal "body-text-start"
    )

[<Fact>]
let ``processFile extracts URLs from content`` () =
    withTempFolder (fun folder ->
        let filePath = Path.Combine(folder, "links.htm")
        // Create a referenced file so file-exists check can find it
        File.WriteAllText(Path.Combine(folder, "other.htm"), "<html></html>")
        File.WriteAllText(filePath, """<html><body>
<!---BODY TEXT START--->
<a href="other.htm">Link</a>
<a href="https://example.com">External</a>
<!---BODY TEXT END--->
</body></html>""")

        let entity = makeScannedFileEntity "links.htm"
        let result = processFile folder entity

        let urls = result.ParsedUrls |> List.toArray
        urls.Length |> should be (greaterThanOrEqualTo 2)

        let localUrl = urls |> Array.tryFind (fun u -> u.RawUrl = "other.htm")
        localUrl.IsSome |> should equal true
        localUrl.Value.FileExists |> should equal (Nullable true)

        let externalUrl = urls |> Array.tryFind (fun u -> u.RawUrl = "https://example.com")
        externalUrl.IsSome |> should equal true
        externalUrl.Value.FileExists.HasValue |> should equal false
    )

[<Fact>]
let ``processFile sets FileExists false for missing local files`` () =
    withTempFolder (fun folder ->
        let filePath = Path.Combine(folder, "broken.htm")
        File.WriteAllText(filePath, """<html><body>
<!---BODY TEXT START--->
<a href="missing-page.htm">Broken link</a>
<!---BODY TEXT END--->
</body></html>""")

        let entity = makeScannedFileEntity "broken.htm"
        let result = processFile folder entity

        let urls = result.ParsedUrls |> List.toArray
        let brokenUrl = urls |> Array.tryFind (fun u -> u.RawUrl = "missing-page.htm")
        brokenUrl.IsSome |> should equal true
        brokenUrl.Value.FileExists |> should equal (Nullable false)
    )

[<Fact>]
let ``processFile handles mailto links`` () =
    withTempFolder (fun folder ->
        let filePath = Path.Combine(folder, "contact.htm")
        File.WriteAllText(filePath, """<html><body>
<!---BODY TEXT START--->
<a href="mailto:test@example.com">Email</a>
<!---BODY TEXT END--->
</body></html>""")

        let entity = makeScannedFileEntity "contact.htm"
        let result = processFile folder entity

        let urls = result.ParsedUrls |> List.toArray
        let mailUrl = urls |> Array.tryFind (fun u -> u.RawUrl.StartsWith("mailto:"))
        mailUrl.IsSome |> should equal true
        mailUrl.Value.FileExists.HasValue |> should equal false
    )

[<Fact>]
let ``processFile extracts img tag URLs`` () =
    withTempFolder (fun folder ->
        let filePath = Path.Combine(folder, "images.htm")
        File.WriteAllText(Path.Combine(folder, "photo.jpg"), "fake image")
        File.WriteAllText(filePath, """<html><body>
<!---BODY TEXT START--->
<img src="photo.jpg" alt="A photo">
<!---BODY TEXT END--->
</body></html>""")

        let entity = makeScannedFileEntity "images.htm"
        let result = processFile folder entity

        let urls = result.ParsedUrls |> List.toArray
        let imgUrl = urls |> Array.tryFind (fun u -> u.TagType = "img")
        imgUrl.IsSome |> should equal true
        imgUrl.Value.RawUrl |> should equal "photo.jpg"
        imgUrl.Value.AltText |> should equal "A photo"
    )

[<Fact>]
let ``processFile with TEXT START template`` () =
    withTempFolder (fun folder ->
        let filePath = Path.Combine(folder, "old.htm")
        File.WriteAllText(filePath, """<html><body>
<!---TEXT START--->
<p>Old format content</p>
<!---TEXT END--->
</body></html>""")

        let entity = makeScannedFileEntity "old.htm"
        let result = processFile folder entity

        result.PageContent.IsSome |> should equal true
        let page = result.PageContent.Value
        page.TemplateType |> should equal "text-start"
        page.Content |> should haveSubstring "Old format content"
    )

[<Fact>]
let ``processFile with no template markers uses full body`` () =
    withTempFolder (fun folder ->
        let filePath = Path.Combine(folder, "plain.htm")
        File.WriteAllText(filePath, """<html><body><p>Just content</p></body></html>""")

        let entity = makeScannedFileEntity "plain.htm"
        let result = processFile folder entity

        result.PageContent.IsSome |> should equal true
        let page = result.PageContent.Value
        page.Content |> should haveSubstring "Just content"
    )
