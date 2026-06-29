module SiteGenerator.Data

open System
open System.IO
open System.Linq
open System.Text.Json
open OldSiteParser.Database
open SiteGenerator.Paths
open SiteGenerator.Types

type JsonTag =
    { TagName: string
      TagValue: string }

type JsonRelatedPage =
    { RelationDescription: string
      RelatedPageText: string
      RelatedPageUrl: string }

type JsonPost =
    { FinalPath: string
      Title: string
      EarliestSnapshot: string
      UsedSnapshot: string
      ReceiptCount: int
      Heading: string
      Synonym: string
      Tags: JsonTag[]
      RelatedPages: JsonRelatedPage[]
      Ingredients: string[] }

let loadSiteData (config: RunConfig) =
    if not (Directory.Exists(config.SiteDataDir)) then
        invalidOp (sprintf "Could not find site-data directory at %s" config.SiteDataDir)

    let jsonOptions = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    let jsonFiles =
        Directory.GetFiles(config.SiteDataDir, "*.json", SearchOption.AllDirectories)
        |> Array.filter (fun p -> not (Path.GetFileName(p).Equals("assets.json", StringComparison.OrdinalIgnoreCase)))

    let posts =
        jsonFiles
        |> Array.map (fun jsonFile ->
            let jsonStr = File.ReadAllText(jsonFile)
            let jsonPost = JsonSerializer.Deserialize<JsonPost>(jsonStr, jsonOptions)
            
            let finalPath = normalizeSitePath jsonPost.FinalPath
            let heading = optionOfText jsonPost.Heading |> Option.map OldSiteParser.Shared.Kebab.stripTitlePrefix
            let synonym = optionOfText jsonPost.Synonym
            let rawTitle = optionOfText jsonPost.Title |> Option.map OldSiteParser.Shared.Kebab.stripTitlePrefix
            let title =
                [ rawTitle; heading; synonym; Some(titleFromPath finalPath) ]
                |> List.choose id
                |> List.head

            let htmlPath = Path.Combine(config.SiteDataDir, jsonPost.FinalPath)
            let contentHtml =
                if File.Exists(htmlPath) then
                    File.ReadAllText(htmlPath, System.Text.Encoding.UTF8)
                else
                    ""

            let tags =
                if isNull jsonPost.Tags then []
                else
                    jsonPost.Tags
                    |> Array.choose (fun tag ->
                        match optionOfText tag.TagName, optionOfText tag.TagValue with
                        | Some rawTagName, Some tagValue ->
                            if String.Equals(rawTagName, "list-page-reference", StringComparison.OrdinalIgnoreCase) ||
                               String.Equals(rawTagName, "number-of-receipts", StringComparison.OrdinalIgnoreCase) then
                                None
                            else
                                let tagName =
                                    if String.Equals(rawTagName, "subclass", StringComparison.OrdinalIgnoreCase) then "status"
                                    else rawTagName
                                let label =
                                    if String.Equals(tagName, "class", StringComparison.OrdinalIgnoreCase) then tagValue
                                    else sprintf "%s: %s" tagName tagValue

                                Some
                                    { TagName = tagName
                                      TagValue = tagValue
                                      Label = label
                                      PagePath = sprintf "tags/%s/%s.html" (slugify tagName) (slugify tagValue) }
                        | _ -> None)
                    |> Array.distinctBy (fun tag -> tag.TagName.ToLowerInvariant(), tag.TagValue.ToLowerInvariant())
                    |> Array.sortBy (fun tag -> tag.Label.ToLowerInvariant())
                    |> Array.toList

            let relatedPages =
                if isNull jsonPost.RelatedPages then []
                else
                    jsonPost.RelatedPages
                    |> Array.choose (fun rp ->
                        match optionOfText rp.RelatedPageText, optionOfText rp.RelatedPageUrl with
                        | Some text, Some url ->
                            Some
                                { Description = optionOfText rp.RelationDescription
                                  Text = text
                                  Url = url }
                        | _ -> None)
                    |> Array.distinctBy (fun link -> link.Text.ToLowerInvariant(), link.Url.ToLowerInvariant())
                    |> Array.toList

            let ingredients =
                if isNull jsonPost.Ingredients then []
                else
                    jsonPost.Ingredients
                    |> Array.choose optionOfText
                    |> Array.distinctBy (fun ingredient -> ingredient.ToLowerInvariant())
                    |> Array.sortBy (fun ingredient -> ingredient.ToLowerInvariant())
                    |> Array.toList

            { FinalPath = finalPath
              Title = title
              Heading = heading
              Synonym = synonym
              ContentHtml = contentHtml
              Tags = tags
              RelatedPages = relatedPages
              Ingredients = ingredients
              ReceiptCount = jsonPost.ReceiptCount
              EarliestSnapshot = optionOfText jsonPost.EarliestSnapshot
              UsedSnapshot = optionOfText jsonPost.UsedSnapshot })
        |> Array.sortBy (fun post -> post.Title.ToLowerInvariant())

    let assetsJsonPath = Path.Combine(config.SiteDataDir, "assets.json")
    let assets =
        if File.Exists(assetsJsonPath) then
            let assetsJsonStr = File.ReadAllText(assetsJsonPath)
            let jsonAssets = JsonSerializer.Deserialize<SiteAsset[]>(assetsJsonStr, jsonOptions)
            jsonAssets
            |> Array.choose (fun asset ->
                match optionOfText asset.InitialPath with
                | None -> None
                | Some initialPath ->
                    Some
                        { SiteAsset.InitialPath = normalizeSitePath initialPath
                          FinalPath = optionOfText asset.FinalPath |> Option.map normalizeSitePath |> Option.defaultValue (normalizeSitePath initialPath) })
        else
            [||]

    posts, assets

let copyAssets (config: RunConfig) (assets: SiteAsset array) =
    for asset in assets do
        let sourcePath = Path.Combine(config.DownloaderOutput, normalizeDiskPath asset.InitialPath)
        let targetPath = sitePathToOutputPath config asset.FinalPath

        if File.Exists(sourcePath) then
            ensureParentDirectory targetPath
            File.Copy(sourcePath, targetPath, true)
        else
            printfn "Missing asset source: %s" sourcePath