module SiteGenerator.Pages.NotFoundPage

open Feliz.ViewEngine
open SiteGenerator.Paths
open SiteGenerator.Types
open SiteGenerator.Pages.PageShell

let render404Page (config: RunConfig) =
    renderPage config "404.html" "Page Not Found" "The requested page was not found on the Foods of England tribute website." [
        Html.section [
            prop.className "hero"
            prop.children [
                Html.h1 [ prop.className "hero__title"; prop.text "Page Not Found" ]
                Html.p [
                    prop.className "hero__lead"
                    prop.text "The page you are looking for might have been removed, had its name changed, or is temporarily unavailable."
                ]
            ]
        ]
        Html.section [
            prop.className "content-card"
            prop.custom ("style", "text-align: center; padding: 3rem 1.5rem;")
            prop.children [
                Html.p [
                    prop.custom ("style", "margin-bottom: 2rem; font-size: 1.15rem; color: var(--muted);")
                    prop.text "We suggest returning to the home page or exploring a completely random traditional dish from our archive."
                ]
                Html.div [
                    prop.custom ("style", "display: flex; gap: 1rem; justify-content: center; flex-wrap: wrap;")
                    prop.children [
                        Html.a [
                            prop.className "chip-link"
                            prop.href (relativeHref "404.html" "index.html")
                            prop.text "Back to Home Page"
                        ]
                        Html.a [
                            prop.className "chip-link"
                            prop.href "/random"
                            prop.text "View Random Page"
                        ]
                    ]
                ]
            ]
        ]
    ] []
