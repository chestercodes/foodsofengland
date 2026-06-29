module SiteGenerator.Pages.IndexPage

open Feliz.ViewEngine
open SiteGenerator.Paths
open SiteGenerator.Types
open SiteGenerator.Pages.PageShell

let renderIndexPage (config: RunConfig) (posts: SitePost array) =
    renderPage config "index.html" "Foods of England" "Explore thousands of English recipes, regional specialties, historical cookbooks, and ingredients." [
        Html.section [
            prop.className "hero"
            prop.children [
                Html.h1 [
                    prop.className "hero__title"
                    prop.text "Foods of England"
                ]
                Html.blockquote [
                    prop.custom("style", "font-style: italic; border-left: 4px solid var(--accent); padding-left: 1rem; margin: 0 0 1.5rem 0; font-size: 1.15rem; color: var(--ink);")
                    prop.children [
                        Html.p "“Cooking in England, when well done, is superior to that of any other country in the world.”"
                        Html.footer [
                            prop.custom("style", "font-size: 0.95rem; color: var(--muted); margin-top: 0.25rem;")
                            prop.text "— Louis Eustache Ude, Le Cuisinier français"
                        ]
                    ]
                ]
                Html.p "Every traditional dish, every original receipt*, every classic cookbook."

            ]
        ]
        Html.section [
            prop.className "content-card"
            prop.children [
                
                Html.h2 [ prop.text "Where we're up to..." ]
                Html.p [
                    prop.text "Yes 'receipts' ('recipe' is French). On the way to restoring the glory of English food – finding the story behind every single traditional dish... with thousands of dishes listed, original receipts, and major historical cookbooks online."
                ]

                Html.p [
                    prop.children [
                        Html.text "This site is a tribute to the Foods of England project. For more information see the "
                        Html.a [ prop.href (relativeHref "index.html" "about.html"); prop.text "About" ]
                        Html.text " page."
                    ]
                ]
                
                Html.h2 [ prop.text "Today's Specials..." ]
                Html.p [
                    prop.children [
                        Html.text "See the full list of "
                        Html.a [ prop.href (relativeHref "index.html" "references.html"); prop.text "cookbooks" ]
                    ]
                ]
                Html.p [
                    prop.children [
                        Html.text "Fancy some "
                        Html.a [ prop.href (relativeHref "index.html" "feasting.html"); prop.text "Feasting" ]
                        Html.text "?"
                    ]
                ]
                Html.p [
                    prop.children [
                        Html.text "Want a dish to match the date? see the  "
                        Html.a [ prop.href (relativeHref "index.html" "diary.html"); prop.text "Diary" ]
                        Html.text "."
                    ]
                ]
                Html.p [
                    prop.children [
                        Html.text "Want to know how to measure stuff? You'll need English "
                        Html.a [ prop.href (relativeHref "index.html" "weights-and-measures.html"); prop.text "Weights and Measures" ]
                        Html.text "."
                    ]
                ]
                Html.p [
                    prop.children [
                        Html.text "The English collection of Piquant Table Sauces is quite unique. Start with "
                        Html.a [ prop.href (relativeHref "index.html" "worcester-or-worcestershire-sauce.html"); prop.text "Worcester Sauce" ]
                        Html.text " and "
                        Html.a [ prop.href (relativeHref "index.html" "yorkshire-relish.html"); prop.text "Yorkshire Relish" ]
                        Html.text "."
                    ]
                ]
                Html.p [
                    prop.children [
                        Html.text "Did the Puritans really ban mince pies? "
                        Html.a [ prop.href (relativeHref "index.html" "christmas-banned-by-puritans.html"); prop.text "Probably not..." ]
                    ]
                ]
                Html.p [
                    prop.children [
                        Html.text "Discover "
                        Html.a [ prop.href (relativeHref "index.html" "badger.html"); prop.text "Roast Badger" ]
                        Html.text ", and the Ilchester Badger Feast."
                    ]
                ]
                Html.p [
                    prop.children [
                        Html.text "Which came first, "
                        Html.a [ prop.href (relativeHref "index.html" "curry.html"); prop.text "Curry" ]
                        Html.text " or "
                        Html.a [ prop.href (relativeHref "index.html" "fish-and-chips.html"); prop.text "Fish-and-Chips" ]
                        Html.text "?"
                    ]
                ]
                Html.p [
                    prop.children [
                        Html.text "What did "
                        Html.a [ prop.href (relativeHref "index.html" "scott-s-last-expedition.html"); prop.text "Captain Scott take with him to eat" ]
                        Html.text "?"
                    ]
                ]
                Html.p [
                    prop.children [
                        Html.text "When did we last eat "
                        Html.a [ prop.href (relativeHref "index.html" "horse.html"); prop.text "horsemeat" ]
                        Html.text " in England?"
                    ]
                ]
                Html.p [
                    prop.children [
                        Html.a [ prop.href (relativeHref "index.html" "beef-wellington.html"); prop.text "Beef Wellington" ]
                        Html.text " – thoroughly English and completely Victorian? Or French, named in America after an Irishman in the 1920’s?"
                    ]
                ]
                Html.p [
                    prop.children [
                        Html.a [ prop.href (relativeHref "index.html" "scotch-eggs.html"); prop.text "Scotch Eggs" ]
                        Html.text " – a tale of disinfectant."
                    ]
                ]
                Html.p [
                    prop.children [
                        Html.text "Which is the real thing? "
                        Html.a [ prop.href (relativeHref "index.html" "abernethy-biscuits.html"); prop.text "Abernethy Biscuits" ]
                        Html.text " or "
                        Html.a [ prop.href (relativeHref "index.html" "digestive-biscuits.html"); prop.text "Digestive Biscuits" ]
                        Html.text "?"
                    ]
                ]
                Html.p [
                    prop.children [
                        Html.text "Was "
                        Html.a [ prop.href (relativeHref "index.html" "sally-lunn.html"); prop.text "Sally Lunn" ]
                        Html.text " a Huguenot Maid, or just a spelling mistake?"
                    ]
                ]
                Html.p [
                    prop.children [
                        Html.text "Everything you ever wanted to know about "
                        Html.a [ prop.href (relativeHref "index.html" "thieves-vinegar.html"); prop.text "Thieves’ Vinegar" ]
                        Html.text "."
                    ]
                ]
                Html.p [
                    prop.children [
                        Html.text "Explore "
                        Html.a [ prop.href (relativeHref "index.html" "pandewaff.html"); prop.text "Pandewaff" ]
                        Html.text ", the "
                        Html.a [ prop.href (relativeHref "index.html" "wigan-slappy.html"); prop.text "Wigan Slappy" ]
                        Html.text " and the "
                        Html.a [ prop.href (relativeHref "index.html" "omnibus-pudding.html"); prop.text "Omnibus Pudding" ]
                        Html.text ". Discover the strange origins of "
                        Html.a [ prop.href (relativeHref "index.html" "loseyns.html"); prop.text "Lasagne (Loseyns)" ]
                        Html.text ", "
                        Html.a [ prop.href (relativeHref "index.html" "stuffed-chine.html"); prop.text "Chine" ]
                        Html.text ", and "
                        Html.a [ prop.href (relativeHref "index.html" "hindle-wakes.html"); prop.text "Hindle Wakes" ]
                        Html.text "."
                    ]
                ]
                Html.p [
                    prop.children [
                        Html.text "Trace the history of familiar favorites: "
                        Html.a [ prop.href (relativeHref "index.html" "marmite.html"); prop.text "Marmite" ]
                        Html.text ", "
                        Html.a [ prop.href (relativeHref "index.html" "dairylea.html"); prop.text "Dairylea" ]
                        Html.text ", "
                        Html.a [ prop.href (relativeHref "index.html" "bath-olivers.html"); prop.text "Bath Olivers" ]
                        Html.text ", "
                        Html.a [ prop.href (relativeHref "index.html" "cornflake-cakes.html"); prop.text "Cornflake Cakes" ]
                        Html.text ", and "
                        Html.a [ prop.href (relativeHref "index.html" "twiglets.html"); prop.text "Twiglets" ]
                        Html.text "."
                    ]
                ]
                Html.p [
                    prop.children [
                        Html.text "Foods inspired by the "
                        Html.a [ prop.href (relativeHref "index.html" "royalty.html"); prop.text "royal family" ]
                    ]
                ]
            ]
        ]
    ] []
