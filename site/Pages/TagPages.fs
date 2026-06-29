module SiteGenerator.Pages.TagPages

open Feliz.ViewEngine
open SiteGenerator.Paths
open SiteGenerator.Types
open SiteGenerator.Pages.PageShell
open SiteGenerator.Pages.Helpers

let renderTagPages (config: RunConfig) (posts: SitePost array) =
    posts
    |> Array.collect (fun post -> post.Tags |> List.map (fun tag -> tag, post) |> List.toArray)
    |> Array.groupBy (fun (tag, _) -> tag.PagePath)
    |> Array.map (fun (_, pairs) ->
        let tag = fst pairs[0]

        let postsForTag =
            pairs
            |> Array.map snd
            |> Array.distinctBy (fun (post: SitePost) -> post.FinalPath)
            |> Array.sortBy (fun (post: SitePost) -> post.Title.ToLowerInvariant())

        tag.PagePath,
        renderPage config tag.PagePath tag.Label (sprintf "Browse all traditional English dishes tagged under %s." tag.Label) [
            Html.section [
                prop.className "hero"
                prop.children [
                    Html.h1 [ prop.className "hero__title"; prop.text tag.Label ]
                    Html.p [ prop.className "hero__lead"; prop.text (sprintf "%d posts in this collection" postsForTag.Length) ]
                ]
            ]
            Html.section [
                prop.className "content-card"
                prop.children [
                    Html.ul [
                        prop.className "collection-list"
                        prop.children [
                            for post in postsForTag do
                                Html.li [
                                    prop.className "collection-item"
                                    prop.children [
                                        Html.a [
                                            prop.href (relativeHref tag.PagePath (post: SitePost).FinalPath)
                                            prop.text ((post: SitePost).Title)
                                        ]
                                        Html.p [
                                            prop.className "collection-item__meta"
                                            prop.text (getFirstLine (post: SitePost).ContentHtml)
                                        ]
                                    ]
                                ]
                        ]
                    ]
                ]
            ]
        ] [])
