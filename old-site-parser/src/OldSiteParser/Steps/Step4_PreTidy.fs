module OldSiteParser.Steps.Step4_PreTidy
// Step 4: Pre-tidy HTML processing
// this aims to
// - strip out the html that might be complained about by tidy, if html contains useful info then add as a returned tag
// - change the references links to point to the new references.html page and extract the reference name as a tag


open System
open System.IO
open System.Linq
open System.Text.RegularExpressions
open OldSiteParser.Database
open OldSiteParser.Domain.Types
open OldSiteParser.Domain.Mapping
open OldSiteParser.Shared

type ReplacementUrl =
    | FoundFile of FinalName
    | NotFound of RawUrl

type TagOutput = { Name: string; Value: string }

// --- Custom tag extraction ---

/// Regex for <class>, <subclass>, <locale> with proper close, doubled opener, or misplaced nesting.
/// Handles: <tag>content</tag>, <tag>content<tag>, <locale><i>text</locale></i>
let customTagPattern =
    @"<(class|subclass|locale)>(.*?)<(?:/\1|\1)>(?:</i>)?"

let customTagRegex =
    Regex(customTagPattern, RegexOptions.IgnoreCase ||| RegexOptions.Singleline)

/// Pattern for orphaned class content (left behind after multi-class lines):
/// matches "<content><class>" where there's no opening <class> but a closing one
let orphanedClassPattern =
    @"(<a\s[^>]*>.*?</a>)\s*<(class|subclass|locale)>"

let orphanedClassRegex =
    Regex(orphanedClassPattern, RegexOptions.IgnoreCase ||| RegexOptions.Singleline)

/// Extract the inner text from a custom tag match, stripping inner HTML tags.
let extractInnerText (html: string) : string =
    Regex.Replace(html, @"<[^>]+>", "").Trim()

/// Extract category link URL from a <class> tag that wraps an <a> tag.
let extractCategoryLink (html: string) : string option =
    let m = Regex.Match(html, @"<a\s+href=""([^""]+)""", RegexOptions.IgnoreCase)
    if m.Success then Some m.Groups.[1].Value else None

let cleanClassValue (value: string) : string =
    let clean = value.Trim()
    match clean with
    | s when String.Equals(s, "Accompaniments", StringComparison.OrdinalIgnoreCase) -> "accompaniments"
    | s when String.Equals(s, "Breads", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "Breads ", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "Breads - Sweet", StringComparison.OrdinalIgnoreCase) -> "breads"
    | s when String.Equals(s, "Biscuits", StringComparison.OrdinalIgnoreCase) -> "biscuits"
    | s when String.Equals(s, "Cakes", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "Cakes and Scones", StringComparison.OrdinalIgnoreCase) -> "cakes"
    | s when String.Equals(s, "Cheeses", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "Cheese Dishes", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "Cheese", StringComparison.OrdinalIgnoreCase) -> "cheese"
    | s when String.Equals(s, "Raj Dishes", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "curry", StringComparison.OrdinalIgnoreCase) -> "curry"
    | s when String.Equals(s, "Dairy", StringComparison.OrdinalIgnoreCase) -> "dairy"
    | s when String.Equals(s, "Drinks", StringComparison.OrdinalIgnoreCase) -> "drinks"
    | s when String.Equals(s, "Eggs", StringComparison.OrdinalIgnoreCase) -> "eggs"
    | s when String.Equals(s, "Fish", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "Fish - Shellfish", StringComparison.OrdinalIgnoreCase) -> "fish"
    | s when String.Equals(s, "Game and Offal", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "Offal", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "Meats Offal", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "game-and-offal", StringComparison.OrdinalIgnoreCase) -> "game-and-offal"
    | s when String.Equals(s, "Classic Meals", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "meals", StringComparison.OrdinalIgnoreCase) -> "meals"
    | s when String.Equals(s, "Meat and Meat Dishes", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "Meats", StringComparison.OrdinalIgnoreCase) -> "meats"
    | s when String.Equals(s, "Puddings and Sweet Deserts", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "Puddings - Sweet Deserts", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "Puddings", StringComparison.OrdinalIgnoreCase) ||
             String.Equals(s, "Puddings and Sweet Desserts", StringComparison.OrdinalIgnoreCase) ||
             String.Equals(s, "Puddings & Sweets", StringComparison.OrdinalIgnoreCase) -> "puddings-and-sweet-deserts"
    | s when String.Equals(s, "Pot Meals", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "pot-meals", StringComparison.OrdinalIgnoreCase) -> "pot-meals"
    | s when String.Equals(s, "Pies and Pastries", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "Pies - Savoury", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "Pastries - Savoury Top Crust Pies", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "Pastries - Sweet Pies", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "Pastries - Sweet Open Pies", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "Pastries", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "pies-and-pastries", StringComparison.OrdinalIgnoreCase) -> "pies-and-pastries"
    | s when String.Equals(s, "Preserves", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "Preserves - Sweet", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "Preserves - Savoury", StringComparison.OrdinalIgnoreCase) ||
             String.Equals(s, "Preserves & Jams", StringComparison.OrdinalIgnoreCase) -> "preserves"
    | s when String.Equals(s, "Poultry", StringComparison.OrdinalIgnoreCase) -> "poultry"
    | s when String.Equals(s, "Sauces and Spicery", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "Sauces - Pouring", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "Sauces - Table", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "sauces-and-spicery", StringComparison.OrdinalIgnoreCase) -> "sauces-and-spicery"
    | s when String.Equals(s, "Sausages", StringComparison.OrdinalIgnoreCase) -> "sausages"
    | s when String.Equals(s, "Snacks", StringComparison.OrdinalIgnoreCase) -> "snacks"
    | s when String.Equals(s, "Soups", StringComparison.OrdinalIgnoreCase) -> "soups"
    | s when String.Equals(s, "Sweets and Toffee", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "Sweets", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "sweets-and-toffee", StringComparison.OrdinalIgnoreCase) -> "sweets-and-toffee"
    | s when String.Equals(s, "Fruit and Vegetables", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "Vegetables", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "Fruit", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "fruit-and-vegetables", StringComparison.OrdinalIgnoreCase) ||
             String.Equals(s, "Fruits & Vegetables", StringComparison.OrdinalIgnoreCase) -> "fruit-and-vegetables"
    | other -> other.ToLowerInvariant().Replace(" ", "-")

