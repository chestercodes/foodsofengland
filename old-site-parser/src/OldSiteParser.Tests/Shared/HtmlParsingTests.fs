module OldSiteParser.Tests.Shared.HtmlParsingTests

open Xunit
open FsUnit.Xunit
open OldSiteParser.Domain.Types
open OldSiteParser.Shared.HtmlParsing

// --- extractBodyContent ---

[<Fact>]
let ``extractBodyContent finds TEXT START/END markers`` () =
    let html = """<html><body>
    before
    <!---TEXT START--->
    <p>Hello world</p>
    <!---TEXT END--->
    after
    </body></html>"""

    let (content, template) = extractBodyContent html
    template |> should equal "text-start"
    content |> should haveSubstring "Hello world"
    content |> should not' (haveSubstring "before")
    content |> should not' (haveSubstring "after")

[<Fact>]
let ``extractBodyContent finds BODY TEXT START/END markers`` () =
    let html = """<html><body>
    header
    <!---BODY TEXT START--->
    <p>Body content here</p>
    <!---BODY TEXT END--->
    footer
    </body></html>"""

    let (content, template) = extractBodyContent html
    template |> should equal "body-text-start"
    content |> should haveSubstring "Body content here"

[<Fact>]
let ``extractBodyContent finds PAGE CONTENT marker to EOF`` () =
    let html = """<html><body>
    header
    <!--PAGE CONTENT-->
    <p>Page content to end</p>
    </body></html>"""

    let (content, template) = extractBodyContent html
    template |> should equal "page-content"
    content |> should haveSubstring "Page content to end"

[<Fact>]
let ``extractBodyContent falls back to body when no markers`` () =
    let html = """<html><body><p>Just body</p></body></html>"""

    let (content, template) = extractBodyContent html
    template |> should equal "none"
    content |> should haveSubstring "Just body"

[<Fact>]
let ``extractBodyContent uses first matching marker`` () =
    let html = """<html><body>
    <!---TEXT START--->
    <p>First template</p>
    <!---TEXT END--->
    <!---BODY TEXT START--->
    <p>Second template</p>
    <!---BODY TEXT END--->
    </body></html>"""

    let (content, template) = extractBodyContent html
    template |> should equal "text-start"
    content |> should haveSubstring "First template"

// --- extractUrls ---

[<Fact>]
let ``extractUrls finds anchor hrefs`` () =
    let html = """<a href="page.htm">Link</a>"""
    let urls = extractUrls html
    urls |> should haveLength 1
    urls.[0].RawUrl |> should equal (RawUrl "page.htm")
    urls.[0].TagType |> should equal "a"

[<Fact>]
let ``extractUrls finds img srcs with alt text`` () =
    let html = """<img src="photo.jpg" alt="A photo">"""
    let urls = extractUrls html
    urls |> should haveLength 1
    urls.[0].RawUrl |> should equal (RawUrl "photo.jpg")
    urls.[0].TagType |> should equal "img"
    urls.[0].AltText |> should equal (Some "A photo")

[<Fact>]
let ``extractUrls finds multiple tags`` () =
    let html = """
    <a href="page1.htm">Link 1</a>
    <img src="img1.jpg">
    <a href="page2.htm">Link 2</a>
    <link href="style.css">
    """

    let urls = extractUrls html
    urls |> should haveLength 4

[<Fact>]
let ``extractUrls skips empty URLs`` () =
    let html = """<a href="">Empty</a><a href="real.htm">Real</a>"""
    let urls = extractUrls html
    urls |> should haveLength 1
    urls.[0].RawUrl |> should equal (RawUrl "real.htm")

// --- isLocalUrl ---

[<Fact>]
let ``isLocalUrl returns true for relative paths`` () =
    isLocalUrl "images/photo.jpg" |> should equal true

[<Fact>]
let ``isLocalUrl returns true for files`` () =
    isLocalUrl "listbicuits.htm" |> should equal true

[<Fact>]
let ``isLocalUrl returns false for bad external urls 1`` () =
    isLocalUrl "http:museumoflondon.org.uk" |> should equal false

[<Fact>]
let ``isLocalUrl returns true for foodsofengland.co.uk`` () =
    isLocalUrl "http://www.foodsofengland.co.uk/page.htm"
    |> should equal true

[<Fact>]
let ``isLocalUrl returns false for tweet about foodsofengland.co.uk`` () =
    isLocalUrl "https://twitter.com/intent/tweet?text=More from Foods of England - Yorkshire Curd Tart @foodsofengland www.foodsofengland.co.uk/yorkshirecurdtartorcheesecake.htm pic.twitter.com/Ailq0QR8vG"
    |> should equal false

[<Fact>]
let ``isLocalUrl returns false for external URLs`` () =
    isLocalUrl "https://www.google.com" |> should equal false

[<Fact>]
let ``isLocalUrl returns false for mailto`` () =
    isLocalUrl "mailto:test@example.com" |> should equal false

[<Fact>]
let ``isLocalUrl returns false for empty string`` () =
    isLocalUrl "" |> should equal false

// --- stripFragment ---

[<Fact>]
let ``stripFragment removes hash fragment`` () =
    stripFragment "references.htm#Garrett 1891"
    |> should equal "references.htm"

[<Fact>]
let ``stripFragment leaves URL without fragment unchanged`` () =
    stripFragment "page.htm" |> should equal "page.htm"

// --- extractPathPortion ---

[<Fact>]
let ``extractPathPortion strips scheme and host for absolute URL`` () =
    extractPathPortion "http://www.foodsofengland.co.uk/page.htm"
    |> should equal "page.htm"

[<Fact>]
let ``extractPathPortion leaves relative URL unchanged`` () =
    extractPathPortion "../images/photo.jpg"
    |> should equal "../images/photo.jpg"
