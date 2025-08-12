using HtmlAgilityPack;
using Newtonsoft.Json;

namespace Morpheus.GenderScraper;

class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            args = ["stage1"];
        }

        var stage = args[0].ToLowerInvariant();
        
        switch (stage)
        {
            case "stage1":
                await Stage1.Do();
                break;
            default:
                Console.WriteLine($"Unknown stage: {stage}");
                Console.WriteLine("Available stages: stage1");
                break;
        }
    }
}