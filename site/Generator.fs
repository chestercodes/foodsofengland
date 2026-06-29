module SiteGenerator.Generator

open System
open System.IO
open SiteGenerator.Data
open SiteGenerator.Paths
open SiteGenerator.Rendering
open SiteGenerator.Types

let private writePage (config: RunConfig) (sitePath: string) (html: string) =
    sitePathToOutputPath config sitePath |> fun outputPath -> writeAllText outputPath html

let generateSite (config: RunConfig) =
    if not (Directory.Exists(config.SiteDataDir)) then
        invalidOp (sprintf "Could not find site-data directory at %s" config.SiteDataDir)

    if not (Directory.Exists(config.DownloaderOutput)) then
        Directory.CreateDirectory(config.DownloaderOutput) |> ignore

    if Directory.Exists(config.OutputDir) then
        Directory.Delete(config.OutputDir, true)

    Directory.CreateDirectory(config.OutputDir) |> ignore

    let posts, assets = loadSiteData config
    let tagPages = renderTagPages config posts

    copyAssets config assets
    writeAllText (sitePathToOutputPath config "assets/site.css") siteCss
    writeAllText (sitePathToOutputPath config "assets/search.js") searchJs
    writeAllText (sitePathToOutputPath config "assets/ingredients.js") ingredientsJs
    writeAllText (sitePathToOutputPath config "assets/categories.js") categoriesJs

    // Copy background images
    let srcBgDir = Path.Combine(repoRoot, "site", "Pages", "backgrounds")
    let destBgDir = sitePathToOutputPath config "assets/backgrounds"
    if Directory.Exists(srcBgDir) then
        if not (Directory.Exists(destBgDir)) then
            Directory.CreateDirectory(destBgDir) |> ignore
        for file in Directory.GetFiles(srcBgDir) do
            let fileName = Path.GetFileName(file)
            File.Copy(file, Path.Combine(destBgDir, fileName), true)
    writePage config "index.html" (renderIndexPage config posts)
    writePage config "about.html" (renderAboutPage config)
    writePage config "ingredients.html" (renderIngredientPage config posts)
    writePage config "categories.html" (renderCategoriesPage config posts)
    writePage config "404.html" (render404Page config)

    let exceptions = Set.ofList [ "index.html"; "about.html"; "ingredients.html"; "categories.html"; "404.html" ]

    for post in posts do
        if not (exceptions.Contains(post.FinalPath)) then
            writePage config post.FinalPath (renderPostPage config post)

    for (pagePath, html) in tagPages do
        writePage config pagePath html

    printfn "Generated %d post pages, %d tag pages, and copied %d assets to %s" posts.Length tagPages.Length assets.Length config.OutputDir