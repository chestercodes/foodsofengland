module OldSiteParser.Tests.Steps.Step6MetadataTests

open System
open System.IO
open System.Text.Json
open Xunit
open OldSiteParser.Domain.Types
open OldSiteParser.Steps.Step6_Extract

let private getTestCasePath (baseName: string) =
    Path.Combine("Steps", "Step6Files", baseName)

let private readTestFile (baseName: string) (fileName: string) =
    File.ReadAllText(Path.Combine(getTestCasePath baseName, fileName))

let private runFileTest (baseName: string) =
    let inputContent = readTestFile baseName "content.in.html"
    let relativePath = RelativePath (baseName + ".html")
    processHtmlFile inputContent relativePath

let private normalizeSnapshotContent (content: string) : string =
    content.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd('\n')

let private normalizeSnapshotValue (value: string) : string =
    normalizeSnapshotContent value

let private normalizeReceiptSnapshotValue (value: string) : string =
    value
    |> normalizeSnapshotContent
    |> fun normalized -> System.Text.RegularExpressions.Regex.Replace(normalized, @"^(?:<br>\s*)+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase)

let private serializeCanonical (items: obj array) =
    let options = JsonSerializerOptions(WriteIndented = true)
    JsonSerializer.Serialize(items, options)

let private canonicalizeMetadata (items: (string * string) seq) =
    items
    |> Seq.sortBy id
    |> Seq.map (fun (key, value) -> box {| key = key; value = normalizeSnapshotValue value |})
    |> Seq.toArray
    |> serializeCanonical

let private canonicalizeReceipts (items: (string * string) seq) =
    items
    |> Seq.sortBy id
    |> Seq.map (fun (receiptId, receiptText) -> box {| receiptId = receiptId; receiptText = normalizeSnapshotValue receiptText |})
    |> Seq.toArray
    |> serializeCanonical

let private canonicalizeReceiptTriples (items: (string * string * string) seq) =
    items
    |> Seq.sortBy (fun (receiptId, _, _) -> receiptId)
    |> Seq.map (fun (receiptId, receiptSource, receiptText) ->
        box
            {| receiptId = receiptId
               receiptSource = normalizeSnapshotValue receiptSource
               receiptText = normalizeReceiptSnapshotValue receiptText |})
    |> Seq.toArray
    |> serializeCanonical

let private canonicalizeRelatedPages (items: (string * string * string) seq) =
    items
    |> Seq.sortBy id
    |> Seq.map (fun (relationDescription, relatedPageText, relatedPageUrl) ->
        box {| relationDescription = normalizeSnapshotValue relationDescription
               relatedPageText = normalizeSnapshotValue relatedPageText
               relatedPageUrl = normalizeSnapshotValue relatedPageUrl |})
    |> Seq.toArray
    |> serializeCanonical

let private canonicalizeVerifiedMetadata (json: string) =
    use document = JsonDocument.Parse(json)

    document.RootElement.EnumerateArray()
    |> Seq.map (fun element ->
        element.GetProperty("key").GetString(),
        element.GetProperty("value").GetString())
    |> canonicalizeMetadata

let private canonicalizeVerifiedReceipts (text: string) =
    text.Replace("\r\n", "\n").Replace("\r", "\n")
    |> fun normalized -> normalized.Split([| "\n------\n" |], StringSplitOptions.RemoveEmptyEntries)
    |> Seq.map (fun block ->
        let lines = block.Split('\n')
        let headerLines =
            lines
            |> Array.takeWhile (fun line ->
                let trimmed = line.TrimStart()
                trimmed.StartsWith("id:", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("source:", StringComparison.OrdinalIgnoreCase)
                || String.IsNullOrWhiteSpace(line))

        let receiptId =
            headerLines
            |> Array.find (fun line -> line.TrimStart().StartsWith("id:", System.StringComparison.OrdinalIgnoreCase))
            |> fun line -> line.Substring(line.IndexOf(':') + 1).Trim().Trim('"')

        let receiptSource =
            headerLines
            |> Array.find (fun line -> line.TrimStart().StartsWith("source:", System.StringComparison.OrdinalIgnoreCase))
            |> fun line -> line.Substring(line.IndexOf(':') + 1).Trim().Trim('"')

        let remainingLines = lines |> Array.skip headerLines.Length
        let receiptTextLines =
            if remainingLines.Length > 0 && remainingLines.[0].Trim() = "---" then
                remainingLines |> Array.skip 1
            else
                remainingLines

        let receiptText = String.Join("\n", receiptTextLines)
        receiptId, receiptSource, receiptText)
    |> canonicalizeReceiptTriples

let private canonicalizeVerifiedRelatedPages (json: string) =
    use document = JsonDocument.Parse(json)

    document.RootElement.EnumerateArray()
    |> Seq.map (fun element ->
        element.GetProperty("relationDescription").GetString(),
        element.GetProperty("relatedPageText").GetString(),
        element.GetProperty("relatedPageUrl").GetString())
    |> canonicalizeRelatedPages

[<Theory>]
[<InlineData("almond-cake")>]
[<InlineData("bacon")>]
[<InlineData("barley-bread")>]
[<InlineData("barnstable-gingerbread")>]
[<InlineData("eel-pie")>]
let ``processHtmlFile matches snapshot`` (baseName: string) =
    let actualResult = runFileTest baseName

    let expectedContent =
        readTestFile baseName "content.verified.html"
        |> normalizeSnapshotContent

    let expectedMetadataJson =
        readTestFile baseName "metadata.verified.json"
        |> canonicalizeVerifiedMetadata

    let expectedReceiptsJson =
        readTestFile baseName "receipts.verified.txt"
        |> canonicalizeVerifiedReceipts

    let expectedRelatedPagesJson =
        readTestFile baseName "related-pages.verified.json"
        |> canonicalizeVerifiedRelatedPages

    let actualContent = normalizeSnapshotContent actualResult.Content

    let actualMetadataJson =
        actualResult.Metadata
        |> Seq.map (fun item -> item.MetadataKey, item.MetadataValue)
        |> canonicalizeMetadata

    let actualReceiptsJson =
        actualResult.Receipts
        |> Seq.map (fun item -> item.ReceiptId, item.ReceiptSource, item.ReceiptText)
        |> canonicalizeReceiptTriples

    let actualRelatedPagesJson =
        actualResult.RelatedPages
        |> Seq.map (fun item -> item.RelationDescription, item.RelatedPageText, item.RelatedPageUrl)
        |> canonicalizeRelatedPages

    Assert.Equal(expectedContent, actualContent)
    Assert.Equal(expectedMetadataJson, actualMetadataJson)
    Assert.Equal(expectedReceiptsJson, actualReceiptsJson)
    Assert.Equal(expectedRelatedPagesJson, actualRelatedPagesJson)
