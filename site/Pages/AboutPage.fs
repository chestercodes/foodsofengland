module SiteGenerator.Pages.AboutPage

open Feliz.ViewEngine
open SiteGenerator.Types
open SiteGenerator.Pages.PageShell

let renderAboutPage (config: RunConfig) =
    renderPage config "about.html" "About - Foods of England" "Learn more about the Foods of England Project tribute website, dedicated to preserving English culinary heritage." [
        Html.section [
            prop.className "hero"
            prop.children [
                Html.h1 [ prop.className "hero__title"; prop.text "The Foods of England Project" ]
                Html.p [
                    prop.className "hero__lead"
                    prop.text "Preserving and exploring the rich culinary heritage of England."
                ]
            ]
        ]
        Html.section [
            prop.className "content-card"
            prop.children [
                Html.div [
                    prop.className "post-content"
                    prop.children [
                        Html.p [
                            prop.children [
                                Html.text "The foods of England project was the efforts of "
                                Html.a [ prop.href "https://x.com/SquashedBox"; prop.text "Glyn Hughes" ]
                                Html.text ", all credit and blame should go to him."
                            ]
                        ]
                        Html.p "He sadly passed in 2022, and the domain name expired fairly soon after. It now seems to have been taken over by someone who seems to be advertising for online casinos."
                        Html.p "This tribute website is dedicated to preserving the work of the Foods of England project in a convenient format."
                        Html.p """The site foodsofengland.info is an effort to preserve the site in it's original form, which is great, but it does not have a functional search and is missing some pages.
This site aims to preserve the original content, but also make it more usable and accessible."""
                        
                        Html.p [
                            prop.children [
                                Html.text "I am a software developer, so I cannot commit to adding to the historical research, but I can add edits or improvements to this site. If you have any suggestions then please get in touch by filing an issue on the "
                                Html.a [
                                    prop.href "https://github.com/chestercodes/foodsofengland/issues"
                                    prop.text "GitHub repository"
                                ]
                            ]
                        ]

                    ]
                ]
            ]
        ]
    ] []