let cleanSubclassValue (value: string) : string =
    let clean = value.Trim()
    match clean with
    | s when String.Equals(s, "Historic", StringComparison.OrdinalIgnoreCase) -> "historic"
    | s when String.Equals(s, "Endangered", StringComparison.OrdinalIgnoreCase) -> "endangered"
    | s when String.Equals(s, "EU Protected Name", StringComparison.OrdinalIgnoreCase) -> "eu-protected-name"
    | s when String.Equals(s, "Possibly Extinct", StringComparison.OrdinalIgnoreCase) -> "possibly-extinct"
    | s when String.Equals(s, "Extinct", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "Extinct & Lost", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "Lost", StringComparison.OrdinalIgnoreCase) || 
             String.Equals(s, "Historic, Lost", StringComparison.OrdinalIgnoreCase) -> "extinct-and-lost"
    | s when String.Equals(s, "Formerly EU Protected Name", StringComparison.OrdinalIgnoreCase) -> "formerly-eu-protected-name"
    | other -> other.ToLowerInvariant().Replace(" ", "-")

let buildCustomTagGroup (tagName: string) (innerHtml: string) : TagOutput list =
    let textContent = extractInnerText innerHtml

    match tagName with
    | "class" ->
        let cleanClass = cleanClassValue textContent
        match extractCategoryLink innerHtml with
        | Some url ->
            [ { Name = "list-page-reference"; Value = url }
              { Name = "class"; Value = cleanClass } ]
        | None ->
            [ { Name = "class"; Value = cleanClass } ]
    | "subclass" ->
        if String.Equals(textContent.Trim(), "North", StringComparison.OrdinalIgnoreCase) then
            [ { Name = "locale"; Value = "North" } ]
        else
            let cleanSubclass = cleanSubclassValue textContent
            [ { Name = "subclass"; Value = cleanSubclass } ]
    | "locale" ->
        let rawLocaleText =
            match textContent with
            | "Devon and Cornwall" -> "Devon, Cornwall"
            | "South and Midlands" -> "South, Midlands"
            | "Westmorland and North" -> "Westmorland, North"
            | "Known nationwide, but especially associated with Devon, Cornwall" -> "Devon, Cornwall"
            | "Westminster, London" -> "Westminster, London"
            | "Stone, Staffordshire" -> "Stone, Staffordshire"
            | other -> other
        
        let transformLocaleName name =
            match name with
            | "Lancashshire" -> "Lancashire"
            | "Borsetshire" -> "Dorsetshire"
            | "Co Durham" -> "Durham"
            | "Northumberland ?" -> "Northumberland"
            | "W Midlands" -> "West Midlands"
            | _ -> name

        rawLocaleText.Split([|','|], StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun s -> s.Trim())
        |> Array.filter (fun s -> not (String.IsNullOrWhiteSpace(s)))
        |> Array.map transformLocaleName
        |> Array.map (fun loc -> { Name = "locale"; Value = loc })
        |> Array.toList
    | _ ->
        [ { Name = tagName; Value = textContent } ]

