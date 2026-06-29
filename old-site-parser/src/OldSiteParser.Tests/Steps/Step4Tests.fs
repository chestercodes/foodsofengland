module OldSiteParser.Tests.Steps.Step4CustomTagTests

open System.IO
open System.Text.RegularExpressions
open System.Text.Json
open Xunit
open FsUnit.Xunit
open OldSiteParser.Domain.Types
open OldSiteParser.Steps.Step4_PreTidy

let private testFinalName = FinalName "step4-custom-tag-test.html"

let private runProcessCustomTags (content: string) =
    processCustomTags testFinalName content

// --- Production custom-tag behavior via processCustomTags ---

[<Fact>]
let ``processCustomTags removes class tag and extracts class and list-page-reference`` () =
    let input = """<class><a href="listcakes.htm"><b><big>Cakes</big></b></a></class>"""
    let output, tags = runProcessCustomTags input

    output |> should equal ""
    tags.Length |> should equal 2

    let classTag = tags |> List.find (fun t -> t.Name = "class")
    classTag.Value |> should equal "cakes"

    let listTag = tags |> List.find (fun t -> t.Name = "list-page-reference")
    listTag.Value |> should equal "listcakes.htm"

[<Fact>]
let ``processCustomTags extracts subclass tag`` () =
    let input = """<subclass><i>Historic</i></subclass>"""
    let output, tags = runProcessCustomTags input

    output |> should equal ""
    tags.Length |> should equal 1

    let tag = tags |> List.head
    tag.Name |> should equal "subclass"
    tag.Value |> should equal "historic"

[<Fact>]
let ``processCustomTags handles doubled opener closing`` () =
    let input = """<class><a href="listcakes.htm"><b><big>Cakes</big></b></a><class>"""
    let output, tags = runProcessCustomTags input

    output |> should equal ""
    tags.Length |> should equal 2

    let classTag = tags |> List.find (fun t -> t.Name = "class")
    classTag.Value |> should equal "cakes"

[<Fact>]
let ``processCustomTags handles misplaced nesting`` () =
    let input = """<locale><i>Yorkshire</locale></i>"""
    let output, tags = runProcessCustomTags input

    output |> should equal ""
    tags.Length |> should equal 1

    let tag = tags |> List.head
    tag.Name |> should equal "locale"
    tag.Value |> should equal "Yorkshire"

[<Fact>]
let ``processCustomTags handles multiple custom tags and preserves order`` () =
    let input =
        """
        <p>Some text</p>
        <class><b>Cakes</b></class>
        <p>More text</p>
        <locale>Devon</locale>
        <subclass><i>Modern</i></subclass>
        """

    let output, tags = runProcessCustomTags input

    output |> should not' (haveSubstring "<class>")
    output |> should not' (haveSubstring "<locale>")
    output |> should not' (haveSubstring "<subclass>")

    let tagNames = tags |> List.map (fun t -> t.Name)
    tagNames |> should contain "class"
    tagNames |> should contain "locale"
    tagNames |> should contain "subclass"

    let tagValues = tags |> List.filter (fun t -> t.Name = "class" || t.Name = "locale" || t.Name = "subclass") |> List.map (fun t -> t.Value)
    tagValues |> should contain "cakes"
    tagValues |> should contain "Devon"
    tagValues |> should contain "modern"

[<Fact>]
let ``processCustomTags leaves non custom tags unchanged`` () =
    let input = """<customthing>content</customthing>"""
    let output, tags = runProcessCustomTags input

    output |> should equal "<customthing>content</customthing>"
    tags.Length |> should equal 0

[<Fact>]
let ``extractInnerText uses production implementation`` () =
    extractInnerText "<b><i>Breads</i></b>" |> should equal "Breads"

[<Fact>]
let ``extractCategoryLink uses production implementation`` () =
    extractCategoryLink "<a href=\"listcakes.htm\"><b>Cakes</b></a>" |> should equal (Some "listcakes.htm")
    extractCategoryLink "<b>No link</b>" |> should equal None

[<Fact>]
let ``extractHeading removes font wrapped heading without leaving wrapper artifacts`` () =
    let input = """<font color="B81010"><big><big><big><big><b>Albion Pudding</b></big></big></big></big><br><br></font><br>Body"""
    let output, tags = extractHeading input

    output |> should equal "<br>Body"
    tags |> should equal [ { Name = "heading"; Value = "Albion Pudding" } ]

[<Fact>]
let ``extractHeading removes reopened trailing font artifact`` () =
    let input = """<font color="#B81010"> <big><big><big><big><big><b>Aerated Bread</b></big></big></big></big></big><br><br><font color="#"><br>Body"""
    let output, tags = extractHeading input

    output |> should equal "<br>Body"
    tags |> should equal [ { Name = "heading"; Value = "Aerated Bread" } ]

