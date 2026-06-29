module SiteGenerator.Pages.PostPage

open System
open Feliz.ViewEngine
open SiteGenerator.Paths
open SiteGenerator.Types
open SiteGenerator.Pages.PageShell
open SiteGenerator.Pages.Helpers

let renderPostPage (config: RunConfig) (post: SitePost) =
    let metaDesc =
        let rawDesc = getFirstLine post.ContentHtml
        if String.IsNullOrWhiteSpace(rawDesc) then
            sprintf "Traditional English recipe and culinary history for %s." post.Title
        else
            rawDesc
    renderPage config post.FinalPath post.Title metaDesc [
        Html.article [
            prop.className "post-layout"
            prop.children [
                Html.header [
                    prop.className "post-header"
                    prop.children [
                        Html.h1 [
                            prop.className "post-title"
                            prop.text post.Title
                        ]
                        match post.Heading with
                        | Some heading when not (String.Equals(heading, post.Title, StringComparison.OrdinalIgnoreCase)) ->
                            Html.p [
                                prop.className "hero__lead"
                                prop.text heading
                            ]
                        | _ -> Html.div []
                        if post.Tags.Length > 0 && not (String.Equals(post.FinalPath, "references.html", StringComparison.OrdinalIgnoreCase)) then
                            Html.div [
                                prop.className "post-tags"
                                prop.children [
                                    for tag in post.Tags do
                                        Html.a [
                                            prop.className "tag-pill"
                                            prop.href (relativeHref post.FinalPath tag.PagePath)
                                            prop.text tag.Label
                                        ]
                                ]
                            ]
                    ]
                ]
                if String.IsNullOrWhiteSpace(post.ContentHtml) then
                    Html.p [
                        prop.className "meta-line"
                        prop.text "No extracted content was available for this post."
                    ]
                else
                    Html.div [
                        prop.className "post-content"
                        prop.children [ rawHtml (replaceOriginalLinks post.FinalPath post.ContentHtml) ]
                    ]
                Html.footer [
                    prop.className "post-footer"
                    prop.children [
                        if post.RelatedPages.Length > 0 then
                            Html.section [
                                Html.h2 [ prop.text "Related pages" ]
                                Html.div [
                                    prop.className "post-tags"
                                    prop.children [
                                        for related in post.RelatedPages do
                                            let isEmail = related.Url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) && related.Url.Contains("@foodsofengland.co.uk")
                                            let href = if isEmail then "https://github.com/chestercodes/foodsofengland/issues" else relativeHref post.FinalPath related.Url
                                            let text = if isEmail then "raise an issue on GitHub" else stripHtmlTags related.Text
                                            Html.a [
                                                prop.className "tag-pill"
                                                prop.href href
                                                prop.text text
                                            ]
                                    ]
                                ]
                            ]
                        Html.section [
                            prop.custom ("style", "margin-top: 2.5rem; border-top: 1px solid var(--line); padding-top: 1.5rem;")
                            prop.children [
                                Html.p [
                                    prop.custom ("style", "font-style: italic; color: var(--muted); font-size: 0.95rem;")
                                    prop.children [
                                        Html.text "If you have any corrections, suggestions, or additional information for this page, please "
                                        Html.a [
                                            let issueTitle = Uri.EscapeDataString(post.FinalPath)
                                            prop.href (sprintf "https://github.com/chestercodes/foodsofengland/issues/new?title=%s" issueTitle)
                                            prop.text "file an issue on GitHub"
                                        ]
                                        Html.text "."
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ] []
