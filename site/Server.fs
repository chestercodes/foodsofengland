module Server

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.DependencyInjection.Extensions

module Config =
    type SiteConfig = {
        BaseUrl: Uri
    }

    let getConfig () : SiteConfig =
        {
            BaseUrl = Uri("https://foodsofengland.uk")
        }

open Config

module Data =
    type Post = {
        Id: string
        Title: string
        Content: string
        DatePublished: DateTime
    }

    let getAllPosts () : Post list =
        try
            let posts, _ = SiteGenerator.Data.loadSiteData SiteGenerator.Paths.defaultConfig
            posts
            |> Array.map (fun post ->
                { Id = post.FinalPath.Replace(".html", "")
                  Title = post.Title
                  Content = post.ContentHtml
                  DatePublished = DateTime(2024, 1, 1) })
            |> Array.toList
        with ex ->
            eprintfn "Failed to load posts from database: %s" ex.Message
            []
    
    type SiteData = {
        Posts: Post list
    }

open Data

module Sitemap =
    let generateSitemap (baseUrl: Uri) (siteData: SiteData) : string =
        let posts =
            siteData.Posts
            |> List.filter (fun post -> not (String.Equals(post.Id, "donate", StringComparison.OrdinalIgnoreCase)))
            |> List.map (fun post ->
                    sprintf "<sitemap><loc>%s/%s.html</loc></sitemap>"
                        (baseUrl.ToString().TrimEnd('/'))
                        post.Id
                ) |> String.concat "\n"

        sprintf "<sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"> %s </sitemapindex>" posts

module Api =
    open OldSiteParser.Database

    let xml (thing: string): IResult =
        Results.Text(thing, "application/xml")
    
    let sitemap: SiteData -> SiteConfig -> IResult =
        fun (siteData: SiteData) (siteConfig: SiteConfig) ->
            Sitemap.generateSitemap siteConfig.BaseUrl siteData |> xml

    let getSearchResults (siteData: SiteData) (query: string) : {| title: string; finalPath: string |} array =
        if String.IsNullOrWhiteSpace(query) || query.Length < 2 then
            [||]
        else
            let term = query.Trim()
            siteData.Posts
            |> List.filter (fun p ->
                (p.Title <> null && p.Title.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (p.Content <> null && p.Content.Contains(term, StringComparison.OrdinalIgnoreCase))
            )
            |> List.map (fun p -> {| title = p.Title; finalPath = p.Id + ".html" |})
            |> List.truncate 15
            |> List.toArray

[<EntryPoint>]
let main args =
    let hasGenerateSite = args |> Array.contains "--generate-site"
    if hasGenerateSite then
        try
            printfn "Generating static site..."
            SiteGenerator.Generator.generateSite SiteGenerator.Paths.defaultConfig
            printfn "Static site generation complete."
            0
        with ex ->
            eprintfn "Failed to generate static site: %s" ex.Message
            1
    else
        let builder = WebApplication.CreateBuilder(args)

        let siteData = {
            Posts = getAllPosts()
            }
        
        let siteConfig: SiteConfig = getConfig()
        
        builder.Services.TryAddSingleton<SiteData>(siteData) |> ignore
        builder.Services.TryAddSingleton<SiteConfig>(siteConfig) |> ignore
        builder.Services.AddResponseCompression() |> ignore
        
        let app = builder.Build()

        app.UseResponseCompression() |> ignore

        app.Use(fun (context: HttpContext) (next: Func<System.Threading.Tasks.Task>) ->
            let path = context.Request.Path.Value
            if not (String.IsNullOrEmpty(path)) then
                // Rewrite extensionless URL to .html if the file exists in WebRoot
                if not (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) &&
                   not (path.Equals("/random", StringComparison.OrdinalIgnoreCase)) &&
                   not (path.Contains(".")) then
                    let webRoot = if String.IsNullOrEmpty(app.Environment.WebRootPath) then "wwwroot" else app.Environment.WebRootPath
                    let relativePart = path.TrimStart('/').Replace('/', System.IO.Path.DirectorySeparatorChar) + ".html"
                    let diskPath = System.IO.Path.Combine(webRoot, relativePart)
                    if System.IO.File.Exists(diskPath) then
                        context.Request.Path <- PathString(path + ".html")

                let finalPath = context.Request.Path.Value
                if not (finalPath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) &&
                   not (finalPath.StartsWith("/random", StringComparison.OrdinalIgnoreCase)) then
                    // Cache-Control: Instructs Cloudflare CDN (via s-maxage) to cache the static file resources for 7 days,
                    // and tells browser clients (via max-age) to store them locally for 24 hours.
                    context.Response.Headers.["Cache-Control"] <- Microsoft.Extensions.Primitives.StringValues("public, max-age=86400, s-maxage=604800")
                else
                    // Cache-Control / Pragma / Expires: Tells proxy CDNs and browsers to completely bypass caching for the Search API
                    // and dynamic random-redirect handlers to guarantee fresh real-time results.
                    context.Response.Headers.["Cache-Control"] <- Microsoft.Extensions.Primitives.StringValues("no-cache, no-store, must-revalidate")
                    context.Response.Headers.["Pragma"] <- Microsoft.Extensions.Primitives.StringValues("no-cache")
                    context.Response.Headers.["Expires"] <- Microsoft.Extensions.Primitives.StringValues("0")
                
                // X-Content-Type-Options: nosniff - Prevents browsers from guessing/sniffing content types outside of declared headers.
                context.Response.Headers.["X-Content-Type-Options"] <- Microsoft.Extensions.Primitives.StringValues("nosniff")
                
                // X-Frame-Options: SAMEORIGIN - Mitigates clickjacking vulnerabilities by preventing framing from external sites.
                context.Response.Headers.["X-Frame-Options"] <- Microsoft.Extensions.Primitives.StringValues("SAMEORIGIN")
                
                // Referrer-Policy: no-referrer-when-downgrade - Avoids sending referrer context when navigating back to HTTP protocols.
                context.Response.Headers.["Referrer-Policy"] <- Microsoft.Extensions.Primitives.StringValues("no-referrer-when-downgrade")
            next.Invoke()
        ) |> ignore

        // Serve static files from wwwroot
        app.UseStatusCodePagesWithReExecute("/404.html") |> ignore
        app.UseDefaultFiles() |> ignore
        app.UseStaticFiles() |> ignore

        app.MapGet("/sitemap.xml", Func<SiteData, SiteConfig, IResult>(Api.sitemap)) |> ignore
        
        app.MapGet("/random", Func<SiteData, IResult>(fun (siteData: SiteData) ->
            if List.isEmpty siteData.Posts then
                Results.NotFound("No posts found.")
            else
                let rand = Random()
                let index = rand.Next(siteData.Posts.Length)
                let post = siteData.Posts.[index]
                Results.Redirect(sprintf "/%s.html" post.Id)
        )) |> ignore

        app.MapGet("/api/search", Func<string, SiteData, IResult>(fun (q: string) (siteData: SiteData) ->
            Api.getSearchResults siteData q |> Results.Ok
        )) |> ignore
        
        app.MapGet("/api/live", Func<IResult>(fun () ->
            "good" |> Results.Ok
        )) |> ignore

        app.Run()

        0 // Exit code
