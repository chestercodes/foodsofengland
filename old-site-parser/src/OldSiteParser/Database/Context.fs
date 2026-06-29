namespace OldSiteParser.Database

open Microsoft.EntityFrameworkCore

type PipelineDbContext(options: DbContextOptions<PipelineDbContext>) =
    inherit DbContext(options)

    [<DefaultValue>]
    val mutable private scannedFiles: DbSet<Step1ScannedFileEntity>

    [<DefaultValue>]
    val mutable private rawPageContents: DbSet<Step2RawContentEntity>

    [<DefaultValue>]
    val mutable private parsedUrls: DbSet<Step2ParsedUrlEntity>

    [<DefaultValue>]
    val mutable private finalNames: DbSet<Step3FinalNameEntity>

    [<DefaultValue>]
    val mutable private preTidyMetadata: DbSet<Step4PreTidyMetadataEntity>

    [<DefaultValue>]
    val mutable private tidyResults: DbSet<Step5TidyResultEntity>

    [<DefaultValue>]
    val mutable private postTidyMetadata: DbSet<Step6PostTidyMetadataEntity>

    [<DefaultValue>]
    val mutable private step6Contents: DbSet<Step6ContentEntity>

    [<DefaultValue>]
    val mutable private receipts: DbSet<Step6ReceiptEntity>

    [<DefaultValue>]
    val mutable private relatedPages: DbSet<Step6RelatedPageEntity>

    [<DefaultValue>]
    val mutable private analysisLogs: DbSet<AnalysisLogEntity>

    [<DefaultValue>]
    val mutable private step7Ingredients: DbSet<Step7IngredientEntity>

    [<DefaultValue>]
    val mutable private step7ReceiptIngredients: DbSet<Step7ReceiptIngredientEntity>

    member this.ScannedFiles
        with get () = this.scannedFiles
        and set v = this.scannedFiles <- v

    member this.RawPageContents
        with get () = this.rawPageContents
        and set v = this.rawPageContents <- v

    member this.ParsedUrls
        with get () = this.parsedUrls
        and set v = this.parsedUrls <- v

    member this.FinalNames
        with get () = this.finalNames
        and set v = this.finalNames <- v

    member this.PreTidyMetadata
        with get () = this.preTidyMetadata
        and set v = this.preTidyMetadata <- v

    member this.TidyResults
        with get () = this.tidyResults
        and set v = this.tidyResults <- v

    member this.PostTidyMetadata
        with get () = this.postTidyMetadata
        and set v = this.postTidyMetadata <- v

    member this.Step6Contents
        with get () = this.step6Contents
        and set v = this.step6Contents <- v

    member this.Receipts
        with get () = this.receipts
        and set v = this.receipts <- v

    member this.RelatedPages
        with get () = this.relatedPages
        and set v = this.relatedPages <- v

    member this.AnalysisLogs
        with get () = this.analysisLogs
        and set v = this.analysisLogs <- v

    member this.Step7Ingredients
        with get () = this.step7Ingredients
        and set v = this.step7Ingredients <- v

    member this.Step7ReceiptIngredients
        with get () = this.step7ReceiptIngredients
        and set v = this.step7ReceiptIngredients <- v

    override this.OnModelCreating(modelBuilder: ModelBuilder) =
        modelBuilder.Entity<Step1ScannedFileEntity>().ToTable("Step1ScannedFiles")
        |> ignore

        modelBuilder.Entity<Step2RawContentEntity>().ToTable("Step2RawContents")
        |> ignore

        modelBuilder.Entity<Step2ParsedUrlEntity>().ToTable("Step2ParsedUrls")
        |> ignore

        modelBuilder.Entity<Step3FinalNameEntity>().ToTable("Step3FinalNames") |> ignore

        modelBuilder
            .Entity<Step4PreTidyMetadataEntity>()
            .ToTable("Step4PreTidyMetadata")
        |> ignore

        modelBuilder.Entity<Step5TidyResultEntity>().ToTable("Step5TidyResults")
        |> ignore

        modelBuilder
            .Entity<Step6PostTidyMetadataEntity>()
            .ToTable("Step6PostTidyMetadata")
        |> ignore

        modelBuilder.Entity<Step6ContentEntity>().ToTable("Step6Contents") |> ignore

        modelBuilder.Entity<Step6ReceiptEntity>().ToTable("Step6Receipts") |> ignore

        modelBuilder.Entity<Step6RelatedPageEntity>().ToTable("Step6RelatedPages")
        |> ignore

        modelBuilder.Entity<AnalysisLogEntity>().ToTable("AnalysisLogs") |> ignore

        modelBuilder.Entity<Step7IngredientEntity>().ToTable("Step7Ingredients") |> ignore

        modelBuilder.Entity<Step7ReceiptIngredientEntity>().ToTable("Step7ReceiptIngredients")
        |> ignore

module ContextFactory =
    open Microsoft.EntityFrameworkCore

    /// Create a PipelineDbContext for a given SQLite file path.
    /// Calls EnsureCreated() to initialize tables.
    let create (databasePath: string) : PipelineDbContext =
        let optionsBuilder = DbContextOptionsBuilder<PipelineDbContext>()
        let connStr = sprintf "Data Source=%s" databasePath
        optionsBuilder.UseSqlite(connStr) |> ignore
        let ctx = new PipelineDbContext(optionsBuilder.Options)
        ctx.Database.EnsureCreated() |> ignore
        ctx