let collectCustomTagMatches
    (regex: Regex)
    (getTagName: Match -> string)
    (getInnerHtml: Match -> string)
    (content: string)
    : string * (int * TagOutput list) list =
    let mutable result = content
    let mutable positionedTags: (int * TagOutput list) list = []
    let mutable found = true

    while found do
        let matches = regex.Matches(result)

        if matches.Count = 0 then
            found <- false
        else
            for i in (matches.Count - 1) .. -1 .. 0 do
                let m = matches.[i]
                let tagName = getTagName m
                let innerHtml = getInnerHtml m

                positionedTags <- (m.Index, buildCustomTagGroup tagName innerHtml) :: positionedTags
                result <- result.Substring(0, m.Index) + result.Substring(m.Index + m.Length)

    (result, positionedTags)

/// Process custom tags in content: extract values and remove them.
/// Returns the modified content and a list of tag outputs.
let processCustomTags
    (finalName: FinalName)
    (content: string)
    : string * TagOutput list =
    let result, standardTags =
        collectCustomTagMatches
            customTagRegex
            (fun m -> m.Groups.[1].Value.ToLowerInvariant())
            (fun m -> m.Groups.[2].Value)
            content

    let result, orphanedTags =
        collectCustomTagMatches
            orphanedClassRegex
            (fun m -> m.Groups.[2].Value.ToLowerInvariant())
            (fun m -> m.Groups.[1].Value)
            result

    // Sort by original position and flatten
    let tags =
        standardTags @ orphanedTags
        |> List.sortBy fst
        |> List.collect snd

    (result, tags)

let extractHeading (content: string) : string * TagOutput list =
    let headingRegex =
        let headingPattern =
            @"(?:<font[^>]*>\s*)?(?:<big>){2,}<b>(?:<font[^>]*>)?(.*?)(?:</font>)?</b>(?:</big>){2,}\s*(?:<br\s*/?>\s*)*(?:</font>\s*)?(?:<font[^>]*>\s*)?"
        Regex(headingPattern, RegexOptions.IgnoreCase ||| RegexOptions.Singleline)
    let m = headingRegex.Match(content)
    if m.Success then
        let headingText = extractInnerText m.Groups.[1].Value
        let result = content.Substring(0, m.Index) + content.Substring(m.Index + m.Length)
        (result, [ { Name = "heading"; Value = headingText } ])
    else
        (content, [])

let referenceRegex =
    let referencePattern = @"<a\s+href=""references\.htm#([^""]+)"""
    Regex(referencePattern, RegexOptions.IgnoreCase)

let rewriteReferences (content: string) : string * TagOutput list =
    let mutable tags: TagOutput list = []
    let result =
        referenceRegex.Replace(content, fun m ->
            let fragment = m.Groups.[1].Value
            let kebabFragment = Kebab.toKebabCase fragment
            let refTag = { Name = "reference"; Value = kebabFragment }
            if not (tags |> List.contains refTag) then
                tags <- refTag :: tags
            sprintf "<a href=\"references.html#%s\"" kebabFragment)
    (result, tags |> List.rev)

let rewriteReferencesPageNameAttributes (content: string) : string =
    let nameRegex = Regex(@"<a\s+name=""([^""]+)""([^>]*)>", RegexOptions.IgnoreCase)
    nameRegex.Replace(content, fun m ->
        let nameVal = m.Groups.[1].Value
        let rest = m.Groups.[2].Value
        let kebabId = Kebab.toKebabCase nameVal
        if rest.Contains("id=") then
            m.Value
        else
            sprintf "<a name=\"%s\" id=\"%s\"%s>" nameVal kebabId rest)

