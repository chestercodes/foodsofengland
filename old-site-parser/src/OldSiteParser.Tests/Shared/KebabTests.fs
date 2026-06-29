module OldSiteParser.Tests.Shared.KebabTests

open Xunit
open FsUnit.Xunit
open OldSiteParser.Domain.Types
open OldSiteParser.Shared.Kebab

// --- toKebabCase ---

[<Fact>]
let ``toKebabCase lowercases and replaces spaces`` () =
    toKebabCase "African Cakes" |> should equal "african-cakes"

[<Fact>]
let ``toKebabCase replaces underscores`` () =
    toKebabCase "apple_hearthbread" |> should equal "apple-hearthbread"

[<Fact>]
let ``toKebabCase strips special characters`` () =
    toKebabCase "Mrs Beeton's Seed Cake!" |> should equal "mrs-beeton-s-seed-cake"

[<Fact>]
let ``toKebabCase collapses multiple hyphens`` () =
    toKebabCase "after---dinner---mints" |> should equal "after-dinner-mints"

[<Fact>]
let ``toKebabCase trims leading and trailing hyphens`` () =
    toKebabCase "-leading-trailing-" |> should equal "leading-trailing"

[<Fact>]
let ``toKebabCase handles empty/whitespace input`` () =
    toKebabCase "" |> should equal "unnamed"
    toKebabCase "   " |> should equal "unnamed"

[<Fact>]
let ``toKebabCase handles mixed case with numbers`` () =
    toKebabCase "Book1658Compleat" |> should equal "book1658compleat"

// --- stripExtension ---

[<Fact>]
let ``stripExtension removes .htm`` () =
    stripExtension "About.htm" |> should equal "About"

[<Fact>]
let ``stripExtension removes .html`` () =
    stripExtension "page.html" |> should equal "page"

[<Fact>]
let ``stripExtension handles no extension`` () =
    stripExtension "noext" |> should equal "noext"

[<Fact>]
let ``stripExtension handles multiple dots`` () =
    stripExtension "file.backup.htm" |> should equal "file.backup"

// --- stripTitlePrefix ---

[<Fact>]
let ``stripTitlePrefix strips 'Foods of England - '`` () =
    stripTitlePrefix "Foods of England - African Cakes"
    |> should equal "African Cakes"

[<Fact>]
let ``stripTitlePrefix strips 'The Foods of England - '`` () =
    stripTitlePrefix "The Foods of England - Apple Hearthbread"
    |> should equal "Apple Hearthbread"

[<Fact>]
let ``stripTitlePrefix is case-insensitive`` () =
    stripTitlePrefix "foods of england - Test"
    |> should equal "Test"

[<Fact>]
let ``stripTitlePrefix leaves non-matching titles unchanged`` () =
    stripTitlePrefix "Some Other Title"
    |> should equal "Some Other Title"

// --- stripCommonParts ---

[<Fact>]
let ``stripCommonParts removes 'foodsofengland' from name`` () =
    stripCommonParts "foodsofengland-beechmint"
    |> should equal "beechmint"

[<Fact>]
let ``stripCommonParts removes '500-' prefix`` () =
    stripCommonParts "500-cake-image"
    |> should equal "cake-image"

[<Fact>]
let ``stripCommonParts returns original if stripping would empty`` () =
    stripCommonParts "foodsofengland"
    |> should equal "foodsofengland"

[<Fact>]
let ``stripCommonParts cleans double hyphens after stripping`` () =
    stripCommonParts "foodsofengland--beechmint"
    |> should equal "beechmint"

// --- kebabifyFileName ---

[<Fact>]
let ``kebabifyFileName strips extension and kebab-cases`` () =
    kebabifyFileName (RelativePath "African_Cakes.htm")
    |> should equal "african-cakes"

[<Fact>]
let ``kebabifyFileName handles spaces in filename`` () =
    kebabifyFileName (RelativePath "foodsofengland beechmint.jpg")
    |> should equal "foodsofengland-beechmint"

// --- findDuplicateNames ---

[<Fact>]
let ``findDuplicateNames returns empty for all unique`` () =
    findDuplicateNames [ "a"; "b"; "c" ]
    |> should be Empty

[<Fact>]
let ``findDuplicateNames detects duplicates`` () =
    findDuplicateNames [ "a"; "b"; "a"; "c"; "b" ]
    |> should equal (set [ "a"; "b" ])

[<Fact>]
let ``findDuplicateNames handles empty input`` () =
    findDuplicateNames [] |> should be Empty
