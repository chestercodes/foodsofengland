module OldSiteParser.Tests.Steps.Step7IngredientsTests

open Xunit
open OldSiteParser.Database
open OldSiteParser.Steps.Step7_Ingredients

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

let private makeReceipt (id: string) (relativePath: string) (text: string) : Step6ReceiptEntity =
    { ReceiptId = id
      ReceiptSource = ""
      RelativePath = relativePath
      ReceiptText = text }

// ===========================================================================
// extractIngredientsFromText
// ===========================================================================

[<Fact>]
let ``extractIngredientsFromText extracts word after 'of'`` () =
    let text = "Mix 2 cups of flour with 1 teaspoon of salt"
    let result = extractIngredientsFromText text
    Assert.Contains("flour", result)
    Assert.Contains("salt", result)

[<Fact>]
let ``extractIngredientsFromText extracts word after measurement without 'of'`` () =
    let text = "Add 1 cup butter and 2 tablespoons honey"
    let result = extractIngredientsFromText text
    Assert.Contains("butter", result)
    Assert.Contains("honey", result)

[<Fact>]
let ``extractIngredientsFromText extracts word after digit number`` () =
    let text = "Use 3 eggs and 4 apples"
    let result = extractIngredientsFromText text
    Assert.Contains("eggs", result)
    Assert.Contains("apples", result)

[<Fact>]
let ``extractIngredientsFromText extracts word after number word`` () =
    let text = "Mix two pounds of barley with one ounce of yeast"
    let result = extractIngredientsFromText text
    Assert.Contains("barley", result)
    Assert.Contains("yeast", result)

[<Fact>]
let ``extractIngredientsFromText does not include measurement words as ingredients`` () =
    let text = "2 cups of flour"
    let result = extractIngredientsFromText text
    Assert.DoesNotContain("cups", result)
    Assert.Contains("flour", result)

[<Fact>]
let ``extractIngredientsFromText strips HTML before processing`` () =
    let text = "Take <b>2</b> cups of <i>flour</i> and 1 teaspoon of <b>salt</b>"
    let result = extractIngredientsFromText text
    Assert.Contains("flour", result)
    Assert.Contains("salt", result)

[<Fact>]
let ``extractIngredientsFromText returns alphabetically sorted result`` () =
    let text = "1 cup of sugar, 1 cup of flour, 1 cup of butter"
    let result = extractIngredientsFromText text
    Assert.Equal<string list>(result |> List.sort, result)

[<Fact>]
let ``extractIngredientsFromText returns deduplicated result`` () =
    let text = "1 cup of flour and 2 cups of flour"
    let result = extractIngredientsFromText text
    let flourCount = result |> List.filter (fun x -> x = "flour") |> List.length
    Assert.Equal(1, flourCount)

[<Fact>]
let ``extractIngredientsFromText handles measurement then 'of' then ingredient`` () =
    let text = "Add two tablespoons of honey"
    let result = extractIngredientsFromText text
    Assert.Contains("honey", result)

[<Fact>]
let ``extractIngredientsFromText ignores very short words`` () =
    // "of a" → "a" is length 1, should not become an ingredient
    let text = "Take 1 cup of a"
    let result = extractIngredientsFromText text
    Assert.DoesNotContain("a", result)

[<Fact>]
let ``extractIngredientsFromText handles fragments across multiple separators`` () =
    // Each clause is separated by , ; .
    let text = "1 cup of flour, 2 eggs; 3 tablespoons of oil. 1 teaspoon of salt"
    let result = extractIngredientsFromText text
    Assert.Contains("flour", result)
    Assert.Contains("eggs", result)
    Assert.Contains("oil", result)
    Assert.Contains("salt", result)

[<Fact>]
let ``extractIngredientsFromText handles real example 1`` () =
    let text = """
    <br>
    <b>61. Abernethy Biscuits. (Dr. Abernethy's Original Recipe.)</b><br>
    1 quart of milk, 6 eggs, 8 ozs. of sugar, ½ oz. of caraway seeds, with flour sufficient to make the whole of the required consistency. They are generally weighed off at 2 ozs. each, moulded up, pinned and docked, and baked in a moderate oven.<br>
    Note. The heat of an oven is not required so strong for biscuits containing sugar, as it causes them to take more colour in less time.<br>
    <br>
    <b>62. Abernethys as made in London.</b><br>
    7 lbs. of flour, 8 ozs. of sugar, 8 ozs. of butter, 4 eggs, 1 ½ pint of milk, 2 tablespoonfuls of orange-flower water, ½ oz. of caraway seeds.<br>
    <br>
    <b>63. Usual Way of making Abernethy Biscuits.</b><br>
    Take 8 lbs. of flour, 1 ½ lb. of butter and lard, 12 ozs. of sugar, ½ oz. of caraway seeds; some use about ½ oz. of powdered <a href="food-additives.html"><b>volatile salts</b></a>. Proceed to make into dough as before. Well break the dough and finish with either hand or machine.<br>
    <hr>
    <br>
    <br>
    Abernethy biscuits were developed by the distinguished London surgeon John Abernethy (1764-1831). '<i>Reece's Cylopedia</i>' of c1820 tells us that; "His '<i>Surgical Observations on the Constitutional Origin and Treatment of Local Diseases</i>' (1809) ... was one of the earliest popular works on medical science. So great was his zeal in encouraging patients to read the book that he earned the nickname "Doctor My-Book". He taught that local diseases were frequently the results of disordered states of the digestive organs, and were to be treated by purging and attention to diet." Part of which attention was his promotion, from about 1829, of his coarse-flour biscuits as an aid to effective digestion. They quickly seem to have gained a certain celebrity...<br>
    <br>
    """
    let result = extractIngredientsFromText text
    Assert.Contains("milk", result)
    Assert.Contains("eggs", result)
    Assert.Contains("sugar", result)
    Assert.Contains("caraway seeds", result)
    Assert.Contains("butter", result)
    Assert.Contains("orange-flower water", result)