let removeGoogleSearchForms (content: string) : string =
    let googleSearchFormRegex =
        let googleSearchFormPattern =
            @"<form\s[^>]*action=""https?://www\.google\.com/search""[^>]*>.*?</form>"
        Regex(googleSearchFormPattern, RegexOptions.IgnoreCase ||| RegexOptions.Singleline)
    googleSearchFormRegex.Replace(content, "")

let normalizeBrTags (content: string) : string =
    Regex.Replace(content, @"<BR\s*/?>", "<br>")

let replaceGoogleSearchUrls (content: string) : string * TagOutput list =
    let googleSearchRegex =
        let googleSearchPattern =
            @"https?://www\.google\.co\.uk/search\?[^""]*?q=([^""&]+)[^""]*"
        Regex(googleSearchPattern, RegexOptions.IgnoreCase)
    let mutable tags: TagOutput list = []
    let result =
        googleSearchRegex.Replace(content, fun m ->
            let query = Uri.UnescapeDataString(m.Groups.[1].Value)
            let searchUrl = sprintf "tags/keyword/%s.html" (Kebab.toKebabCase query)
            tags <- { Name = "keyword"; Value = query } :: tags
            searchUrl)
    (result, tags |> List.rev)

let removeTweetLinks (content: string) : string =
    let tweetPattern =
        @"<a\s+href=""https?://twitter\.com/intent/[^""]*""[^>]*><img\s+src=""tw[^""]*""[^>]*></a>"
    let tweetSharePattern =
        @"<a\s+href=""https?://twitter\.com/share""[^>]*>.*?</a>\s*<script>.*?</script>"
    let tweetFollowPattern =
        @"<a\s+href=""https?://twitter\.com/[^""]*""[^>]*class=""twitter-follow-button""[^>]*>.*?</a>\s*<script[^>]*src=""https?://platform\.twitter\.com/widgets\.js""[^>]*></script>"
    let tweetRegex =
        Regex(tweetPattern, RegexOptions.IgnoreCase ||| RegexOptions.Singleline)
    let tweetShareRegex =
        Regex(tweetSharePattern, RegexOptions.IgnoreCase ||| RegexOptions.Singleline)
    let tweetFollowRegex =
        Regex(tweetFollowPattern, RegexOptions.IgnoreCase ||| RegexOptions.Singleline)
    let result = tweetRegex.Replace(content, "")
    let result = tweetShareRegex.Replace(result, "")
    tweetFollowRegex.Replace(result, "")

let removeBuybooksLinks (content: string) : string =
    let buybooksPattern =
        @"<a\s+href=""buybooks\.htm""><img\s+src=""buybooks[^""]*""[^>]*></a>"
    let buybooksRegex =
        Regex(buybooksPattern, RegexOptions.IgnoreCase ||| RegexOptions.Singleline)
    buybooksRegex.Replace(content, "")

let removeNewPageComments (content: string) : string =
    let newPageCommentPattern =
        @"(?:<!--New Page created[^>]*-->\s*)+(?:<br\s*/?>\s*)?"
    let newPageCommentRegex =
        Regex(newPageCommentPattern, RegexOptions.IgnoreCase)
    newPageCommentRegex.Replace(content, "")

let removeLeadingFormerlyArtifact (content: string) : string =
    let formerlyLineRegex =
        Regex(@"(?:^|\r?\n)\s*Formerly:\s*(?:<br\s*/?>\s*)+", RegexOptions.IgnoreCase)
    formerlyLineRegex.Replace(content, Environment.NewLine)

let removeLeadingTagSeparator (content: string) : string =
    let leadingSeparatorRegex =
        Regex(@"^\s*,\s*", RegexOptions.IgnoreCase ||| RegexOptions.Singleline)
    leadingSeparatorRegex.Replace(content, "")

let scrollPngPattern = @"<img\s([^>]*src=""[^""]*scroll\.png[^""]*""[^>]*)>"

let scrollPngRegex =
    Regex(scrollPngPattern, RegexOptions.IgnoreCase)

let injectScrollGuid (fileName: FinalName) (content: string) : string =
    let mutable i = 0
    let (FinalName path) = fileName
    scrollPngRegex.Replace(content, fun m ->
        let scrollId = sprintf "%s-%d" path (i + 1)
        i <- i + 1
        sprintf """<img id="%s" %s>""" scrollId m.Groups.[1].Value)

