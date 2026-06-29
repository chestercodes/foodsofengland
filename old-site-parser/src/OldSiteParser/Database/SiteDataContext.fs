namespace OldSiteParser.Database

open Microsoft.EntityFrameworkCore

type SiteDataContext(options: DbContextOptions<SiteDataContext>) =
    inherit DbContext(options)

    [<DefaultValue>]
    val mutable private nonHtmlFiles: DbSet<SiteDataNonHtmlFileEntity>

    [<DefaultValue>]
    val mutable private posts: DbSet<SiteDataPostEntity>

    [<DefaultValue>]
    val mutable private postTags: DbSet<SiteDataPostTagEntity>

    [<DefaultValue>]
    val mutable private relatedPages: DbSet<SiteDataRelatedPageEntity>

    [<DefaultValue>]
    val mutable private postIngredients: DbSet<SiteDataPostIngredientEntity>

    [<DefaultValue>]
    val mutable private postReceipts: DbSet<SiteDataPostReceiptEntity>

    member this.NonHtmlFiles
        with get () = this.nonHtmlFiles
        and set v = this.nonHtmlFiles <- v

    member this.Posts
        with get () = this.posts
        and set v = this.posts <- v

    member this.PostTags
        with get () = this.postTags
        and set v = this.postTags <- v

    member this.RelatedPages
        with get () = this.relatedPages
        and set v = this.relatedPages <- v

    member this.PostIngredients
        with get () = this.postIngredients
        and set v = this.postIngredients <- v

    member this.PostReceipts
        with get () = this.postReceipts
        and set v = this.postReceipts <- v

    override _.OnModelCreating(modelBuilder: ModelBuilder) =
        modelBuilder.Entity<SiteDataNonHtmlFileEntity>().ToTable("NonHtmlFiles") |> ignore
        modelBuilder.Entity<SiteDataPostEntity>().ToTable("Posts") |> ignore
        modelBuilder.Entity<SiteDataPostTagEntity>().ToTable("PostTags") |> ignore
        modelBuilder.Entity<SiteDataRelatedPageEntity>().ToTable("RelatedPages") |> ignore
        modelBuilder.Entity<SiteDataPostIngredientEntity>().ToTable("PostIngredients") |> ignore
        modelBuilder.Entity<SiteDataPostReceiptEntity>().ToTable("PostReceipts") |> ignore

module SiteDataContextFactory =
    let create (databasePath: string) : SiteDataContext =
        let optionsBuilder = DbContextOptionsBuilder<SiteDataContext>()
        let connStr = sprintf "Data Source=%s" databasePath
        optionsBuilder.UseSqlite(connStr) |> ignore
        let ctx = new SiteDataContext(optionsBuilder.Options)
        ctx.Database.EnsureCreated() |> ignore
        ctx