// ===========================================================================
// discoverIngredients
// ===========================================================================

[<Fact>]
let ``discoverIngredients aggregates ingredients across multiple receipts`` () =
    let receipts =
        [| makeReceipt "r1" "page1.html" "1 cup of flour and 1 cup of sugar"
           makeReceipt "r2" "page2.html" "2 cups of flour and 3 eggs" |]
    let result = discoverIngredients receipts
    Assert.Contains("flour", result)
    Assert.Contains("sugar", result)
    Assert.Contains("eggs", result)

[<Fact>]
let ``discoverIngredients deduplicates ingredients across receipts`` () =
    let receipts =
        [| makeReceipt "r1" "page1.html" "1 cup of flour"
           makeReceipt "r2" "page2.html" "2 cups of flour" |]
    let result = discoverIngredients receipts
    let flourCount = result |> List.filter (fun x -> x = "flour") |> List.length
    Assert.Equal(1, flourCount)

[<Fact>]
let ``discoverIngredients returns alphabetically sorted list`` () =
    let receipts =
        [| makeReceipt "r1" "page1.html" "1 cup of sugar, 1 cup of flour, 3 eggs" |]
    let result = discoverIngredients receipts
    Assert.Equal<string list>(result |> List.sort, result)

[<Fact>]
let ``discoverIngredients returns empty list for empty receipt collection`` () =
    let result = discoverIngredients [||]
    Assert.Empty(result)

[<Fact>]
let ``discoverIngredients returns empty list when receipts have no extractable ingredients`` () =
    let receipts =
        [| makeReceipt "r1" "page1.html" "Stir well and cook for thirty minutes until done" |]
    let result = discoverIngredients receipts
    Assert.Empty(result)

// ===========================================================================
// findIngredientMentions
// ===========================================================================

[<Fact>]
let ``findIngredientMentions returns matching ingredients`` () =
    let receipt = makeReceipt "r1" "page.html" "Take flour, water and salt"
    let ingredients = [ "butter"; "flour"; "salt"; "sugar"; "water" ]
    let result = findIngredientMentions ingredients receipt
    Assert.Contains("flour", result)
    Assert.Contains("salt", result)
    Assert.Contains("water", result)
    Assert.DoesNotContain("butter", result)
    Assert.DoesNotContain("sugar", result)

[<Fact>]
let ``findIngredientMentions uses whole-word matching`` () =
    // "salt" must not match inside "saltpetre"
    let receipt = makeReceipt "r1" "page.html" "Rub saltpetre into the meat"
    let ingredients = [ "salt" ]
    let result = findIngredientMentions ingredients receipt
    Assert.Empty(result)

[<Fact>]
let ``findIngredientMentions is case-insensitive`` () =
    let receipt = makeReceipt "r1" "page.html" "Take Flour and SALT"
    let ingredients = [ "flour"; "salt" ]
    let result = findIngredientMentions ingredients receipt
    Assert.Contains("flour", result)
    Assert.Contains("salt", result)

[<Fact>]
let ``findIngredientMentions strips HTML from receipt text before matching`` () =
    let receipt = makeReceipt "r1" "page.html" "Take <b>flour</b> and <i>salt</i>"
    let ingredients = [ "flour"; "salt" ]
    let result = findIngredientMentions ingredients receipt
    Assert.Contains("flour", result)
    Assert.Contains("salt", result)

[<Fact>]
let ``findIngredientMentions returns empty list when no ingredients match`` () =
    let receipt = makeReceipt "r1" "page.html" "Stir well and cook for 30 minutes"
    let ingredients = [ "flour"; "salt"; "sugar" ]
    let result = findIngredientMentions ingredients receipt
    Assert.Empty(result)

[<Fact>]
let ``findIngredientMentions returns empty list for empty ingredient list`` () =
    let receipt = makeReceipt "r1" "page.html" "Take flour and salt"
    let result = findIngredientMentions [] receipt
    Assert.Empty(result)

[<Fact>]
let ``findIngredientMentions handles multi-word ingredient names`` () =
    let receipt = makeReceipt "r1" "page.html" "Add barley flour to the bowl"
    let ingredients = [ "barley"; "flour" ]
    let result = findIngredientMentions ingredients receipt
    Assert.Contains("barley", result)
    Assert.Contains("flour", result)
