namespace OldSiteParser.Database

open System.ComponentModel.DataAnnotations
open System.ComponentModel.DataAnnotations.Schema

/// EF Core entity types with mutable properties and primitive types.
/// These are the database-layer representations; domain types are separate.

[<CLIMutable>]
type Step1ScannedFileEntity =
    { [<Key>]
      RelativePath: string
      FileHash: string
      FileType: string
      EarliestSnapshot: string
      UsedSnapshot: string
      TitleTag: string
      ErrorMessage: string
      IsExcluded: bool
      HasFilesPrefix: bool
      IsRefreshedSitePage: System.Nullable<bool>
      HasPageNotFoundTitle: System.Nullable<bool>
      LastModified: System.DateTime }

[<CLIMutable>]
type AnalysisLogEntity =
    { [<Key>]
      [<DatabaseGenerated(DatabaseGeneratedOption.Identity)>]
      Id: int
      Step: string
      Category: string
      RelativePath: string
      Message: string }

[<CLIMutable>]
type Step2RawContentEntity =
    { [<Key>]
      RelativePath: string
      Content: string
      TemplateType: string }

[<CLIMutable>]
type Step2ParsedUrlEntity =
    { [<Key>]
      [<DatabaseGenerated(DatabaseGeneratedOption.Identity)>]
      Id: int
      RelativePath: string
      RawUrl: string
      ServerPath: string
      DiskPath: string
      RenamedUrl: string
      TagType: string
      AltText: string
      FileExists: System.Nullable<bool> }

[<CLIMutable>]
type Step3FinalNameEntity =
    { [<Key>]
      RelativePath: string
      FinalName: string
      FileType: string
      DuplicateOf: string }

[<CLIMutable>]
type Step4PreTidyMetadataEntity =
    { [<Key>]
      [<DatabaseGenerated(DatabaseGeneratedOption.Identity)>]
      Id: int
      RelativePath: string
      TagName: string
      TextContent: string }

[<CLIMutable>]
type Step5TidyResultEntity =
    { [<Key>]
      FinalName: string
      ExitCode: int
      Stdout: string
      Stderr: string
      Output: string }

[<CLIMutable>]
type Step6PostTidyMetadataEntity =
    { [<Key>]
      [<DatabaseGenerated(DatabaseGeneratedOption.Identity)>]
      Id: int
      RelativePath: string
      MetadataKey: string
      MetadataValue: string }

[<CLIMutable>]
type Step6ContentEntity =
    { [<Key>]
      FinalName: string
      Content: string }

[<CLIMutable>]
type Step6ReceiptEntity =
    { [<Key>]
      ReceiptId: string
      ReceiptSource: string
      RelativePath: string
      ReceiptText: string }

[<CLIMutable>]
type Step6RelatedPageEntity =
    { [<Key>]
      [<DatabaseGenerated(DatabaseGeneratedOption.Identity)>]
      Id: int
      SourcePage: string
      RelationDescription: string
      RelatedPageText: string
      RelatedPageUrl: string }

[<CLIMutable>]
type Step7IngredientEntity =
    { [<Key>]
      IngredientName: string }

[<CLIMutable>]
type Step7ReceiptIngredientEntity =
    { [<Key>]
      [<DatabaseGenerated(DatabaseGeneratedOption.Identity)>]
      Id: int
      RelativePath: string
      ReceiptId: string
      IngredientName: string }

[<CLIMutable>]
type SiteDataNonHtmlFileEntity =
    { [<Key>]
      [<DatabaseGenerated(DatabaseGeneratedOption.Identity)>]
      Id: int
      InitialPath: string
      FinalPath: string }

[<CLIMutable>]
type SiteDataPostEntity =
    { [<Key>]
      FinalPath: string
      Title: string
      EarliestSnapshot: string
      UsedSnapshot: string
      ReceiptCount: int
      Heading: string
      Synonym: string
      Content: string }

[<CLIMutable>]
type SiteDataPostTagEntity =
    { [<Key>]
      [<DatabaseGenerated(DatabaseGeneratedOption.Identity)>]
      Id: int
      FinalPath: string
      TagName: string
      TagValue: string }

[<CLIMutable>]
type SiteDataRelatedPageEntity =
    { [<Key>]
      [<DatabaseGenerated(DatabaseGeneratedOption.Identity)>]
      Id: int
      FinalPath: string
      RelationDescription: string
      RelatedPageText: string
      RelatedPageUrl: string }

[<CLIMutable>]
type SiteDataPostIngredientEntity =
    { [<Key>]
      [<DatabaseGenerated(DatabaseGeneratedOption.Identity)>]
      Id: int
      FinalPath: string
      IngredientName: string }

[<CLIMutable>]
type SiteDataPostReceiptEntity =
    { [<Key>]
      [<DatabaseGenerated(DatabaseGeneratedOption.Identity)>]
      Id: int
      FinalPath: string
      ReceiptId: string
      ReceiptSource: string
      ReceiptText: string }