[<Fact>]
let ``customTagRegex uses production pattern`` () =
    customTagRegex.IsMatch "<class><b>Breads</b></class>" |> should equal true
    customTagRegex.IsMatch "<customthing>content</customthing>" |> should equal false

// --- File-based processContent tests ---

type TagJson = { name: string; value: string }

let private getTestCasePath (baseName: string) =
    Path.Combine("Steps", "Step4Files", baseName)

let private readTestFile (baseName: string) (fileName: string) =
    File.ReadAllText(Path.Combine(getTestCasePath baseName, fileName))

let private runFileTest (baseName: string) =
    let beforeContent = readTestFile baseName "content.in.html"
    let finalName = FinalName (baseName + ".html")

    let (processedContent, actualTags) = processContent finalName beforeContent
    (processedContent, actualTags)

let private normalizeSnapshotContent (content: string) : string =
    content.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd('\n')

let private canonicalizeTagPairs (tags: (string * string) seq) =
    let options = JsonSerializerOptions(WriteIndented = true)
    tags
    |> Seq.sortBy id
    |> Seq.map (fun (name, value) -> {| name = name; value = value |})
    |> Seq.toArray
    |> fun canonicalTags -> JsonSerializer.Serialize(canonicalTags, options)

let private serializeActualTags (actualTags: TagOutput list) =
    actualTags
    |> Seq.map (fun tag -> tag.Name, tag.Value)
    |> canonicalizeTagPairs

let private canonicalizeVerifiedTags (json: string) =
    use document = JsonDocument.Parse(json)

    document.RootElement.EnumerateArray()
    |> Seq.map (fun element ->
        element.GetProperty("name").GetString(),
        element.GetProperty("value").GetString())
    |> canonicalizeTagPairs

[<Theory>]
[<InlineData("aerated-bread")>]
[<InlineData("abingdon-buns")>]
[<InlineData("albion-pudding")>]
[<InlineData("apple-rose-jelly")>]
[<InlineData("barley-bread")>]
[<InlineData("barley-soup")>]
[<InlineData("eel-pie")>]
[<InlineData("egg-hot")>]
[<InlineData("horsham-biscuits")>]
[<InlineData("herb-jelly")>]
let ``processContent matches snapshot`` (baseName: string) =
    let actualContent, actualTags = runFileTest baseName

    let expectedContent =
        readTestFile baseName "content.verified.html"
        |> normalizeSnapshotContent

    let expectedTagsJson =
        readTestFile baseName "tags.verified.json"
        |> canonicalizeVerifiedTags

    let actualContent = normalizeSnapshotContent actualContent
    let actualTagsJson = serializeActualTags actualTags

    Assert.Equal(expectedContent, actualContent)
    Assert.Equal(expectedTagsJson, actualTagsJson)

[<Fact>]
let ``processContent removes empty wrappers from abingdon buns edges`` () =
    let actualContent, _ = runFileTest "abingdon-buns"
    let normalizedLineEndings = actualContent.Replace("\r\n", "\n").Replace("\r", "\n")

    normalizedLineEndings.StartsWith("The town of Abingdon", System.StringComparison.Ordinal)
    |> should equal true

    normalizedLineEndings.EndsWith("For another civic celebration food, see: <a href=\"denbydalepie.htm\"><b>Denby Dale Pie</b></a>", System.StringComparison.Ordinal)
    |> should equal true

    normalizedLineEndings.Contains("<center></center>")
    |> should equal false

[<Fact>]
let ``processContent removes aerated bread page chrome`` () =
    let actualContent, _ = runFileTest "aerated-bread"
    let normalizedLineEndings = actualContent.Replace("\r\n", "\n").Replace("\r", "\n")

    normalizedLineEndings.StartsWith("A form of bread aerated directly with carbon dioxide gas", System.StringComparison.Ordinal)
    |> should equal true

    normalizedLineEndings.Contains("<form action=\"http://www.google.com/search\"")
    |> should equal false

    normalizedLineEndings.Contains("<hr size=\"4\" color=\"#B81010\">")
    |> should equal false

    normalizedLineEndings.EndsWith("</font>", System.StringComparison.Ordinal)
    |> should equal false

[<Fact>]
let ``processContent removes apple rose jelly legacy footer chrome`` () =
    let actualContent, _ = runFileTest "apple-rose-jelly"
    let normalizedLineEndings = actualContent.Replace("\r\n", "\n").Replace("\r", "\n")
    let normalizedContent = normalizeSnapshotContent actualContent

    normalizedContent.StartsWith("Clear apple jelly with rose petals mixed-in.", System.StringComparison.Ordinal)
    |> should equal true

    normalizedLineEndings.Contains("Sitemap")
    |> should equal false

    normalizedLineEndings.Contains("WHIMBERRY")
    |> should equal false

    normalizedLineEndings.Contains("</body>")
    |> should equal false
