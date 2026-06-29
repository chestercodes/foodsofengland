module OldSiteParser.Shared.HtmlParsing

open System
open System.Text.RegularExpressions
open HtmlAgilityPack
open OldSiteParser.Domain

/// Extract the <title> tag value from an HTML file.
/// Returns None if no title tag is found.
let extractTitle (filePath: string) : string option =
    try
        let doc = HtmlDocument()
        doc.Load(filePath)
        let titleNode = doc.DocumentNode.SelectSingleNode("//title")

        match titleNode with
        | null -> None
        | node ->
            let text = node.InnerText.Trim()
            if String.IsNullOrWhiteSpace(text) then None else Some text
    with _ ->
        None

/// Known start/end comment marker pairs for body content extraction.
/// Order matters — first match wins.
let private markerPairs =
    [ ("<!---TEXT START--->", Some "<!---TEXT END--->")
      ("<!---BODY TEXT START--->", Some "<!---BODY TEXT END--->")
      ("<!--PAGE CONTENT-->", None) ] // None = read to EOF or next end marker

/// Extract body content from an HTML file using known comment markers.
/// Returns (content, templateType).
let extractBodyContent (fileContent: string) : string * string =
    let tryFindMarker (startMarker: string) (endMarker: string option) =
        let startIdx = fileContent.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase)

        if startIdx < 0 then
            None
        else
            let contentStart = startIdx + startMarker.Length

            match endMarker with
            | Some endM ->
                let endIdx = fileContent.IndexOf(endM, contentStart, StringComparison.OrdinalIgnoreCase)

                if endIdx < 0 then
                    Some(fileContent.Substring(contentStart))
                else
                    Some(fileContent.Substring(contentStart, endIdx - contentStart))
            | None ->
                // For PAGE CONTENT, try common end markers, else read to EOF
                let possibleEnds =
                    [ "<!---TEXT END--->"
                      "<!---BODY TEXT END--->" ]

                let endIdx =
                    possibleEnds
                    |> List.map (fun e -> fileContent.IndexOf(e, contentStart, StringComparison.OrdinalIgnoreCase))
                    |> List.filter (fun i -> i >= 0)
                    |> List.sort
                    |> List.tryHead

                match endIdx with
                | Some idx -> Some(fileContent.Substring(contentStart, idx - contentStart))
                | None -> Some(fileContent.Substring(contentStart))

    // Try each marker pair in order
    let result =
        markerPairs
        |> List.tryPick (fun (startM, endM) ->
            let content = tryFindMarker startM endM

            match content with
            | Some c ->
                let templateType =
                    if startM.Contains("BODY TEXT START") then "body-text-start"
                    elif startM.Contains("TEXT START") then "text-start"
                    else "page-content"

                Some(c, templateType)
            | None -> None)

    match result with
    | Some(content, template) -> (content.Trim(), template)
    | None ->
        // Fallback: try extracting <body> content
        let doc = HtmlDocument()
        doc.LoadHtml(fileContent)
        let bodyNode = doc.DocumentNode.SelectSingleNode("//body")

        match bodyNode with
        | null -> (fileContent, "none")
        | node -> (node.InnerHtml.Trim(), "none")

/// Parsed URL record from HTML content.
type ParsedUrlInfo =
    { RawUrl: RawUrl
      TagType: string
      AltText: string option }

/// Extract all URLs from HTML content (a, img, link, script tags).
let extractUrls (htmlContent: string) : ParsedUrlInfo list =
    let doc = HtmlDocument()
    doc.LoadHtml(htmlContent)

    let extractFromNodes (tagName: string) (attrName: string) =
        let nodes = doc.DocumentNode.SelectNodes(sprintf "//%s[@%s]" tagName attrName)

        match nodes with
        | null -> []
        | _ ->
            nodes
            |> Seq.map (fun node ->
                let url = node.GetAttributeValue(attrName, "")
                let alt = node.GetAttributeValue("alt", null)

                { RawUrl = RawUrl url
                  TagType = tagName
                  AltText = if String.IsNullOrEmpty(alt) then None else Some alt })
            |> Seq.filter (fun u ->
                let (RawUrl rawUrl) = u.RawUrl
                not (String.IsNullOrWhiteSpace(rawUrl))
            )
            |> Seq.toList

    [ yield! extractFromNodes "a" "href"
      yield! extractFromNodes "img" "src"
      yield! extractFromNodes "link" "href"
      yield! extractFromNodes "script" "src" ]

let badSchemePattern = Regex("^http:[a-zA-Z]+.*")
/// Determine if a URL is relative/local to the site.
/// Returns true for relative paths and foodsofengland.co.uk URLs.
let isLocalUrl (url: string) : bool =
    if String.IsNullOrWhiteSpace(url) then
        false
    elif url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) then
        false
    elif url.StartsWith("http://matrixstats.foodsofengland.co.uk/", StringComparison.OrdinalIgnoreCase) then
        false
    elif url.Contains("www.bbc.co.uk", StringComparison.OrdinalIgnoreCase) then
        false
    else
        if url.Contains(":") = false then
            true // No scheme, treat as relative/local
        else
            if badSchemePattern.IsMatch(url) then
                false // Malformed absolute URL (e.g. "http:example.com")
            else
                try
                    let uri = Uri(url)
                    if uri.IsAbsoluteUri = false then
                        true
                    else
                        if uri.Host.Contains("foodsofengland.co.uk", StringComparison.OrdinalIgnoreCase) then
                            true
                        else
                            false
                with _ ->
                    false

let getLocalPart (url: string) : string option =
    if String.IsNullOrWhiteSpace(url) then
        None
    elif url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) then
        None
    else
        let uri = Uri(url)
        if uri.IsAbsoluteUri = false then
            uri.LocalPath |> Some
        else
            if uri.Host.Contains("foodsofengland.co.uk", StringComparison.OrdinalIgnoreCase) then
                uri.LocalPath |> Some
            else
                None

/// Encode a raw URL by normalizing percent-encoding.
/// Decodes any existing percent-encoding, then re-encodes each path segment consistently.
let encodeUrl (rawUrl: string) : string =
    if String.IsNullOrWhiteSpace(rawUrl) then rawUrl
    else
        try
            let decoded = Uri.UnescapeDataString(rawUrl)
            let parts = decoded.Split([|'/'|])
            parts |> Array.map Uri.EscapeDataString |> String.concat "/"
        with _ -> rawUrl

/// Strip the hash fragment from a URL for file-existence checking.
let stripFragment (url: string) : string =
    let idx = url.IndexOf('#')
    if idx >= 0 then url.Substring(0, idx) else url

/// Strip query parameters from a URL.
let stripQueryParams (url: string) : string =
    let idx = url.IndexOf('?')
    if idx >= 0 then url.Substring(0, idx) else url

/// Extract just the path portion from a URL (strip scheme+host for absolute foodsofengland URLs).
let extractPathPortion (url: string) : string =
    if url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
       || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) then
        try
            let uri = Uri(url)
            uri.AbsolutePath.TrimStart('/')
        with _ ->
            url
    else
        url
