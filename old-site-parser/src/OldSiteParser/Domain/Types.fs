namespace OldSiteParser.Domain

/// Strongly-typed domain types wrapping primitive values.
/// These provide compile-time safety to prevent mixing up strings.
[<AutoOpen>]
module Types =

    // Single-case discriminated unions for primitive wrappers
    type RelativePath = RelativePath of string
    type RawUrl = RawUrl of string
    type FileHash = FileHash of string
    type FinalName = FinalName of string
    type ReceiptId = ReceiptId of string

    type FileType =
        | Html
        | Image
        | Unknown

    type TemplateType =
        | TextStart
        | BodyTextStart
        | PageContent
        | NoTemplate

    type FileExistence =
        | Exists
        | DoesNotExist
        | Ambiguous

    type CustomTagName =
        | Class
        | SubClass
        | Locale

    type ScannedFile =
        { Path: RelativePath
          Hash: FileHash
          FileType: FileType
          EarliestSnapshot: string option
          UsedSnapshot: string option
          TitleTag: string option
          Error: string option
          IsExcluded: bool
          HasFilesPrefix: bool
          IsRefreshedSitePage: bool option
          HasPageNotFoundTitle: bool option
          LastModified: System.DateTime }

    type PageContentRecord =
        { Path: RelativePath
          Content: string
          Template: TemplateType }

    type ParsedUrl =
        { SourcePath: RelativePath
          RawUrl: RawUrl
          ServerPath: string option
          DiskPath: string option
          RenamedUrl: string option
          TagType: string
          AltText: string option
          FileExists: FileExistence option }

    type FinalNameEntry =
        { OriginalPath: RelativePath
          Name: FinalName
          FileType: FileType
          DuplicateOf: string option }

    type CustomTagValue =
        { SourcePath: RelativePath
          TagName: CustomTagName
          TextContent: string }

    type TidyResult =
        { FinalName: FinalName
          ExitCode: int
          Stdout: string
          Stderr: string
          Output: string }

    type ExtractedMetadata =
        { SourcePath: RelativePath
          Key: string
          Value: string }

    type Receipt =
        { Id: ReceiptId
          Source: string
          SourcePath: RelativePath
          Text: string }

    // Helper functions for unwrapping
    let unwrapPath (RelativePath p) = p
    let unwrapHash (FileHash h) = h
    let unwrapFinalName (FinalName n) = n
    let unwrapReceiptId (ReceiptId g) = g
    let unwrapRawUrl (RawUrl u) = u

    let fileTypeToString =
        function
        | Html -> "html"
        | Image -> "image"
        | Unknown -> "unknown"

    let stringToFileType =
        function
        | "html" -> Html
        | "image" -> Image
        | _ -> Unknown

    let templateTypeToString =
        function
        | TextStart -> "text-start"
        | BodyTextStart -> "body-text-start"
        | PageContent -> "page-content"
        | NoTemplate -> "none"

    let stringToTemplateType =
        function
        | "text-start" -> TextStart
        | "body-text-start" -> BodyTextStart
        | "page-content" -> PageContent
        | _ -> NoTemplate

    let customTagNameToString =
        function
        | Class -> "class"
        | SubClass -> "subclass"
        | Locale -> "locale"

    let stringToCustomTagName =
        function
        | "class" -> Class
        | "subclass" -> SubClass
        | "locale" -> Locale
        | s -> failwithf "Unknown custom tag name: %s" s
