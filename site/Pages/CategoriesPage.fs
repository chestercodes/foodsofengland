module SiteGenerator.Pages.CategoriesPage

open System
open Feliz.ViewEngine
open SiteGenerator.Paths
open SiteGenerator.Types
open SiteGenerator.Pages.PageShell
open SiteGenerator.Pages.Helpers

let classLabel = function
    | "accompaniments" -> "Accompaniments"
    | "biscuits" -> "Biscuits"
    | "breads" -> "Breads"
    | "cakes" -> "Cakes"
    | "cheese" -> "Cheeses"
    | "curry" -> "Curry"
    | "dairy" -> "Dairy"
    | "drinks" -> "Drinks"
    | "eggs" -> "Eggs"
    | "fish" -> "Fish"
    | "game-and-offal" -> "Game & Offal"
    | "meals" -> "Classic Meals"
    | "meats" -> "Meats"
    | "puddings-and-sweet-deserts" -> "Puddings & Desserts"
    | "pot-meals" -> "Pot Meals"
    | "pies-and-pastries" -> "Pies & Pastries"
    | "preserves" -> "Preserves"
    | "poultry" -> "Poultry"
    | "sauces-and-spicery" -> "Sauces & Spicery"
    | "sausages" -> "Sausages"
    | "snacks" -> "Snacks"
    | "soups" -> "Soups"
    | "sweets-and-toffee" -> "Sweets & Toffee"
    | "fruit-and-vegetables" -> "Fruit & Vegetables"
    | other -> 
        if String.IsNullOrEmpty(other) then ""
        else
            let parts = other.Split('-') |> Array.map (fun s -> if s.Length > 0 then Char.ToUpper(s.[0]).ToString() + s.[1..] else "")
            String.Join(" ", parts)

let statusLabel = function
    | "historic" -> "Historic"
    | "endangered" -> "Endangered"
    | "eu-protected-name" -> "EU Protected Name"
    | "possibly-extinct" -> "Possibly Extinct"
    | "extinct-and-lost" -> "Extinct & Lost"
    | "formerly-eu-protected-name" -> "Formerly EU Protected Name"
    | other -> 
        if String.IsNullOrEmpty(other) then ""
        else
            let parts = other.Split('-') |> Array.map (fun s -> if s.Length > 0 then Char.ToUpper(s.[0]).ToString() + s.[1..] else "")
            String.Join(" ", parts)

let localeLabel (other: string) =
    if String.IsNullOrEmpty(other) then ""
    else
        let parts = other.Split('-') |> Array.map (fun s -> if s.Length > 0 then Char.ToUpper(s.[0]).ToString() + s.[1..] else "")
        String.Join(" ", parts)