// let convertFractionsToHtml (content: string) : string =
//     let conv (c: string) (r: string) (content: string) =
//         if content.Contains(c) then
//             content.Replace(c, r)
//         else content
//     content
//     |> conv "½" "&#189;"
//     |> conv "⅓" "&#8531;"
//     |> conv "⅔" "&#8532;"
//     |> conv "¼" "&#188;"
//     |> conv "¾" "&#190;"
//     |> conv "⅛" "&#8539;"
//     |> conv "⅜" "&#8540;"
//     |> conv "⅝" "&#8541;"
//     |> conv "⅞" "&#8542;"

let tdAnyAttributesRegex = Regex(
    @"(<td\s*[^>]*>)", RegexOptions.IgnoreCase)
let trAnyAttributesRegex = Regex(
    @"(<tr\s*[^>]*>)", RegexOptions.IgnoreCase)
let removeTableElementsIfNoTable (content: string) : string =
    if content.Contains("<table") then content
    else
        let minusStartTd =
            if content.Contains("<td") then
                tdAnyAttributesRegex.Replace(content, "")
            else content
        let minusEndTd = minusStartTd.Replace("</td>", "")
        let minusStartTr =
            if minusEndTd.Contains("<tr") then
                trAnyAttributesRegex.Replace(minusEndTd, "")
            else minusEndTd
        let minusEndTr = minusStartTr.Replace("</tr>", "")
        minusEndTr

let outerBodyFontRegex =
    Regex(@"^\s*<font\s+face=""arial""\s+size=""2""\s+color=""black"">", RegexOptions.IgnoreCase)

let leadingSearchHeaderRegex =
    Regex(
        @"^\s*<center>\s*(?:<font[^>]*>\s*(?:<small>.*?</small>\s*)?)?<form\s[^>]*action=""https?://www\.google\.com/search""[^>]*?/?>.*?</form>\s*(?:</font>\s*)?(?:<hr[^>]*>\s*</center>|</center>\s*<hr[^>]*>)\s*",
        RegexOptions.IgnoreCase ||| RegexOptions.Singleline)

let leadingHeadingArtifactsRegex =
    Regex(
        @"^\s*(?:<br\s*/?>\s*)*<font[^>]*>\s*<br\s*/?>\s*<br\s*/?>\s*<font[^>]*>\s*",
        RegexOptions.IgnoreCase ||| RegexOptions.Singleline)

let emptyLeadingFontBlockRegex =
    Regex(@"^\s*<font[^>]*>\s*(?:<br\s*/?>\s*)*</font>\s*", RegexOptions.IgnoreCase ||| RegexOptions.Singleline)

let sitemapFooterRegex =
    Regex(
        @"<font\s+face=""arial""\s+size=""(?:1|2)""\s+color=""#000000"">\s*<br><a\s+href=""sitemap\.htm(?:l)?"">Sitemap</a>.*$",
        RegexOptions.IgnoreCase ||| RegexOptions.Singleline)

let trailingFontClosersRegex =
    Regex(@"(?:</font>\s*)+$", RegexOptions.IgnoreCase ||| RegexOptions.Singleline)

let leadingFontClosersRegex =
    Regex(@"^\s*(?:</font>\s*)+(?:<br\s*/?>\s*)*", RegexOptions.IgnoreCase ||| RegexOptions.Singleline)

let trailingFooterSpacerCenterRegex =
    Regex(@"((?:<br>\s*){4})<center>\s*</center>\s*$", RegexOptions.IgnoreCase ||| RegexOptions.Singleline)

let removePageChrome (content: string) : string =
    let withoutOuterBodyFont = outerBodyFontRegex.Replace(content, "", 1)
    let withoutSearchHeader = leadingSearchHeaderRegex.Replace(withoutOuterBodyFont, "", 1)
    let withoutRepeatedOuterBodyFont = outerBodyFontRegex.Replace(withoutSearchHeader, "", 1)
    sitemapFooterRegex.Replace(withoutRepeatedOuterBodyFont, "")

let removeHeadingArtifacts (content: string) : string =
    let withoutLeadingArtifacts = leadingHeadingArtifactsRegex.Replace(content, "", 1)
    let withoutEmptyLeadingFontBlock = emptyLeadingFontBlockRegex.Replace(withoutLeadingArtifacts, "", 1)
    let withoutLeadingClosers = leadingFontClosersRegex.Replace(withoutEmptyLeadingFontBlock, "")
    trailingFontClosersRegex.Replace(withoutLeadingClosers, "")

let removeTrailingFooterSpacerCenter (content: string) : string =
    trailingFooterSpacerCenterRegex.Replace(content, "$1")

