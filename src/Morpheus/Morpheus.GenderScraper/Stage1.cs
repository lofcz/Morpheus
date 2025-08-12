using HtmlAgilityPack;
using Newtonsoft.Json;

namespace Morpheus.GenderScraper;

public static class Stage1
{
    private const string BaseUrl = "https://www.emimino.cz/seznam-jmen/strankovani/{0}/#list-names";
    private const int StartPage = 1;
    private const int EndPage = 213;
    private const string TargetUlClasses = "list-style-none ml0 columns columns--2 columns--md-3 fs-18";
    
    public static async Task Do()
    {
        Console.WriteLine("=== Stage 1: Scraping Name URLs from emimino.cz ===");
        Console.WriteLine($"Processing pages {StartPage} to {EndPage}");
        
        var allUrls = new HashSet<string>();
        var httpClient = new HttpClient();
        
        // Set a proper User-Agent to avoid being blocked
        httpClient.DefaultRequestHeaders.Add("User-Agent", 
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        try
        {
            for (int page = StartPage; page <= EndPage; page++)
            {
                await ProcessPage(httpClient, page, allUrls);
                
                // Progress logging every 10 pages
                if (page % 10 == 0 || page == EndPage)
                {
                    Console.WriteLine($"Progress: {page}/{EndPage} pages processed, {allUrls.Count} URLs collected");
                }
                
                // Be polite to the server
                await Task.Delay(Random.Shared.Next(1, 10));
            }
        }
        finally
        {
            httpClient.Dispose();
        }
        
        // Save results to JSON
        var resultsList = allUrls.OrderBy(url => url).ToList();
        var json = JsonConvert.SerializeObject(resultsList, Formatting.Indented);
        await File.WriteAllTextAsync("stage1.json", json);
        
        Console.WriteLine($"=== Stage 1 Complete ===");
        Console.WriteLine($"Total unique URLs collected: {resultsList.Count}");
        Console.WriteLine($"Results saved to: stage1.json");
    }
    
    private static async Task ProcessPage(HttpClient httpClient, int pageNumber, HashSet<string> allUrls)
    {
        var url = string.Format(BaseUrl, pageNumber);
        
        try
        {
            Console.WriteLine($"Processing page {pageNumber}: {url}");
            
            var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            
            var html = await response.Content.ReadAsStringAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            
            // Find the target UL element with the specific classes
            var targetUl = doc.DocumentNode
                .SelectSingleNode($"//ul[@class='{TargetUlClasses}']");
            
            if (targetUl == null)
            {
                Console.WriteLine($"  Warning: Could not find target UL with classes '{TargetUlClasses}' on page {pageNumber}");
                return;
            }
            
            // Extract all links from LI elements within this UL
            var links = targetUl.SelectNodes(".//li//a[@href]");
            
            if (links == null || links.Count == 0)
            {
                Console.WriteLine($"  Warning: No links found in target UL on page {pageNumber}");
                return;
            }
            
            int pageUrlCount = 0;
            foreach (var link in links)
            {
                var href = link.GetAttributeValue("href", string.Empty);
                
                if (string.IsNullOrWhiteSpace(href))
                    continue;
                
                // Ensure we have absolute URLs
                if (!href.StartsWith("http"))
                {
                    if (href.StartsWith("/"))
                        href = "https://www.emimino.cz" + href;
                    else
                        href = "https://www.emimino.cz/" + href;
                }
                
                // Only collect URLs that point to name detail pages
                if (href.Contains("/seznam-jmen/detail/"))
                {
                    if (allUrls.Add(href))
                    {
                        pageUrlCount++;
                    }
                }
            }
            
            Console.WriteLine($"  Found {pageUrlCount} new name URLs on page {pageNumber}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Error processing page {pageNumber}: {ex.Message}");
        }
    }
}
