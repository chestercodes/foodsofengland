module SiteGenerator.Paths

open System
open System.IO
open System.Text
open System.Text.RegularExpressions
open SiteGenerator.Types

let private ascendFileSystemUntilRoot (startPath: string) =
    let mutable current = DirectoryInfo(Path.GetFullPath(startPath))

    while not (String.Equals(current.Name, "foodsofengland", StringComparison.OrdinalIgnoreCase)) do
        current <-
            match current.Parent with
            | null -> invalidOp "Could not find repository root."
            | parent -> parent

    current.FullName

let repoRoot =
    let envVar = Environment.GetEnvironmentVariable("REPO_ROOT")
    if not (String.IsNullOrWhiteSpace(envVar)) then
        Path.GetFullPath(envVar)
    else
        ascendFileSystemUntilRoot __SOURCE_DIRECTORY__

let defaultConfig =
    { SiteDataDir = Path.Combine(repoRoot, "site", "data")
      DownloaderOutput = Path.Combine(repoRoot, "old-site-downloader", "out")
      OutputDir = Path.Combine(repoRoot, "site", "wwwroot") }

let optionOfText (value: string) =
    if String.IsNullOrWhiteSpace(value) then None else Some(value.Trim())

let normalizeSitePath (path: string) =
    if String.IsNullOrWhiteSpace(path) then "" else path.Replace('\\', '/').TrimStart('/')

let normalizeDiskPath (path: string) =
    normalizeSitePath path |> fun value -> value.Replace('/', Path.DirectorySeparatorChar)

let ensureParentDirectory (filePath: string) =
    let directory = Path.GetDirectoryName(filePath)

    if not (String.IsNullOrWhiteSpace(directory)) then
        Directory.CreateDirectory(directory) |> ignore

let writeAllText (filePath: string) (content: string) =
    ensureParentDirectory filePath
    File.WriteAllText(filePath, content, UTF8Encoding(false))

let sitePathToOutputPath (config: RunConfig) (sitePath: string) =
    Path.Combine(config.OutputDir, normalizeDiskPath sitePath)

let relativeHref (fromSitePath: string) (toSitePath: string) =
    let fromDirectory =
        Path.Combine("root", normalizeDiskPath fromSitePath)
        |> Path.GetDirectoryName

    let targetPath = Path.Combine("root", normalizeDiskPath toSitePath)
    Path.GetRelativePath(fromDirectory, targetPath).Replace('\\', '/')

let slugify (value: string) =
    let lowered = value.Trim().ToLowerInvariant()
    let slug = Regex.Replace(lowered, "[^a-z0-9]+", "-").Trim('-')
    if String.IsNullOrWhiteSpace(slug) then "item" else slug

let titleFromPath (finalPath: string) =
    let baseName = Path.GetFileNameWithoutExtension(normalizeDiskPath finalPath)

    if String.IsNullOrWhiteSpace(baseName) then
        "Untitled"
    else
        Regex.Replace(baseName, "[-_]+", " ").Trim()