let removeLeadingFontArtifacts (content: string) : string =
    let rec loop (current: string) =
        let withoutEmptyFontBlock = emptyLeadingFontBlockRegex.Replace(current, "", 1)
        let withoutLeadingClosers = leadingFontClosersRegex.Replace(withoutEmptyFontBlock, "")

        if withoutLeadingClosers = current then
            current
        else
            loop withoutLeadingClosers

    loop content

let trimEdgeArtifacts (content: string) : string =
    let breakTokens = [| "<br>"; "<br/>"; "<br />" |]
    let emptyCenterOpen = "<center>"
    let emptyCenterClose = "</center>"

    let startsWithAt (value: string) (startIndex: int) (token: string) =
        startIndex + token.Length <= value.Length
        && String.Compare(value, startIndex, token, 0, token.Length, StringComparison.OrdinalIgnoreCase) = 0

    let endsWithAt (value: string) (endExclusive: int) (token: string) =
        endExclusive >= token.Length
        && String.Compare(value, endExclusive - token.Length, token, 0, token.Length, StringComparison.OrdinalIgnoreCase) = 0

    let rec skipLeadingWhitespace index =
        if index < content.Length && Char.IsWhiteSpace(content[index]) then
            skipLeadingWhitespace (index + 1)
        else
            index

    let rec skipTrailingWhitespace endExclusive =
        if endExclusive > 0 && Char.IsWhiteSpace(content[endExclusive - 1]) then
            skipTrailingWhitespace (endExclusive - 1)
        else
            endExclusive

    let tryConsumeBreakPrefix startIndex =
        breakTokens
        |> Array.tryPick (fun token ->
            if startsWithAt content startIndex token then Some (startIndex + token.Length) else None)

    let tryConsumeBreakSuffix endExclusive =
        breakTokens
        |> Array.tryPick (fun token ->
            if endsWithAt content endExclusive token then Some (endExclusive - token.Length) else None)

    let tryConsumeEmptyCenterPrefix startIndex =
        if startsWithAt content startIndex emptyCenterOpen then
            let afterOpen = skipLeadingWhitespace (startIndex + emptyCenterOpen.Length)

            if startsWithAt content afterOpen emptyCenterClose then
                Some (afterOpen + emptyCenterClose.Length)
            else
                None
        else
            None

    let tryConsumeEmptyCenterSuffix endExclusive =
        if endsWithAt content endExclusive emptyCenterClose then
            let beforeClose = skipTrailingWhitespace (endExclusive - emptyCenterClose.Length)

            if endsWithAt content beforeClose emptyCenterOpen then
                Some (beforeClose - emptyCenterOpen.Length)
            else
                None
        else
            None

    let rec trimLeading startIndex =
        let trimmedStart = skipLeadingWhitespace startIndex

        match tryConsumeEmptyCenterPrefix trimmedStart with
        | Some nextIndex -> trimLeading nextIndex
        | None ->
            match tryConsumeBreakPrefix trimmedStart with
            | Some nextIndex -> trimLeading nextIndex
            | None -> trimmedStart

    let rec trimTrailing endExclusive =
        let trimmedEnd = skipTrailingWhitespace endExclusive

        match tryConsumeEmptyCenterSuffix trimmedEnd with
        | Some nextEnd -> trimTrailing nextEnd
        | None ->
            match tryConsumeBreakSuffix trimmedEnd with
            | Some nextEnd -> trimTrailing nextEnd
            | None -> trimmedEnd

    let startIndex = trimLeading 0
    let endIndex = trimTrailing content.Length

    if startIndex >= endIndex then
        ""
    else
        content.Substring(startIndex, endIndex - startIndex)

let applyContentTransforms (transforms: (string -> string) list) (content: string) : string =
    (content, transforms) ||> List.fold (fun current transform -> transform current)

// --- Combined processing ---

