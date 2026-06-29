module OldSiteParser.Tests.Steps.Step4CustomTagTests

open Xunit
open FsUnit.Xunit
open OldSiteParser.Domain.Types
open OldSiteParser.Steps.Step4_PreTidy

let private testFinalName = FinalName "step4-custom-tag-test.html"

let private runProcessCustomTags (content: string) =
    processCustomTags testFinalName content

// --- Production custom-tag behavior via processCustomTags ---

[<Fact>]
let ``processCustomTags replaces class tag and extracts class entity with link`` () =
    let input = """<class><a href="listcakes.htm"><b><big>Cakes</big></b></a></class>"""
    let output, entities = runProcessCustomTags input

    output |> should equal "<!-- class: Cakes -->"
    entities.Length |> should equal 1

    let tag = entities |> List.head
    tag.RelativePath |> should equal "step4-custom-tag-test.html"
    tag.TagName |> should equal "class"
    tag.TextContent |> should equal "Cakes"
    tag.CategoryLinkUrl |> should equal "listcakes.htm"

[<Fact>]
let ``processCustomTags extracts subclass tag and leaves category link null`` () =
    let input = """<subclass><i>Historic</i></subclass>"""
    let output, entities = runProcessCustomTags input

    output |> should equal "<!-- subclass: Historic -->"
    entities.Length |> should equal 1

    let tag = entities |> List.head
    tag.TagName |> should equal "subclass"
    tag.TextContent |> should equal "Historic"
    tag.CategoryLinkUrl |> should equal null

[<Fact>]
let ``processCustomTags handles doubled opener closing`` () =
    let input = """<class><a href="listcakes.htm"><b><big>Cakes</big></b></a><class>"""
    let output, entities = runProcessCustomTags input

    output |> should equal "<!-- class: Cakes -->"
    entities.Length |> should equal 1

    let tag = entities |> List.head
    tag.TagName |> should equal "class"
    tag.TextContent |> should equal "Cakes"
    tag.CategoryLinkUrl |> should equal "listcakes.htm"

[<Fact>]
let ``processCustomTags handles misplaced nesting`` () =
    let input = """<locale><i>Yorkshire</locale></i>"""
    let output, entities = runProcessCustomTags input

    output |> should equal "<!-- locale: Yorkshire -->"
    entities.Length |> should equal 1

    let tag = entities |> List.head
    tag.TagName |> should equal "locale"
    tag.TextContent |> should equal "Yorkshire"
    tag.CategoryLinkUrl |> should equal null

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

    let output, entities = runProcessCustomTags input

    output |> should not' (haveSubstring "<class>")
    output |> should not' (haveSubstring "<locale>")
    output |> should not' (haveSubstring "<subclass>")
    output |> should haveSubstring "<!-- class: Cakes -->"
    output |> should haveSubstring "<!-- locale: Devon -->"
    output |> should haveSubstring "<!-- subclass: Modern -->"

    entities.Length |> should equal 3
    entities |> List.map (fun e -> e.TagName) |> should equal [ "class"; "locale"; "subclass" ]
    entities |> List.map (fun e -> e.TextContent) |> should equal [ "Cakes"; "Devon"; "Modern" ]

[<Fact>]
let ``processCustomTags leaves non custom tags unchanged`` () =
    let input = """<customthing>content</customthing>"""
    let output, entities = runProcessCustomTags input

    output |> should equal "<customthing>content</customthing>"
    entities.Length |> should equal 0

[<Fact>]
let ``extractInnerText uses production implementation`` () =
    extractInnerText "<b><i>Breads</i></b>" |> should equal "Breads"

[<Fact>]
let ``extractCategoryLink uses production implementation`` () =
    extractCategoryLink "<a href=\"listcakes.htm\"><b>Cakes</b></a>" |> should equal (Some "listcakes.htm")
    extractCategoryLink "<b>No link</b>" |> should equal None

[<Fact>]
let ``customTagRegex uses production pattern`` () =
    customTagRegex.IsMatch "<class><b>Breads</b></class>" |> should equal true
    customTagRegex.IsMatch "<customthing>content</customthing>" |> should equal false
