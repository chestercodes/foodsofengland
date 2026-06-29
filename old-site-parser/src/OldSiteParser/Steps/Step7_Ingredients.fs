module OldSiteParser.Steps.Step7_Ingredients

open System
open System.IO
open System.Linq
open System.Text.RegularExpressions
open OldSiteParser.Database
open OldSiteParser.Shared

// ==========================================================================
// INGREDIENT EXTRACTION HELPERS
// ==========================================================================

/// Common measurement words used in recipes — not treated as ingredient names.
let commonMeasurements =
    [| "cup"; "cups"; "tablespoon"; "tablespoons"; "tablespoonful"; "tablespoonfuls"; "tbsp"
       "teaspoon"; "teaspoons"; "teaspoonful"; "teaspoonfuls"; "tsp"
       "pound"; "pounds"; "lb"; "lbs"; "ounce"; "ounces"; "oz"; "ozs"
       "pint"; "pints"; "quart"; "quarts"; "gallon"; "gallons"
       "gram"; "grams"; "kg"; "kilogram"; "kilograms"
       "handful"; "handfuls"; "pinch"; "dash"; "drop"; "drops"
       "clove"; "cloves"; "slice"; "slices"; "sprig"; "sprigs"
       "bunch"; "bunches"; "head"; "heads" |]

/// Number words that trigger ingredient extraction when followed by a candidate word.
let numberWords =
    [| "one"; "two"; "three"; "four"; "five"; "six"; "seven"; "eight"; "nine"; "ten"
       "half"; "quarter"; "dozen" |]

let private stripHtml (text: string) : string =
    Regex.Replace(text, @"<[^>]+>", " ")

let private normalizeWhitespace (text: string) : string =
    Regex.Replace(text, @"\s+", " ").Trim()

/// Split receipt text into fragments on , . ; boundaries.
let private splitIntoFragments (text: string) : string[] =
    text.Split([| ','; '.'; ';' |], StringSplitOptions.RemoveEmptyEntries)
    |> Array.map (fun s -> s.Trim())
    |> Array.filter (fun s -> s.Length > 0)

/// Lowercase and strip non-alpha characters (except hyphens) from a word.
let private cleanWord (word: string) : string =
    Regex.Replace(word.Trim().ToLowerInvariant(), @"[^a-z\-]", "")

/// Return true if the raw (lowercased) word is a digit sequence or a number word.
let private isNumber (rawWord: string) : bool =
    Regex.IsMatch(rawWord, @"^\d") || Array.contains rawWord numberWords

let private isMeasurement (cleaned: string) : bool =
    Array.contains cleaned commonMeasurements

/// English function words that terminate an ingredient phrase.
let private stopWords =
    [| "and"; "or"; "with"; "to"; "into"; "in"; "the"; "a"; "an"; "some"; "about"
       "as"; "at"; "by"; "for"; "from"; "on"; "up"; "each"; "sufficient"
       "well"; "then"; "when"; "but"; "not"; "no"; "all"; "this"; "that"
       "which"; "who"; "use"; "off"; "either"; "before"; "after"
       "generally"; "required"; "until"; "make"; "take"; "add"; "put" |]

let private isStopWord (word: string) : bool =
    Array.contains word stopWords

/// A collected multi-word name is valid when it is non-empty and at least 3 chars.
let private isValidIngredientName (name: string) : bool =
    not (String.IsNullOrWhiteSpace(name)) && name.Length >= 3

/// Collect consecutive ingredient words starting at startIdx, stopping when a
/// stop word, trigger word, measurement, number, or fragment end is reached.
/// Returns the words joined by a single space.
let private collectIngredientWords (rawWords: string[]) (startIdx: int) : string =
    let collected = ResizeArray<string>()
    let mutable i = startIdx
    let mutable keepGoing = true

    while i < rawWords.Length && keepGoing do
        let raw = rawWords.[i].Trim().ToLowerInvariant()
        let cleaned = cleanWord raw
        if isStopWord cleaned
           || isMeasurement cleaned
           || isNumber raw
           || cleaned = "of"
           || cleaned.Length < 3 then
            keepGoing <- false
        else
            collected.Add(cleaned)
            i <- i + 1

    String.concat " " (collected |> Seq.toArray)

/// Extract ingredient candidates from a single text fragment.
let private extractFromFragment (fragment: string) : string list =
    let rawWords =
        fragment.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)

    let results = ResizeArray<string>()
    let mutable i = 0

    while i < rawWords.Length do
        let rawWord = rawWords.[i].Trim().ToLowerInvariant()
        let cleaned = cleanWord rawWord

        let triggeredByOf = cleaned = "of"
        let triggeredByMeasurement = isMeasurement cleaned
        let triggeredByNumber = isNumber rawWord

        if (triggeredByOf || triggeredByMeasurement || triggeredByNumber)
           && i + 1 < rawWords.Length then

            let mutable candidateIdx = i + 1

            // Skip "of" immediately following trigger (e.g. "cups of flour")
            if candidateIdx < rawWords.Length
               && cleanWord (rawWords.[candidateIdx].Trim().ToLowerInvariant()) = "of" then
                candidateIdx <- candidateIdx + 1

            // Skip over a measurement word (e.g. "2 tablespoons flour")
            if candidateIdx < rawWords.Length
               && isMeasurement (cleanWord (rawWords.[candidateIdx].Trim().ToLowerInvariant())) then
                candidateIdx <- candidateIdx + 1
                // Skip a further "of" after the measurement (e.g. "2 tablespoons of flour")
                if candidateIdx < rawWords.Length
                   && cleanWord (rawWords.[candidateIdx].Trim().ToLowerInvariant()) = "of" then
                    candidateIdx <- candidateIdx + 1

            if candidateIdx < rawWords.Length then
                let name = collectIngredientWords rawWords candidateIdx
                if isValidIngredientName name then
                    results.Add(name)

        i <- i + 1

    results |> Seq.toList