let processContent (finalName: FinalName) (content: string) : string * TagOutput list =
    let mutable result = content
    let (FinalName nameStr) = finalName
    if String.Equals(nameStr, "references.html", StringComparison.OrdinalIgnoreCase) then
        result <- rewriteReferencesPageNameAttributes result

    let mutable allTags: TagOutput list = []

    let applyTagTransform transform =
        let updatedContent, tags = transform result
        result <- updatedContent
        allTags <- allTags @ tags
    
    result <-
        applyContentTransforms
            [ removeTableElementsIfNoTable
              removePageChrome
            //   convertFractionsToHtml
              removeGoogleSearchForms ]
            result

    // Process custom tags (class/subclass/locale) - removes them and extracts tags
    applyTagTransform (processCustomTags finalName)
    result <- removeLeadingTagSeparator result

    // Remove new page comments (after custom tags so trailing <br>\n can be consumed)
    result <- removeNewPageComments result
    result <- removeLeadingFormerlyArtifact result

    // Extract heading
    applyTagTransform extractHeading
    result <- removeHeadingArtifacts result

    // Rewrite reference links and extract reference tags
    let (afterRefs, refTags) = rewriteReferences result
    result <- afterRefs

    // Replace Google search URLs
    applyTagTransform replaceGoogleSearchUrls

    // Remove tweet-this links
    let cleanupTransforms = [ removeTweetLinks; removeBuybooksLinks; normalizeBrTags; removeTrailingFooterSpacerCenter ]

    result <- applyContentTransforms cleanupTransforms result
    result <- removeLeadingFontArtifacts result
    result <- trimEdgeArtifacts result
    result <- removeLeadingTagSeparator result
    result <- trimEdgeArtifacts result

    // Inject scroll.png ids and count receipts
    let scrollMatches = scrollPngRegex.Matches(result)
    let receiptCount = scrollMatches.Count
    result <- injectScrollGuid finalName result

    allTags <- allTags @ [ { Name = "number-of-receipts"; Value = string receiptCount } ]

    // Add reference tags at the end
    allTags <- allTags @ refTags

    (result, allTags)

// --- URL rewriting ---

let rewriteUrls (content: string) (urlMap: Map<RawUrl, ReplacementUrl>) : string =
    let doc = HtmlAgilityPack.HtmlDocument()
    doc.LoadHtml(content)

    let rewriteAttr (tagName: string) (attrName: string) =
        let nodes =
            doc.DocumentNode.SelectNodes(sprintf "//%s[@%s]" tagName attrName)

        match nodes with
        | null -> ()
        | _ ->
            for node in nodes do
                let url = node.GetAttributeValue(attrName, "")

                if not (String.IsNullOrWhiteSpace(url)) then
                    // Try to match this URL to a known file's final name
                    match urlMap |> Map.tryFind (RawUrl url) with
                    | Some finalName ->
                        let replacement =
                            match finalName with
                            | FoundFile (FinalName fn) -> fn
                            | NotFound (RawUrl original) ->
                                //Logging.logWarning "URL Rewrite" url (sprintf "No match found for URL: %s" original)
                                original // Leave unchanged if not found

                        node.SetAttributeValue(attrName, replacement)
                        |> ignore
                    | None -> ()

    rewriteAttr "a" "href"
    rewriteAttr "img" "src"
    rewriteAttr "link" "href"
    rewriteAttr "script" "src"
    doc.DocumentNode.OuterHtml

/// Build a map from original URL paths to final names.
let buildUrlMap (ctx: PipelineDbContext) : Map<RawUrl, ReplacementUrl> =
    // Pre-load all final names into a lookup by RelativePath for O(1) access
    let finalNameLookup =
        ctx.FinalNames.ToArray()
        |> Array.map entityToFinalName
        |> Array.map (fun fn -> let (RelativePath rp) = fn.OriginalPath in (rp, fn))
        |> Map.ofArray

    ctx.ParsedUrls.ToArray()
    |> Array.map entityToParsedUrl
    |> Array.map (fun pu ->
        if pu.FileExists = Some(Exists) then
            let renamedUrl = pu.RenamedUrl |> Option.defaultValue (Guid.NewGuid().ToString())
            let serverPath = pu.ServerPath |> Option.defaultValue (Guid.NewGuid().ToString())
            let finalName =
                finalNameLookup
                |> Map.tryFind serverPath
                |> Option.orElse (finalNameLookup |> Map.tryFind renamedUrl)

            match finalName with
            | Some fn -> (pu.RawUrl, FoundFile fn.Name)
            | None -> (pu.RawUrl, NotFound pu.RawUrl)
        else
            (pu.RawUrl, NotFound pu.RawUrl)
    )
    |> Map.ofArray

type Step4Result =
    {
        Content: string
        CustomTags: Step4PreTidyMetadataEntity list
        OutRelPath: FinalName
        OriginalContent: string
    }

