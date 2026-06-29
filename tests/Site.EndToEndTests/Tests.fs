namespace Site.EndToEndTests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open Xunit
open HtmlAgilityPack

type E2ETests() =
    static let mutable initialized = false
    static let initLock = obj()

    let getBaseUrl () =
        let envUrl = Environment.GetEnvironmentVariable("BASE_URL")
        if String.IsNullOrWhiteSpace(envUrl) then
            "http://site:5000"
        else
            envUrl.TrimEnd('/')

    let client = new HttpClient()

    let ensureHealthy () =
        lock initLock (fun () ->
            if not initialized then
                let baseUrl = getBaseUrl()
                let checkUrl = sprintf "%s/index.html" baseUrl
                let mutable success = false
                let mutable attempts = 0
                let maxAttempts = 30
                use tempClient = new HttpClient()
                
                printfn "Waiting for site to become healthy at: %s" checkUrl
                while not success && attempts < maxAttempts do
                    try
                        attempts <- attempts + 1
                        let response = tempClient.GetAsync(checkUrl).Result
                        if response.StatusCode = HttpStatusCode.OK then
                            success <- true
                        else
                            System.Threading.Thread.Sleep(1000)
                    with _ ->
                        System.Threading.Thread.Sleep(1000)
                
                if not success then
                    failwithf "Site was not healthy after %d attempts at %s" maxAttempts checkUrl
                
                initialized <- true
        )

    [<Fact>]
    member _.``Index page renders correctly and does not contain kicker`` () =
        ensureHealthy()
        let baseUrl = getBaseUrl()
        let url = sprintf "%s/index.html" baseUrl
        
        let response = client.GetAsync(url).Result
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        
        let html = response.Content.ReadAsStringAsync().Result
        let doc = HtmlDocument()
        doc.LoadHtml(html)
        
        let titleNode = doc.DocumentNode.SelectSingleNode("//title")
        Assert.NotNull(titleNode)
        Assert.Contains("Foods of England", titleNode.InnerText)
        
        let kickerNodes = doc.DocumentNode.SelectNodes("//*[contains(@class, 'hero__kicker')]")
        Assert.Null(kickerNodes)

    [<Fact>]
    member _.``About page renders correctly and does not contain kicker`` () =
        ensureHealthy()
        let baseUrl = getBaseUrl()
        let url = sprintf "%s/about.html" baseUrl
        
        let response = client.GetAsync(url).Result
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        
        let html = response.Content.ReadAsStringAsync().Result
        let doc = HtmlDocument()
        doc.LoadHtml(html)
        
        let titleNode = doc.DocumentNode.SelectSingleNode("//title")
        Assert.NotNull(titleNode)
        Assert.Contains("About - Foods of England", titleNode.InnerText)
        
        let kickerNodes = doc.DocumentNode.SelectNodes("//*[contains(@class, 'hero__kicker')]")
        Assert.Null(kickerNodes)

    [<Fact>]
    member _.``404 page works and does not contain kicker`` () =
        ensureHealthy()
        let baseUrl = getBaseUrl()
        let url = sprintf "%s/this-page-does-not-exist.html" baseUrl
        
        let response = client.GetAsync(url).Result
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode)
        
        let html = response.Content.ReadAsStringAsync().Result
        let doc = HtmlDocument()
        doc.LoadHtml(html)
        
        let titleNode = doc.DocumentNode.SelectSingleNode("//title")
        Assert.NotNull(titleNode)
        Assert.Contains("Page Not Found", titleNode.InnerText)
        
        let kickerNodes = doc.DocumentNode.SelectNodes("//*[contains(@class, 'hero__kicker')]")
        Assert.Null(kickerNodes)

    [<Fact>]
    member _.``Random page endpoint redirects to an existing post`` () =
        ensureHealthy()
        let baseUrl = getBaseUrl()
        let url = sprintf "%s/random" baseUrl
        
        let handler = new HttpClientHandler(AllowAutoRedirect = false)
        using (new HttpClient(handler)) (fun redirectClient ->
            let response = redirectClient.GetAsync(url).Result
            Assert.True(
                response.StatusCode = HttpStatusCode.Redirect || 
                response.StatusCode = HttpStatusCode.MovedPermanently || 
                response.StatusCode = HttpStatusCode.Found || 
                response.StatusCode = HttpStatusCode.TemporaryRedirect,
                sprintf "Expected redirect status code, but got %O" response.StatusCode
            )
            
            let location = response.Headers.Location
            Assert.NotNull(location)
            
            let redirectUrl = 
                if location.IsAbsoluteUri then 
                    location.ToString() 
                else 
                    sprintf "%s/%s" (baseUrl.TrimEnd('/')) (location.ToString().TrimStart('/'))
            
            let pageResponse = client.GetAsync(redirectUrl).Result
            Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode)
            
            let html = pageResponse.Content.ReadAsStringAsync().Result
            let doc = HtmlDocument()
            doc.LoadHtml(html)
            
            let kickerNodes = doc.DocumentNode.SelectNodes("//*[contains(@class, 'hero__kicker')]")
            Assert.Null(kickerNodes)
        )

    [<Fact>]
    member _.``Search endpoint returns valid JSON results`` () =
        ensureHealthy()
        let baseUrl = getBaseUrl()
        let query = "pudding"
        let url = sprintf "%s/api/search?q=%s" baseUrl query
        
        let response = client.GetAsync(url).Result
        Assert.Equal(HttpStatusCode.OK, response.StatusCode)
        
        let json = response.Content.ReadAsStringAsync().Result
        let results = JsonSerializer.Deserialize<{| title: string; finalPath: string |} array>(json)
        Assert.NotNull(results)
        Assert.NotEmpty(results)
        
        let firstResult = results.[0]
        Assert.False(String.IsNullOrWhiteSpace(firstResult.title))
        Assert.False(String.IsNullOrWhiteSpace(firstResult.finalPath))
        
        let shortUrl = sprintf "%s/api/search?q=a" baseUrl
        let shortResponse = client.GetAsync(shortUrl).Result
        Assert.Equal(HttpStatusCode.OK, shortResponse.StatusCode)
        let shortJson = shortResponse.Content.ReadAsStringAsync().Result
        let shortResults = JsonSerializer.Deserialize<{| title: string; finalPath: string |} array>(shortJson)
        Assert.Empty(shortResults)
