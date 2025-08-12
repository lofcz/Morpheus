using System.Collections.Concurrent;
using System.Net;
using System.Web;
using HtmlAgilityPack;
using Newtonsoft.Json;

namespace Morpheus.Scraper;

class IpObject
{
    public string Ip { get; set; } = string.Empty;
}

public static class Stage2
{
    private static readonly Uri BaseUri = new("https://www.sklonovani-jmen.cz/");
    private static ConcurrentDictionary<string, int> ipMap = [];
    private const int MaxIpUsageCount = 5;
    
    public static async Task Do()
    {
        // Load links from Stage 1
        var linksPath = Path.Combine(AppContext.BaseDirectory, "stage1-links.json");
        if (!File.Exists(linksPath))
        {
            Console.WriteLine($"Stage 1 links file not found: {linksPath}");
            Console.WriteLine("Please run Stage 1 first.");
            return;
        }

        var linksJson = await File.ReadAllTextAsync(linksPath);
        var links = JsonConvert.DeserializeObject<List<string>>(linksJson) ?? new List<string>();
        Console.WriteLine($"Loaded {links.Count} links from Stage 1");

        var results = new List<NameData>();
        var checkpointPath = Path.Combine(AppContext.BaseDirectory, "stage2-checkpoint.json");
        var outputPath = Path.Combine(AppContext.BaseDirectory, "stage2-results.json");

        // Load checkpoint if exists and create set of processed URLs
        var processedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(checkpointPath))
        {
            var checkpointJson = await File.ReadAllTextAsync(checkpointPath);
            var checkpoint = JsonConvert.DeserializeObject<List<NameData>>(checkpointJson) ?? new List<NameData>();
            results.AddRange(checkpoint);
            
            // Track which URLs were already successfully processed
            foreach (var nameData in checkpoint)
            {
                if (!string.IsNullOrEmpty(nameData.Url))
                {
                    processedUrls.Add(nameData.Url);
                }
            }
            
            Console.WriteLine($"Resumed from checkpoint: {results.Count} already processed");
            Console.WriteLine($"Found {processedUrls.Count} unique processed URLs");
        }

        // Filter out already processed links
        var remainingLinks = links.Where(link => !processedUrls.Contains(link)).ToList();
        Console.WriteLine($"Total links: {links.Count}, Already processed: {processedUrls.Count}, Remaining: {remainingLinks.Count}");

        var semaphore = new SemaphoreSlim(1, 1); // For maxDegreeOfParallelism = 1
        var processed = results.Count; // Start from current checkpoint count

