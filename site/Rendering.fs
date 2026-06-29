module SiteGenerator.Rendering

open System.IO
open SiteGenerator.Paths

// Expose rendering functions from Page modules
let renderPage = SiteGenerator.Pages.PageShell.renderPage
let renderIndexPage = SiteGenerator.Pages.IndexPage.renderIndexPage
let renderAboutPage = SiteGenerator.Pages.AboutPage.renderAboutPage
let renderPostPage = SiteGenerator.Pages.PostPage.renderPostPage
let renderTagPages = SiteGenerator.Pages.TagPages.renderTagPages
let renderIngredientPage = SiteGenerator.Pages.IngredientPage.renderIngredientPage
let renderCategoriesPage = SiteGenerator.Pages.CategoriesPage.renderCategoriesPage
let render404Page = SiteGenerator.Pages.NotFoundPage.render404Page

// Expose CSS and JS assets by reading their separate files from Pages/ directory
let siteCss = File.ReadAllText(Path.Combine(repoRoot, "site", "Pages", "site.css"))
let searchJs = File.ReadAllText(Path.Combine(repoRoot, "site", "Pages", "search.js"))
let ingredientsJs = File.ReadAllText(Path.Combine(repoRoot, "site", "Pages", "ingredients.js"))
let categoriesJs = File.ReadAllText(Path.Combine(repoRoot, "site", "Pages", "categories.js"))