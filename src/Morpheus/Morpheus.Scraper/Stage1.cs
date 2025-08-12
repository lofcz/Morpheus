using System.Web;
using HtmlAgilityPack;
using Newtonsoft.Json;

namespace Morpheus.Scraper;

public static class Stage1
{
    private static readonly Uri BaseUri = new("https://www.sklonovani-jmen.cz/");
    
    public static async Task Do()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "cs-CZ,cs;q=0.9,en;q=0.8");

        var allLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int page = 1; page <= 19; page++)
        {
            var url = new Uri(BaseUri, $"jmena?strana={page}");
            var html = await client.GetStringAsync(url);
            var doc = new HtmlDocument { OptionFixNestedTags = true };
            doc.LoadHtml(html);

            // Get all anchor tags and filter for name links
            var anchors = doc.DocumentNode.SelectNodes("//a[@href]")
                          ?? Enumerable.Empty<HtmlNode>();
            int countBefore = allLinks.Count;
            foreach (var a in anchors)
            {
                var href = a.GetAttributeValue("href", string.Empty);
                if (string.IsNullOrWhiteSpace(href) || !href.StartsWith("jmeno-")) continue;
                
                // Decode URL-encoded characters and construct proper URL
                var decodedHref = HttpUtility.UrlDecode(href);
                var absolute = new Uri(BaseUri, decodedHref).ToString();
                
                // Skip malformed URLs
                if (absolute.Contains("@") || absolute.Contains("/u")) continue;
                
                allLinks.Add(absolute);
            }
            Console.WriteLine($"Page {page}: +{allLinks.Count - countBefore} links");
            await Task.Delay(150); // be polite
        }

        var sortedLinks = allLinks.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();

        var outPath = Path.Combine(AppContext.BaseDirectory, "stage1-links.json");
        await File.WriteAllTextAsync(outPath, JsonConvert.SerializeObject(sortedLinks, Formatting.Indented));
        Console.WriteLine($"Saved {sortedLinks.Count} links to {outPath}");
    }
}