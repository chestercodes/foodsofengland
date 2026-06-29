module SiteGenerator.Types

type RunConfig =
    { SiteDataDir: string
      DownloaderOutput: string
      OutputDir: string }

type SiteTag =
    { TagName: string
      TagValue: string
      Label: string
      PagePath: string }

type RelatedLink =
    { Description: string option
      Text: string
      Url: string }

type SitePost =
    { FinalPath: string
      Title: string
      Heading: string option
      Synonym: string option
      ContentHtml: string
      Tags: SiteTag list
      RelatedPages: RelatedLink list
      Ingredients: string list
      ReceiptCount: int
      EarliestSnapshot: string option
      UsedSnapshot: string option }

type SiteAsset =
    { InitialPath: string
      FinalPath: string }

type IngredientDish =
    { title: string
      href: string
      ingredients: string array
      firstLine: string }