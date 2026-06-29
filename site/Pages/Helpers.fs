module SiteGenerator.Pages.Helpers

open System
open System.Text.RegularExpressions
open Feliz.ViewEngine
open SiteGenerator.Paths

let rawHtml (content: string) =
    Interop.createRawTextElement content

let stripHtmlTags (text: string) =
    Regex.Replace(text, "<[^>]*>", "")

let replaceContactEmails (htmlContent: string) =
    if String.IsNullOrWhiteSpace(htmlContent) then ""
    else
        let pattern = @"(?:-\s*email\s+|email\s+|contact\s+)?<a\s+[^>]*href=""mailto:[^""]*@foodsofengland\.co\.uk[^""]*""[^>]*>.*?</a>"
        let replacement = @"<a href=""https://github.com/chestercodes/foodsofengland/issues"">raise an issue on GitHub</a>"
        Regex.Replace(htmlContent, pattern, replacement, RegexOptions.IgnoreCase)

let replaceOriginalLinks (fromSitePath: string) (htmlContent: string) =
    if String.IsNullOrWhiteSpace(htmlContent) then ""
    else
        let isDonatePage = String.Equals(fromSitePath, "donate.html", StringComparison.OrdinalIgnoreCase)
        if isDonatePage then
            "<p>Donations are no longer being accepted. Thank you for your support of the Foods of England tribute project.</p>"
        else
            let html1 = replaceContactEmails htmlContent
            let relPath = relativeHref fromSitePath "index.html"
            let hrefPattern = @"href=([""'])https?://(?:www\.)?foodsofengland\.co\.uk(?:/index\.htm|/)?\1"
            let html2 = Regex.Replace(html1, hrefPattern, sprintf "href=$1%s$1" relPath, RegexOptions.IgnoreCase)
            
            // Clean up PayPal donation sections (centered donation texts and buttons)
            let donatePattern = @"<center>\s*(?:<big>|<small>)?\s*Your modest donation.*?</center>"
            let html3 = Regex.Replace(html2, donatePattern, "", RegexOptions.IgnoreCase ||| RegexOptions.Singleline)
            
            // Clean up standalone paypal donate button/anchors
            let paypalPattern = @"<a\s+[^>]*href=""https?://(?:www\.)?paypal\.com/donate[^""]*""[^>]*>.*?</a>"
            let html4 = Regex.Replace(html3, paypalPattern, "", RegexOptions.IgnoreCase ||| RegexOptions.Singleline)

            let html5 = 
                html4
                    .Replace("FoodsOfEngland.co.uk", "foodsofengland.uk")
                    .Replace("foodsofengland.co.uk", "foodsofengland.uk")
                    .Replace("Foods of England.co.uk", "foodsofengland.uk")
                    .Replace("FOODSOFENGLAND.CO.UK", "foodsofengland.uk")

            // Remove sitemap list item link to donate.html
            let donateLinkPattern = @"-\s*<a\s+[^>]*href=""donate\.html""[^>]*>.*?</a>\s*<br\s*/?>\s*"
            let html6 = Regex.Replace(html5, donateLinkPattern, "", RegexOptions.IgnoreCase ||| RegexOptions.Singleline)

            // Remove consecutive hr tags/breaks left behind
            let hrPattern = @"<hr>\s*<br>\s*<hr>"
            let html7 = Regex.Replace(html6, hrPattern, "<hr>", RegexOptions.IgnoreCase ||| RegexOptions.Singleline)
            
            html7

let getFirstLine (htmlContent: string) =
    if String.IsNullOrWhiteSpace(htmlContent) then ""
    else
        let cleanedHtml =
            (replaceContactEmails htmlContent)
                .Replace("FoodsOfEngland.co.uk", "FoodsOfEngland.uk")
                .Replace("foodsofengland.co.uk", "foodsofengland.uk")
                .Replace("Foods of England.co.uk", "Foods of England.uk")
                .Replace("FOODSOFENGLAND.CO.UK", "FOODSOFENGLAND.UK")
        let plainText = stripHtmlTags cleanedHtml
        let lines = plainText.Split([| '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
        let nonEmpty = lines |> Array.map (fun s -> s.Trim()) |> Array.filter (fun s -> s.Length > 0)
        let first = if nonEmpty.Length > 0 then nonEmpty.[0] else ""
        if first.Length > 160 then
            first.Substring(0, 160) + "..."
        else
            first
