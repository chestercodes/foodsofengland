module SiteGenerator.Pages.IngredientPage

open System.Text.Encodings.Web
open System.Text.Json
open Feliz.ViewEngine
open SiteGenerator.Paths
open SiteGenerator.Types
open SiteGenerator.Pages.PageShell
open SiteGenerator.Pages.Helpers

let renderIngredientPage (config: RunConfig) (posts: SitePost array) =
    let ingredientPagePath = "ingredients.html"

    let postsWithReceipts =
        posts
        |> Array.filter (fun post -> post.ReceiptCount > 0)

    let ingredients =
        postsWithReceipts
        |> Array.collect (fun post -> post.Ingredients |> List.toArray)
        |> Array.distinctBy (fun ingredient -> ingredient.ToLowerInvariant())
        |> Array.sortBy (fun ingredient -> ingredient.ToLowerInvariant())

    let ingredientData =
        postsWithReceipts
        |> Array.map (fun post ->
            { IngredientDish.title = post.Title
              href = relativeHref ingredientPagePath post.FinalPath
              ingredients = post.Ingredients |> List.toArray
              firstLine = getFirstLine post.ContentHtml })

    let jsonOptions = JsonSerializerOptions(Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)
    let ingredientJson = JsonSerializer.Serialize(ingredientData, jsonOptions)

    renderPage config ingredientPagePath "Ingredient explorer" "Find traditional English dishes by picking and matching their ingredients." [
        Html.section [
            prop.className "hero"
            prop.children [
                Html.h1 [ prop.className "hero__title"; prop.text "Dishes by ingredient" ]
                Html.p [
                    prop.className "hero__lead"
                    prop.text "Want to try and make a dish? Pick the ingredients that you have and the page will narrow the post list to dishes containing all of them."
                ]
            ]
        ]
        Html.section [
            prop.className "ingredient-layout"
            prop.children [
                Html.div [
                    prop.className "filter-panel"
                    prop.children [
                        Html.h2 [ prop.text "Ingredients" ]
                        Html.p [ prop.className "ingredient-help"; prop.text "The filter starts broad, then shows the dishes that match all of the selected ingredients as you add them." ]
                        Html.input [
                            prop.type' "text"
                            prop.id "ingredient-search"
                            prop.placeholder "Search ingredients..."
                            prop.className "search-widget__input"
                            prop.custom("style", "margin-bottom: 1.2rem; width: 100%;")
                        ]
                        Html.div [
                            prop.className "ingredient-grid"
                            prop.children [
                                for ingredient in ingredients do
                                    Html.label [
                                        prop.className "ingredient-toggle"
                                        prop.children [
                                            Html.input [
                                                prop.type' "checkbox"
                                                prop.value ingredient
                                            ]
                                            Html.span [ prop.text ingredient ]
                                        ]
                                    ]
                            ]
                        ]
                    ]
                ]
                Html.div [
                    prop.className "results-panel"
                    prop.children [
                        Html.h2 [ prop.text "Matching dishes" ]
                        Html.p [ prop.className "ingredient-summary"; prop.id "ingredient-summary"; prop.text "Loading dish index..." ]
                        Html.ul [ prop.className "result-list"; prop.id "ingredient-results" ]
                    ]
                ]
            ]
        ]
        Html.script [
            prop.id "ingredients-data"
            prop.type' "application/json"
            prop.children [ rawHtml ingredientJson ]
        ]
    ] [ "assets/ingredients.js" ]
