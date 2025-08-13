using System.Net.Http;
using System.Web;
using HtmlAgilityPack;
using Newtonsoft.Json;

namespace Morpheus.Scraper;

internal static class Program
{
    private static readonly Uri BaseUri = new("https://www.sklonovani-jmen.cz/");

    public static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            args =
            [
                "stage4"
            ];
        }

        var stage = args[0].ToLowerInvariant();
        
                    switch (stage)
            {
                case "stage1":
                    await Stage1.Do();
                    break;
                case "stage2":
                    await Stage2.Do();
                    break;
                case "stage3":
                    await Stage3.Do();
                    break;
                case "stage4":
                    await Stage4.Do();
                    break;

                default:
                    Console.WriteLine($"Unknown stage: {stage}");
                    Console.WriteLine("Available stages: stage1, stage2, stage3, stage4");
                    break;
            }
    }
}