        await Parallel.ForEachAsync(
            remainingLinks,
            new ParallelOptions { MaxDegreeOfParallelism = 1 },
            async (link, cancellationToken) =>
            {
                try
                {
                    HttpClient? client = null;
                    int retryCount = 0;
                    const int maxRetries = 10;

                    // Retry until we get a unique IP
                    do
                    {
                        client?.Dispose(); // Dispose previous client if retry
                        client = CreateHttpClientWithProxy();
                        client.DefaultRequestHeaders.Add("User-Agent", Ua.GetRandomUserAgent());

                        // Check current IP
                        var test = await client.GetAsync("https://api.ipify.org/?format=json", cancellationToken);
                        string str = await test.Content.ReadAsStringAsync(cancellationToken);
                        
                                                IpObject? ipObj = JsonConvert.DeserializeObject<IpObject>(str);
                        string currentIp = ipObj?.Ip ?? "unknown";
                        
                        // Try to increment usage count for this IP
                        var currentUsage = ipMap.AddOrUpdate(currentIp, 1, (key, existingValue) => 
                        {
                            return Interlocked.Increment(ref existingValue);
                        });
                        
                        if (currentUsage <= MaxIpUsageCount)
                        {
                            Console.WriteLine($"Using IP: {currentIp} (usage: {currentUsage}/{MaxIpUsageCount})");
                            break; // Success - IP is within usage limit
                        }
                        else
                        {
                            // Revert the increment since we're not using this IP
                            ipMap.AddOrUpdate(currentIp, 0, (key, existingValue) => 
                            {
                                return Interlocked.Decrement(ref existingValue);
                            });
                        }

                        retryCount++;
                        Console.WriteLine($"IP {currentIp} over usage limit ({currentUsage}), retrying... ({retryCount}/{maxRetries})");
                        
                        if (retryCount >= maxRetries)
                        {
                            Console.WriteLine($"Max retries reached, proceeding with over-used IP: {currentIp}");
                            // Re-increment since we're going to use it anyway
                            ipMap.AddOrUpdate(currentIp, 1, (key, existingValue) => 
                            {
                                return Interlocked.Increment(ref existingValue);
                            });
                            break;
                        }
                        
                        await Task.Delay(1000, cancellationToken); // Wait before retry
                    } while (retryCount < maxRetries);
                    
                    NameData? nameData = null;
                    if (client != null)
                    {
                        nameData = await ScrapeNameData(client, link);
                        client.Dispose(); // Clean up client
                    }
                    
                    if (nameData != null)
                    {
                        await semaphore.WaitAsync(cancellationToken);
                        try
                        {
                            results.Add(nameData);
                            processed++;
                            
                            // Log progress
                            if (processed % 10 == 0)
                            {
                                var totalExpected = processedUrls.Count + remainingLinks.Count;
                                Console.WriteLine($"Processed {processed}/{totalExpected} ({processed * 100.0 / totalExpected:F1}%)");
                            }

                            // Checkpoint every 50 items
                            if (processed % 50 == 0)
                            {
                                await File.WriteAllTextAsync(checkpointPath, 
                                    JsonConvert.SerializeObject(results, Formatting.Indented), cancellationToken);
                                Console.WriteLine($"Checkpoint saved at {processed} items");
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }

                    // Be polite to the server
                    await Task.Delay(Random.Shared.Next(1, 10), cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing {link}: {ex.Message}");
                }
            });

        // Final save
        await File.WriteAllTextAsync(outputPath, JsonConvert.SerializeObject(results, Formatting.Indented));
        Console.WriteLine($"Stage 2 completed! Saved {results.Count} names to {outputPath}");

        // Display IP usage statistics
        Console.WriteLine("\nIP Usage Statistics:");
        foreach (var kvp in ipMap.OrderBy(x => x.Key))
        {
            Console.WriteLine($"  {kvp.Key}: {kvp.Value} uses");
        }
        Console.WriteLine($"Total unique IPs used: {ipMap.Count}");

        // Clean up checkpoint file
        if (File.Exists(checkpointPath))
        {
            File.Delete(checkpointPath);
        }
    }

    private static HttpClient CreateHttpClientWithProxy()
    {
        var proxyHost = "cz-pr.oxylabs.io"; // Environment.GetEnvironmentVariable("MF_PROXY_HOST", EnvironmentVariableTarget.User) ?? "pr.oxylabs.io";
        var proxyPort = "18000"; // int.Parse(Environment.GetEnvironmentVariable("MF_PROXY_PORT", EnvironmentVariableTarget.User) ?? "7777");
        var proxyUser = Environment.GetEnvironmentVariable("MF_PROXY_USER", EnvironmentVariableTarget.User) ?? "customer-USERNAME";
        var proxyPassword = Environment.GetEnvironmentVariable("MF_PROXY_PASSWORD", EnvironmentVariableTarget.User) ?? "PASSWORD";
        
        var proxy = new WebProxy($"https://{proxyHost}:{proxyPort}")
        {
            Credentials = new NetworkCredential(proxyUser, proxyPassword)
        };

        var handler = new HttpClientHandler()
        {
            Proxy = proxy,
            UseProxy = true
        };

        var client = new HttpClient(handler)
        {
            DefaultRequestVersion = HttpVersion.Version20
        };
        return client;
    }

    private static async Task<NameData?> ScrapeNameData(HttpClient client, string url)
    {
        try
        {
            var html = await client.GetStringAsync(url);
            var doc = new HtmlDocument { OptionFixNestedTags = true };
            doc.LoadHtml(html);

            var nameData = new NameData { Url = url };

            // Extract name from URL (e.g., "jmeno-Šimečková" -> "Šimečková")
            var urlParts = url.Split('/');
            var lastPart = urlParts[^1];
            if (lastPart.StartsWith("jmeno-"))
            {
                nameData.Name = HttpUtility.UrlDecode(lastPart.Substring(6));
            }

            // Extract name from H1 tag as backup
            var h1 = doc.DocumentNode.SelectSingleNode("//h1");
            if (h1 != null && string.IsNullOrEmpty(nameData.Name))
            {
                var h1Text = h1.InnerText.Trim();
                var match = System.Text.RegularExpressions.Regex.Match(h1Text, @"Jméno\s+(.+)");
                if (match.Success)
                {
                    nameData.Name = match.Groups[1].Value.Trim();
                }
            }

            // Scrape all declension tables (there can be multiple for different forms)
            nameData.Declensions = ScrapeAllDeclensionTables(doc);

            // Scrape possessive forms table (if present)
            nameData.PossessiveForms = ScrapePossessiveTable(doc);

            // Scrape family naming table (if present)
            nameData.FamilyNaming = ScrapeFamilyNamingTable(doc);

            return nameData;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to scrape {url}: {ex.Message}");
            return null;
        }
    }

    private static List<DeclensionVariant> ScrapeAllDeclensionTables(HtmlDocument doc)
    {
        var variants = new List<DeclensionVariant>();
        
        // Find all tables with "1. pád" that have the right class
        var tables = doc.DocumentNode.SelectNodes("//table[contains(@class, 'tvary_jmena') and .//td[contains(text(), '1. pád')]]");
        if (tables == null) return variants;

        foreach (var table in tables)
        {
            // Find the h2 element that precedes this table
            var h2 = FindPrecedingH2(table);
            if (h2 == null) continue;

            var h2Text = h2.InnerText.Trim();
            var boldText = h2.SelectSingleNode(".//b")?.InnerText.Trim() ?? "";

            // Parse the variant information from the bold text
            var variant = new DeclensionVariant
            {
                VariantDescription = boldText,
                Gender = ParseGender(boldText),
                Type = ParseNameType(boldText),
                Declension = ScrapeSingleDeclensionTable(table)
            };

            variants.Add(variant);
        }

        return variants;
    }

    private static NameGender ParseGender(string boldText)
    {
        if (boldText.Contains("mužského"))
            return NameGender.Masculine;
        else if (boldText.Contains("ženského"))
            return NameGender.Feminine;
        else
            return NameGender.Other;
    }

    private static NameType ParseNameType(string boldText)
    {
        if (boldText.Contains("příjmení"))
            return NameType.LastName;
        else
            return NameType.FirstName;
    }

    private static HtmlNode? FindPrecedingH2(HtmlNode table)
    {
        // First try sibling navigation
        var current = table.PreviousSibling;
        while (current != null)
        {
            if (current.Name == "h2")
                return current;
            current = current.PreviousSibling;
        }
        
        // If not found, use XPath to find preceding h2 in document order
        var h2s = table.OwnerDocument.DocumentNode.SelectNodes("//h2[contains(text(), 'Skloňování')]");
        if (h2s == null) return null;
        
        // Find the closest h2 that appears before this table in the document
        HtmlNode? closestH2 = null;
        foreach (var h2 in h2s)
        {
            if (IsNodeBefore(h2, table))
            {
                closestH2 = h2;
            }
            else
            {
                break; // h2 comes after table, so we're done
            }
        }
        
        return closestH2;
    }
    
    private static bool IsNodeBefore(HtmlNode node1, HtmlNode node2)
    {
        // Simple way to check document order by comparing line positions
        return node1.Line < node2.Line || 
               (node1.Line == node2.Line && node1.LinePosition < node2.LinePosition);
    }

    private static DeclensionTable ScrapeSingleDeclensionTable(HtmlNode table)
    {
        var declension = new DeclensionTable();
        var rows = table.SelectNodes(".//tr[td]");
        if (rows == null) return declension;

        foreach (var row in rows)
        {
            var cells = row.SelectNodes("./td");
            if (cells?.Count >= 3)
            {
                var caseNumber = cells[0].InnerText.Trim();
                var question = cells[1].InnerText.Trim();
                var form = cells[2].InnerText.Trim();

                var caseInfo = new CaseInfo
                {
                    // these two are not needed, bloats the json
                    //CaseNumber = caseNumber,
                    //Question = question,
                    Form = form
                };

                switch (caseNumber)
                {
                    case "1. pád": declension.Nominative = caseInfo; break;
                    case "2. pád": declension.Genitive = caseInfo; break;
                    case "3. pád": declension.Dative = caseInfo; break;
                    case "4. pád": declension.Accusative = caseInfo; break;
                    case "5. pád": declension.Vocative = caseInfo; break;
                    case "6. pád": declension.Locative = caseInfo; break;
                    case "7. pád": declension.Instrumental = caseInfo; break;
                }
            }
        }

        // Look for plural form in the same table
        var pluralRow = table.SelectSingleNode(".//tr[contains(@class, 'mnozne_cislo') or .//td[contains(text(), 'Množné číslo')]]");
        if (pluralRow != null)
        {
            var pluralCell = pluralRow.SelectSingleNode(".//td[last()]");
            if (pluralCell != null)
            {
                declension.Plural = pluralCell.InnerText.Trim();
            }
        }

        return declension;
    }

    private static PossessiveFormsTable? ScrapePossessiveTable(HtmlDocument doc)
    {
        // Look for table with "Přivlastňovací tvary" heading
        var heading = doc.DocumentNode.SelectSingleNode("//h3[contains(text(), 'Přivlastňovací tvary')]");
        if (heading == null) return null;

        var table = heading.SelectSingleNode("following-sibling::table[1]");
        if (table == null) return null;

        var possessive = new PossessiveFormsTable();
        var rows = table.SelectNodes(".//tr[td]");
        if (rows == null) return null;

                 foreach (var row in rows)
         {
             var cells = row.SelectNodes("./td");
             if (cells?.Count >= 3)
             {
                 var gender = cells[0].InnerText.Trim();
                 
                 // Extract only content within <b> tags for singular and plural
                 var singularB = cells[1].SelectSingleNode(".//b");
                 var pluralB = cells[2].SelectSingleNode(".//b");
                 
                 var singular = singularB?.InnerText.Trim() ?? "";
                 var plural = pluralB?.InnerText.Trim() ?? "";

                 var form = new PossessiveForm
                 {
                     Gender = gender,
                     Singular = singular,
                     Plural = plural
                 };

                switch (gender)
                {
                    case "Mužský životný": possessive.MasculineAnimate = form; break;
                    case "Mužský neživotný": possessive.MasculineInanimate = form; break;
                    case "Ženský": possessive.Feminine = form; break;
                    case "Střední": possessive.Neuter = form; break;
                }
            }
        }

        return possessive;
    }

    private static FamilyNamingTable? ScrapeFamilyNamingTable(HtmlDocument doc)
    {
        // Look for table with class "rodina_jmena" or find by heading
        var table = doc.DocumentNode.SelectSingleNode("//table[contains(@class, 'rodina_jmena')]");
        
        // Fallback: look for table with "Pojmenování rodiny" heading
        if (table == null)
        {
            var heading = doc.DocumentNode.SelectSingleNode("//h2[contains(text(), 'Pojmenování') and contains(text(), 'rodiny')]");
            if (heading != null)
            {
                table = heading.SelectSingleNode("following-sibling::table[1]");
            }
        }
        
        if (table == null) return null;

        var family = new FamilyNamingTable();
        var rows = table.SelectNodes(".//tr[td]");
        if (rows == null) return null;

        foreach (var row in rows)
        {
            var cells = row.SelectNodes("./td");
            if (cells?.Count >= 2)
            {
                var description = cells[0].InnerText.Trim();
                
                // Extract content from <b> tags in the second cell
                var formB = cells[1].SelectSingleNode(".//b");
                string form;
                if (formB != null)
                {
                    // Get all text nodes and <u> content within <b>, but clean up formatting
                    form = formB.InnerText.Trim();
                    // Clean up extra whitespace that might come from nested elements
                    form = System.Text.RegularExpressions.Regex.Replace(form, @"\s+", " ");
                }
                else
                {
                    form = cells[1].InnerText.Trim();
                }

                if (description.Contains("Pan a paní") || description.Contains("manželé"))
                {
                    family.Couple = form;
                }
                else if (description.Contains("Rodina"))
                {
                    family.Family = form;
                }
                else if (description.Contains("Děti") || description.Contains("sestry"))
                {
                    family.Children = form;
                }
            }
        }

        return family;
    }
}

// Data structures
public class NameData
{
    public string? Name { get; set; }
    public string? Url { get; set; }
    public List<DeclensionVariant> Declensions { get; set; } = new();
    public PossessiveFormsTable? PossessiveForms { get; set; }
    public FamilyNamingTable? FamilyNaming { get; set; }
}

public class DeclensionVariant
{
    public DeclensionTable? Declension { get; set; }
    public NameGender Gender { get; set; }
    public NameType Type { get; set; }
    public string? VariantDescription { get; set; }  // Full text from h2 like "ženského křestního jména"
}

public enum NameGender
{
    Masculine,
    Feminine,
    Other
}

public enum NameType
{
    FirstName,
    LastName
}

public class DeclensionTable
{
    public CaseInfo? Nominative { get; set; }      // 1. pád
    public CaseInfo? Genitive { get; set; }        // 2. pád
    public CaseInfo? Dative { get; set; }          // 3. pád
    public CaseInfo? Accusative { get; set; }      // 4. pád
    public CaseInfo? Vocative { get; set; }        // 5. pád
    public CaseInfo? Locative { get; set; }        // 6. pád
    public CaseInfo? Instrumental { get; set; }    // 7. pád
    public string? Plural { get; set; }            // Množné číslo
}

public class CaseInfo
{
    public string? Form { get; set; }
}

public class PossessiveFormsTable
{
    public PossessiveForm? MasculineAnimate { get; set; }    // Mužský životný
    public PossessiveForm? MasculineInanimate { get; set; }  // Mužský neživotný
    public PossessiveForm? Feminine { get; set; }            // Ženský
    public PossessiveForm? Neuter { get; set; }              // Střední
}

public class PossessiveForm
{
    public string? Gender { get; set; }
    public string? Singular { get; set; }
    public string? Plural { get; set; }
}

public class FamilyNamingTable
{
    public string? Couple { get; set; }    // Pan a paní, manželé
    public string? Family { get; set; }    // Rodina
    public string? Children { get; set; }  // Děti, sestry
}