let tagOutputToEntity (finalName: FinalName) (tag: TagOutput) : Step4PreTidyMetadataEntity =
    let (FinalName filePath) = finalName
    { Id = 0
      RelativePath = filePath
      TagName = tag.Name
      TextContent = tag.Value }

/// Process a single HTML file: custom tags, scroll ids, URL rewrite, write output.
/// Returns custom tag entities for sequential DB write.
let processHtmlFile
    (fn: FinalNameEntry)
    (pageContentMap: Map<RelativePath, PageContentRecord>)
    (urlMap: Map<RawUrl, ReplacementUrl>)
    : Step4Result option =
    
    match pageContentMap |> Map.tryFind fn.OriginalPath with
    | None ->
        let (RelativePath filePath) = fn.OriginalPath
        Logging.logWarning "Step4" filePath "No page content found"
        None
    | Some pageContent when isNull pageContent.Content ->
        let (RelativePath filePath) = fn.OriginalPath
        Logging.logWarning "Step4" filePath "No page content found"
        None
    | Some pageContent ->
        try
            // Run the combined content processing
            let (processedContent, tags) = processContent fn.Name pageContent.Content
            let mutable content = processedContent

            // Rewrite URLs to final names
            content <- rewriteUrls content urlMap

            let entities = tags |> List.map (tagOutputToEntity fn.Name)
            Some { CustomTags = entities; Content = content; OutRelPath = fn.Name; OriginalContent = pageContent.Content }
        with ex ->
            let (RelativePath filePath) = fn.OriginalPath
            Logging.logError "Step4" filePath (sprintf "Failed to render: %s" ex.Message)
            None

let removeExistingStepData (ctx: PipelineDbContext) workingDir =
    if ctx.PreTidyMetadata.Any() then
        Logging.logInfo "Step4" "Clearing existing CustomTagValues..."
        ctx.PreTidyMetadata.RemoveRange(ctx.PreTidyMetadata) |> ignore
    else
        Logging.logInfo "Step4" "No existing CustomTagValues to clear."
    
    ctx.SaveChanges() |> ignore

    if Directory.Exists(workingDir) then
        // Clean existing files in the working directory
        printfn "Cleaning existing files in working directory: %s" workingDir
        let existingFiles = Directory.GetFiles(workingDir, "*", SearchOption.AllDirectories)
        for file in existingFiles do
            File.Delete(file)
    else
        Directory.CreateDirectory(workingDir) |> ignore

let run (workingDir: string) (ctx: PipelineDbContext) =
    let stopwatch = Diagnostics.Stopwatch.StartNew()
    Logging.logInfo "Step4" "Rendering pre-tidy HTML..."
    
    removeExistingStepData ctx workingDir

    printfn "Building URL map for %d parsed URLs..." (ctx.ParsedUrls.Count())
    let urlMap = buildUrlMap ctx

    printfn "Pre-loading page contents for %d pages..." (ctx.RawPageContents.Count())
    // Pre-load page contents into a lookup map for thread-safe parallel access
    let pageContentMap =
        ctx.RawPageContents.ToArray()
        |> Array.map (fun pc -> (RelativePath pc.RelativePath, entityToPageContent pc))
        |> Map.ofArray

    printfn "Finding HTML files to process..."
    let htmlFinalNames =
        ctx.FinalNames.Where(fun f -> f.FileType = "html").ToArray()
        |> Array.map entityToFinalName

    Logging.logInfo "Step4" (sprintf "Rendering %d HTML files" htmlFinalNames.Length)

    ChannelProcessing.runParallel
        htmlFinalNames
        (fun fn -> processHtmlFile fn pageContentMap urlMap)
        (fun result ->
            match result with
            | None -> 
                printfn "No result for file, skipping write."
                ()
            | Some r ->
                let path = match r.OutRelPath with | FinalName p -> p
                let outputPath = Path.Combine(workingDir, path)
                File.WriteAllText(outputPath, r.Content)

                let originalPath = path + ".original.htm"
                File.WriteAllText(Path.Combine(workingDir, originalPath), r.OriginalContent)
                
                for tag in r.CustomTags do
                    ctx.PreTidyMetadata.Add(tag) |> ignore)
        Environment.ProcessorCount

    ctx.SaveChanges() |> ignore

    let tagCount = ctx.PreTidyMetadata.Count()
    Logging.logInfo "Step4" (sprintf "Extracted %d custom tag values" tagCount)

    stopwatch.Stop()
    Logging.logStepSummary "Step4" htmlFinalNames.Length stopwatch.Elapsed
