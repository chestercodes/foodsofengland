module OldSiteParser.Shared.Kebab

open System
open System.Text.RegularExpressions
open OldSiteParser.Domain

/// Known common parts to strip from image file names.
/// Edit this list to add/remove common prefixes or substrings.
let commonPartsToStrip = [ "foodsofengland"; "500-"; "300-" ]

/// Convert a string to kebab-case:
/// - Strip file extension
/// - Replace spaces, underscores, and non-alphanumeric (except hyphens) with hyphens
/// - Collapse multiple hyphens
/// - Trim leading/trailing hyphens
/// - Lowercase
let toKebabCase (input: string) : string =
    let name =
        if String.IsNullOrWhiteSpace(input) then
            "unnamed"
        else
            input

    name
        .ToLowerInvariant()
    |> fun s -> Uri.UnescapeDataString(s)
    |> fun s -> Regex.Replace(s, @"[^a-z0-9\-]", "-")
    |> fun s -> Regex.Replace(s, @"-{2,}", "-")
    |> fun s -> s.Trim('-')

/// Strip the file extension from a file name.
let stripExtension (fileName: string) : string =
    let idx = fileName.LastIndexOf('.')

    if idx > 0 then
        fileName.Substring(0, idx)
    else
        fileName

/// Strip known common parts from a name.
/// Returns the stripped name, or the original if stripping would make it empty.
let stripCommonParts (name: string) : string =
    let mutable result = name

    for part in commonPartsToStrip do
        result <- result.Replace(part, "")

    // Strip any <number>px- patterns (e.g. 800px-, 1024px-, 348px-)
    result <- Regex.Replace(result, @"\d+px-", "")

    // Clean up any resulting double-hyphens or leading/trailing hyphens
    let cleaned =
        result
        |> fun s -> Regex.Replace(s, @"-{2,}", "-")
        |> fun s -> s.Trim('-')

    if String.IsNullOrWhiteSpace(cleaned) then
        name
    else
        cleaned

/// Generate a kebab-case name from a file name (without extension).
let kebabifyFileName (RelativePath fileName) : string =
    fileName |> stripExtension |> toKebabCase

/// Strip known site title prefixes from an HTML page title.
let stripTitlePrefix (title: string) : string =
    if String.IsNullOrWhiteSpace(title) then title
    else
        let prefixes =
            [ "The Foods of England - "
              "Foods of England - "
              "The Foods of England "
              "Foods of England " ]

        let mutable result = title.Trim()
        let mutable matched = true
        while matched do
            matched <- false
            for prefix in prefixes do
                if not matched && result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) then
                    let stripped = result.Substring(prefix.Length).TrimStart([| '-'; ' '; ':'; ',' |]).Trim()
                    if stripped.Length > 0 then
                        result <- stripped
                        matched <- true
        result

/// Given a list of generated names, detect duplicates and return a set of names
/// that appear more than once.
let findDuplicateNames (names: string seq) : Set<string> =
    names
    |> Seq.countBy id
    |> Seq.filter (fun (_, count) -> count > 1)
    |> Seq.map fst
    |> Set.ofSeq