// ==========================================================================
// PUBLIC API
// ==========================================================================

/// Extract ingredient candidates from receipt text.
/// Returns a sorted, deduplicated list of lowercase ingredient words.
let extractIngredientsFromText (text: string) : string list =
    text
    |> stripHtml
    |> normalizeWhitespace
    |> splitIntoFragments
    |> Array.collect (fun fragment -> extractFromFragment fragment |> Array.ofList)
    |> Array.distinct
    |> Array.sort
    |> Array.toList

/// Discover all ingredient candidates across all receipts.
/// Returns a sorted, deduplicated list.
let discoverIngredients (receipts: Step6ReceiptEntity seq) : string list =
    receipts
    |> Seq.collect (fun r -> extractIngredientsFromText r.ReceiptText)
    |> Seq.distinct
    |> Seq.sort
    |> Seq.toList

/// Find which ingredients from the given list are mentioned (whole-word, case-insensitive)
/// in the given receipt's text.
let findIngredientMentions (ingredientNames: string list) (receipt: Step6ReceiptEntity) : string list =
    let plainText =
        receipt.ReceiptText
        |> stripHtml
        |> normalizeWhitespace
        |> fun s -> s.ToLowerInvariant()

    ingredientNames
    |> List.filter (fun ingredient ->
        Regex.IsMatch(plainText, sprintf @"\b%s\b" (Regex.Escape(ingredient)), RegexOptions.IgnoreCase))

// ==========================================================================
// RUN
// ==========================================================================

let private removeExistingStepData (ctx: PipelineDbContext) =
    if ctx.Step7Ingredients.Any() then
        Logging.logInfo "Step7" "Clearing existing Step7Ingredients..."
        ctx.Step7Ingredients.RemoveRange(ctx.Step7Ingredients) |> ignore
    else
        Logging.logInfo "Step7" "No existing Step7Ingredients to clear."

    if ctx.Step7ReceiptIngredients.Any() then
        Logging.logInfo "Step7" "Clearing existing Step7ReceiptIngredients..."
        ctx.Step7ReceiptIngredients.RemoveRange(ctx.Step7ReceiptIngredients) |> ignore
    else
        Logging.logInfo "Step7" "No existing Step7ReceiptIngredients to clear."

    ctx.SaveChanges() |> ignore

let run (workingDir: string) (ctx: PipelineDbContext) =
    let stopwatch = Diagnostics.Stopwatch.StartNew()
    Logging.logInfo "Step7" "Starting ingredient extraction..."

    Directory.CreateDirectory(workingDir) |> ignore
    removeExistingStepData ctx

    let receipts = ctx.Receipts.ToArray()
    Logging.logInfo "Step7" (sprintf "Stage 1: scanning %d receipts for ingredient candidates..." receipts.Length)

    // -------------------------------------------------------------------------
    // Stage 1: discover ingredient candidates across all receipts
    // -------------------------------------------------------------------------
    let ingredients = discoverIngredients receipts
    let ingredientsFilePath = Path.Combine(workingDir, "Step7_IngredientList.fs")
    let fsLines =
        [| yield "module OldSiteParser.Steps.Step7_IngredientList"
           yield ""
           yield "// this is derived from a file that the code creates which is output to the working directory"
           yield "let ingredientsToExtract ="
           yield "    ["
           yield! ingredients |> List.map (fun i -> sprintf "        \"%s\"" i)
           yield "    ]" |]
    File.WriteAllLines(ingredientsFilePath, fsLines)
    Logging.logInfo "Step7" (sprintf "Stage 1 complete: %d candidate ingredients written to %s" ingredients.Length ingredientsFilePath)

    for ingredient in ingredients do
        ctx.Step7Ingredients.Add({ IngredientName = ingredient }) |> ignore

    ctx.SaveChanges() |> ignore

    // -------------------------------------------------------------------------
    // Stage 2: link receipts to discovered ingredients
    // -------------------------------------------------------------------------
    Logging.logInfo "Step7" "Stage 2: linking receipts to ingredients..."

    let mutable linkCount = 0

    for receipt in receipts do
        let mentions = findIngredientMentions Step7_IngredientList.ingredientsToExtract receipt

        for ingredientName in mentions do
            ctx.Step7ReceiptIngredients.Add(
                { Id = 0
                  RelativePath = receipt.RelativePath
                  ReceiptId = receipt.ReceiptId
                  IngredientName = ingredientName })
            |> ignore

            linkCount <- linkCount + 1

    ctx.SaveChanges() |> ignore

    Logging.logInfo "Step7" (sprintf "Stage 2 complete: %d receipt-ingredient links saved." linkCount)

    stopwatch.Stop()
    Logging.logStepSummary "Step7" receipts.Length stopwatch.Elapsed
