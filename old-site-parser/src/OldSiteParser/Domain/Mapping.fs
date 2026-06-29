module OldSiteParser.Domain.Mapping

open OldSiteParser.Domain.Types
open OldSiteParser.Database

// --- ScannedFile <-> Step1ScannedFileEntity ---

let scannedFileToEntity (sf: ScannedFile) : Step1ScannedFileEntity =
    { RelativePath = unwrapPath sf.Path
      FileHash = unwrapHash sf.Hash
      FileType = fileTypeToString sf.FileType
      EarliestSnapshot = sf.EarliestSnapshot |> Option.defaultValue null
      UsedSnapshot = sf.UsedSnapshot |> Option.defaultValue null
      TitleTag = sf.TitleTag |> Option.defaultValue null
      ErrorMessage = sf.Error |> Option.defaultValue null
      IsExcluded = sf.IsExcluded
      HasFilesPrefix = sf.HasFilesPrefix
      IsRefreshedSitePage =
        match sf.IsRefreshedSitePage with
        | Some v -> System.Nullable<bool>(v)
        | None -> System.Nullable<bool>()
      HasPageNotFoundTitle =
        match sf.HasPageNotFoundTitle with
        | Some v -> System.Nullable<bool>(v)
        | None -> System.Nullable<bool>()
      LastModified = sf.LastModified }

let entityToScannedFile (e: Step1ScannedFileEntity) : ScannedFile =
    { Path = RelativePath e.RelativePath
      Hash = FileHash e.FileHash
      FileType = stringToFileType e.FileType
      EarliestSnapshot = if isNull e.EarliestSnapshot then None else Some e.EarliestSnapshot
      UsedSnapshot = if isNull e.UsedSnapshot then None else Some e.UsedSnapshot
      TitleTag = if isNull e.TitleTag then None else Some e.TitleTag
      Error = if isNull e.ErrorMessage then None else Some e.ErrorMessage
      IsExcluded = e.IsExcluded
      HasFilesPrefix = e.HasFilesPrefix
      IsRefreshedSitePage =
        if e.IsRefreshedSitePage.HasValue then Some e.IsRefreshedSitePage.Value
        else None
      HasPageNotFoundTitle =
        if e.HasPageNotFoundTitle.HasValue then Some e.HasPageNotFoundTitle.Value
        else None
      LastModified = e.LastModified }

// --- PageContentRecord <-> Step2RawContentEntity ---

let pageContentToEntity (pc: PageContentRecord) : Step2RawContentEntity =
    { RelativePath = unwrapPath pc.Path
      Content = pc.Content
      TemplateType = templateTypeToString pc.Template }

let entityToPageContent (e: Step2RawContentEntity) : PageContentRecord =
    { Path = RelativePath e.RelativePath
      Content = e.Content
      Template = stringToTemplateType e.TemplateType }

// --- ParsedUrl <-> Step2ParsedUrlEntity ---

let parsedUrlToEntity (pu: ParsedUrl) : Step2ParsedUrlEntity =
    let fileExists =
        match pu.FileExists with
        | Some Exists -> System.Nullable<bool>(true)
        | Some DoesNotExist -> System.Nullable<bool>(false)
        | Some Ambiguous -> System.Nullable<bool>()
        | None -> System.Nullable<bool>()

    { Id = 0
      RelativePath = unwrapPath pu.SourcePath
      RawUrl = unwrapRawUrl pu.RawUrl
      ServerPath = pu.ServerPath |> Option.defaultValue null
      DiskPath = pu.DiskPath |> Option.defaultValue null
      RenamedUrl = pu.RenamedUrl |> Option.defaultValue null
      TagType = pu.TagType
      AltText = pu.AltText |> Option.defaultValue null
      FileExists = fileExists }

let entityToParsedUrl (e: Step2ParsedUrlEntity) : ParsedUrl =
    let fileExists =
        if e.FileExists.HasValue then
            if e.FileExists.Value then Some Exists else Some DoesNotExist
        else
            None

    { SourcePath = RelativePath e.RelativePath
      RawUrl = RawUrl e.RawUrl
      ServerPath = if isNull e.ServerPath then None else Some e.ServerPath
      DiskPath = if isNull e.DiskPath then None else Some e.DiskPath
      RenamedUrl = if isNull e.RenamedUrl then None else Some e.RenamedUrl
      TagType = e.TagType
      AltText = if isNull e.AltText then None else Some e.AltText
      FileExists = fileExists }

// --- FinalNameEntry <-> Step3FinalNameEntity ---

let finalNameToEntity (fn: FinalNameEntry) : Step3FinalNameEntity =
    { RelativePath = unwrapPath fn.OriginalPath
      FinalName = unwrapFinalName fn.Name
      FileType = fileTypeToString fn.FileType
      DuplicateOf = fn.DuplicateOf |> Option.defaultValue null }

let entityToFinalName (e: Step3FinalNameEntity) : FinalNameEntry =
    { OriginalPath = RelativePath e.RelativePath
      Name = FinalName e.FinalName
      FileType = stringToFileType e.FileType
      DuplicateOf = if isNull e.DuplicateOf then None else Some e.DuplicateOf }

// --- CustomTagValue <-> Step4PreTidyMetadataEntity ---

let customTagToEntity (ct: CustomTagValue) : Step4PreTidyMetadataEntity =
    { Id = 0
      RelativePath = unwrapPath ct.SourcePath
      TagName = customTagNameToString ct.TagName
      TextContent = ct.TextContent }

let entityToCustomTag (e: Step4PreTidyMetadataEntity) : CustomTagValue =
    { SourcePath = RelativePath e.RelativePath
      TagName = stringToCustomTagName e.TagName
      TextContent = e.TextContent }

// --- TidyResult <-> Step5TidyResultEntity ---

let tidyResultToEntity (tr: TidyResult) : Step5TidyResultEntity =
    { FinalName = unwrapFinalName tr.FinalName
      ExitCode = tr.ExitCode
      Stdout = tr.Stdout
      Stderr = tr.Stderr
      Output = tr.Output }

let entityToTidyResult (e: Step5TidyResultEntity) : TidyResult =
    { FinalName = FinalName e.FinalName
      ExitCode = e.ExitCode
      Stdout = e.Stdout
      Stderr = e.Stderr
      Output = e.Output }

// --- ExtractedMetadata <-> Step6PostTidyMetadataEntity ---

let metadataToEntity (m: ExtractedMetadata) : Step6PostTidyMetadataEntity =
    { Id = 0
      RelativePath = unwrapPath m.SourcePath
      MetadataKey = m.Key
      MetadataValue = m.Value }

let entityToMetadata (e: Step6PostTidyMetadataEntity) : ExtractedMetadata =
    { SourcePath = RelativePath e.RelativePath
      Key = e.MetadataKey
      Value = e.MetadataValue }

// --- Receipt <-> Step6ReceiptEntity ---

let receiptToEntity (r: Receipt) : Step6ReceiptEntity =
    { ReceiptId = unwrapReceiptId r.Id
      ReceiptSource = r.Source
      RelativePath = unwrapPath r.SourcePath
      ReceiptText = r.Text }

let entityToReceipt (e: Step6ReceiptEntity) : Receipt =
    { Id = ReceiptId e.ReceiptId
      Source = e.ReceiptSource
      SourcePath = RelativePath e.RelativePath
      Text = e.ReceiptText }