let renderCategoriesPage (config: RunConfig) (posts: SitePost array) =
    let classGroups =
        posts
        |> Array.collect (fun p ->
            p.Tags
            |> List.filter (fun t -> String.Equals(t.TagName, "class", StringComparison.OrdinalIgnoreCase))
            |> List.map (fun t -> t.TagValue.ToLowerInvariant())
            |> List.toArray
        )
        |> Array.groupBy id
        |> Array.map (fun (classVal, occurrences) ->
            let label = classLabel classVal
            classVal, label, occurrences.Length
        )
        |> Array.sortBy (fun (_, label, _) -> label.ToLowerInvariant())

    let statusGroups =
        posts
        |> Array.collect (fun p ->
            p.Tags
            |> List.filter (fun t -> String.Equals(t.TagName, "status", StringComparison.OrdinalIgnoreCase))
            |> List.map (fun t -> t.TagValue.ToLowerInvariant())
            |> List.toArray
        )
        |> Array.groupBy id
        |> Array.map (fun (statusVal, occurrences) ->
            let label = statusLabel statusVal
            statusVal, label, occurrences.Length
        )
        |> Array.sortBy (fun (_, label, _) -> label.ToLowerInvariant())

    let localeGroups =
        posts
        |> Array.collect (fun p ->
            p.Tags
            |> List.filter (fun t -> String.Equals(t.TagName, "locale", StringComparison.OrdinalIgnoreCase))
            |> List.map (fun t -> t.TagValue.ToLowerInvariant())
            |> List.toArray
        )
        |> Array.groupBy id
        |> Array.map (fun (localeVal, occurrences) ->
            let label = localeLabel localeVal
            localeVal, label, occurrences.Length
        )
        |> Array.sortBy (fun (_, label, _) -> label.ToLowerInvariant())

    let serializedPosts =
        posts
        |> Array.choose (fun p ->
            let classes = p.Tags |> List.filter (fun t -> String.Equals(t.TagName, "class", StringComparison.OrdinalIgnoreCase)) |> List.map (fun t -> t.TagValue.ToLowerInvariant()) |> List.toArray
            let statuses = p.Tags |> List.filter (fun t -> String.Equals(t.TagName, "status", StringComparison.OrdinalIgnoreCase)) |> List.map (fun t -> t.TagValue.ToLowerInvariant()) |> List.toArray
            let locales = p.Tags |> List.filter (fun t -> String.Equals(t.TagName, "locale", StringComparison.OrdinalIgnoreCase)) |> List.map (fun t -> t.TagValue.ToLowerInvariant()) |> List.toArray
            
            if classes.Length > 0 || statuses.Length > 0 || locales.Length > 0 then
                Some {| t = p.Title
                        u = relativeHref "categories.html" p.FinalPath
                        d = getFirstLine p.ContentHtml
                        c = classes
                        s = statuses
                        l = locales |}
            else
                None
        )
    let json = System.Text.Json.JsonSerializer.Serialize(serializedPosts)

    renderPage config "categories.html" "Food Categories" "Browse traditional English foods by category, status, and locale." [
        Html.section [
            prop.className "hero"
            prop.children [
                Html.h1 [ prop.className "hero__title"; prop.text "Food Categories" ]
                Html.p [
                    prop.className "hero__lead"
                    prop.text "Explore the rich variety of traditional English foods organized by category, status, and locale. Select a filter on the left to browse."
                ]
            ]
        ]
        
        Html.section [
            prop.className "types-layout"
            prop.children [
                Html.div [
                    prop.className "filter-panel"
                    prop.children [
                        Html.input [
                            prop.type' "text"
                            prop.id "category-search"
                            prop.placeholder "Search categories..."
                            prop.className "search-widget__input"
                            prop.custom("style", "margin-bottom: 1.2rem; width: 100%;")
                        ]
                        
                        Html.h2 [ prop.text "Types of Food" ]
                        Html.ul [
                            prop.className "tag-list"
                            prop.custom ("style", "margin-bottom: 1.5rem;")
                            prop.children [
                                for (classVal, label, count) in classGroups do
                                    Html.li [
                                        Html.a [
                                            prop.className "tag-pill"
                                            prop.href "#"
                                            prop.custom ("data-filter-type", "class")
                                            prop.custom ("data-filter-value", classVal)
                                            prop.custom ("data-friendly-name", label)
                                            prop.text (sprintf "%s (%d)" label count)
                                        ]
                                    ]
                            ]
                        ]
                        
                        Html.h2 [ prop.text "Status of Food" ]
                        Html.ul [
                            prop.className "tag-list"
                            prop.custom ("style", "margin-bottom: 1.5rem;")
                            prop.children [
                                for (statusVal, label, count) in statusGroups do
                                    Html.li [
                                        Html.a [
                                            prop.className "tag-pill"
                                            prop.href "#"
                                            prop.custom ("data-filter-type", "status")
                                            prop.custom ("data-filter-value", statusVal)
                                            prop.custom ("data-friendly-name", label)
                                            prop.text (sprintf "%s (%d)" label count)
                                        ]
                                    ]
                            ]
                        ]

                        Html.h2 [ prop.text "Locales" ]
                        Html.ul [
                            prop.className "tag-list"
                            prop.children [
                                for (localeVal, label, count) in localeGroups do
                                    Html.li [
                                        Html.a [
                                            prop.className "tag-pill"
                                            prop.href "#"
                                            prop.custom ("data-filter-type", "locale")
                                            prop.custom ("data-filter-value", localeVal)
                                            prop.custom ("data-friendly-name", label)
                                            prop.text (sprintf "%s (%d)" label count)
                                        ]
                                    ]
                            ]
                        ]
                    ]
                ]
                Html.div [
                    prop.className "results-panel"
                    prop.children [
                        Html.h3 [ 
                            prop.id "category-selected-title"
                            prop.className "category-title"
                            prop.custom ("style", "display: none;")
                        ]
                        Html.ul [
                            prop.id "category-dishes-list"
                            prop.className "result-list"
                        ]
                        Html.div [
                            prop.id "category-placeholder"
                            prop.children [
                                Html.p [
                                    prop.custom ("style", "color: var(--muted); font-style: italic;")
                                    prop.text "Please select a category, status, or locale filter from the left to view the dishes."
                                ]
                            ]
                        ]
                        Html.script [
                            prop.id "categories-data"
                            prop.type' "application/json"
                            prop.children [ rawHtml json ]
                        ]
                    ]
                ]
            ]
        ]
    ] [ "assets/categories.js" ]
