using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using LlmTornado;
using LlmTornado.Chat;
using LlmTornado.Chat.Models;
using LlmTornado.Code;

namespace RuleGenerator
{
    enum Gender
    {
        [Description("nelze uričt / obojetné")]
        Androgynous,
        [Description("muž")]
        Male,
        [Description("žena")]
        Female
    }
    
    class Declinated
    {
        [Description("První pád (zadání)")]
        public string Nominative { get; set; }
        [Description("Pátý pád (výstup)")]
        public string Vocative { get; set; }
        [Description("pohlaví")]
        public Gender Gender { get; set; }
    }
    
    class Program
    {
        static async Task Main(string[] args)
        {
            string[] names = await File.ReadAllLinesAsync("names_cz.txt");
            string apiKey = await File.ReadAllTextAsync("apiKey.txt");
            
            List<string[]> chunks = names.Chunk(100).ToList();

            await Parallel.ForEachAsync(chunks, new ParallelOptions { MaxDegreeOfParallelism = 1 }, async (chunk, token) =>
            {
                ChatRichResponse response = await new TornadoApi([
                    new ProviderAuthentication(LLmProviders.OpenAi, apiKey)
                ]).Chat.CreateConversation(new ChatRequest
                {
                    Model = ChatModel.OpenAi.Gpt41.V41,
                    Messages = [
                        new ChatMessage(ChatMessageRoles.System, "Převeď zadaná jména z prvního pádu (nominativ) do pátého pádu (vokativ), pokud zadání obsahuje i záznamy, které nejsou jména, vynechej je, pokud zadané jméno obsahuje zjevný překlep, oprav jej ve vyskloňované formě, ale neprováděj změny, pokud není jednoznačné, že se jedná o chybu."),
                        new ChatMessage(ChatMessageRoles.User, string.Join("\n", chunk))
                    ],
                    ResponseFormat = ChatRequestResponseFormats.StructuredJson(([Description("seznam vyskloňovaných jmen")] List<Declinated> vysklonovanaJmena) =>
                    {
                        int z = 0;
                    }, "vysledek", "Akceptuje vyskloňovaná jména")
                }).GetResponseRich(token);

                var tokensOut = response.Usage.CompletionTokens;
                var tokensIn = response.Usage.PromptTokens;
                
                int cc = 0;
            });
        }
    }
}
