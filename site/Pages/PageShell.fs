module SiteGenerator.Pages.PageShell

open Feliz.ViewEngine
open SiteGenerator.Paths
open SiteGenerator.Types

let renderPage (config: RunConfig) (currentSitePath: string) (pageTitle: string) (metaDescription: string) (content: ReactElement list) (scripts: string list) =
    let navLink (title: string) (target: string) =
        Html.a [
            prop.className "site-nav__link"
            prop.href (relativeHref currentSitePath target)
            prop.text title
        ]

    let scriptNodes =
        ("assets/search.js" :: scripts)
        |> List.map (fun scriptPath ->
            let url = relativeHref currentSitePath scriptPath
            Html.script [ prop.src url ]
        )

    let canonicalUrl = sprintf "https://foodsofengland.uk/%s" (if currentSitePath = "index.html" then "" else currentSitePath)

    Render.htmlDocument(
        Html.html [
            Html.head [
                Html.meta [ prop.custom("charset", "utf-8") ]
                Html.meta [
                    prop.name "viewport"
                    prop.content "width=device-width, initial-scale=1"
                ]
                Html.title [ prop.text pageTitle ]
                Html.meta [
                    prop.name "description"
                    prop.content metaDescription
                ]
                Html.meta [ prop.custom("property", "og:title"); prop.content pageTitle ]
                Html.meta [ prop.custom("property", "og:description"); prop.content metaDescription ]
                Html.meta [ prop.custom("property", "og:type"); prop.content "website" ]
                Html.link [
                    prop.rel "canonical"
                    prop.href canonicalUrl
                ]
                Html.link [
                    prop.rel "stylesheet"
                    prop.href (relativeHref currentSitePath "assets/site.css")
                ]
            ]
            Html.body [
                Html.header [
                    prop.className "site-header"
                    prop.children [
                        Html.div [
                            prop.className "site-header__inner"
                            prop.children [
                                Html.a [
                                    prop.className "site-brand"
                                    prop.href (relativeHref currentSitePath "index.html")
                                    prop.text "Foods of England"
                                ]
                                Html.div [
                                    prop.className "search-widget"
                                    prop.children [
                                        Html.input [
                                            prop.type' "text"
                                            prop.id "search-input"
                                            prop.placeholder "Search foods..."
                                            prop.className "search-widget__input"
                                        ]
                                        Html.ul [
                                            prop.id "search-results"
                                            prop.className "search-widget__results"
                                        ]
                                    ]
                                ]
                                Html.nav [
                                    prop.className "site-nav"
                                    prop.children [
                                        navLink "About" "about.html"
                                        navLink "Categories" "categories.html"
                                        navLink "Ingredients" "ingredients.html"
                                        Html.a [
                                            prop.className "site-nav__link"
                                            prop.href "/random"
                                            prop.text "Random Page"
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
                Html.main [
                    prop.className "page-shell"
                    prop.children content
                ]
                yield! scriptNodes
            ]
        ])
