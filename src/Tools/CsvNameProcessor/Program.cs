using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;

namespace CsvNameProcessor
{
    public class NameData
    {
        public string Name { get; set; }
        public int MaleCount { get; set; }
        public int FemaleCount { get; set; }
        public int UnknownCount { get; set; }
        public bool IsFirstName { get; set; }
        public bool IsLastName { get; set; }
        
        public int TotalCount => MaleCount + FemaleCount + UnknownCount;
        
        public string GetGenderCategory()
        {
            if (MaleCount == 0 && FemaleCount == 0) return "U"; // Unknown
            
            var total = MaleCount + FemaleCount;
            if (total == 0) return "U";
            
            var maleRatio = (double)MaleCount / total;
            var femaleRatio = (double)FemaleCount / total;
            
            // If one gender is > 90%, consider it that gender
            if (maleRatio >= 0.9) return "M";
            if (femaleRatio >= 0.9) return "F";
            
            return "A"; // Androgenous
        }
        
        public string GetNameType()
        {
            if (IsFirstName && IsLastName) return "B"; // Both
            if (IsFirstName) return "F"; // First
            if (IsLastName) return "L"; // Last
            return "F"; // Default to first if unclear
        }
        
        public string ToOutputLine()
        {
            var nameType = GetNameType();
            var gender = GetGenderCategory();
            var name = CapitalizeName(Name);
            
            if (gender == "A")
            {
                var total = MaleCount + FemaleCount;
                var maleRatio = total > 0 ? (double)MaleCount / total : 0.5;
                var femaleRatio = total > 0 ? (double)FemaleCount / total : 0.5;
                return $"{nameType},{name},{gender},{maleRatio:F2},{femaleRatio:F2}";
            }
            else
            {
                return $"{nameType},{name},{gender}";
            }
        }
        
        private string CapitalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            
            var textInfo = CultureInfo.CurrentCulture.TextInfo;
            return textInfo.ToTitleCase(name.Trim().ToLower());
        }
    }
    
    class Program
    {
        // List of countries to process
        private static readonly string[] Countries = { "CZ", "SI", "SE", "RS", "PL", "DE", "RU", "NO", "HU" };
        
        static void Main(string[] args)
        {
            var dataDir = "../../../../Rnn/lfg/data";
            var outputDir = "../../Morpheus/Morpheus.Parser/extra";
            
            Console.WriteLine($"Processing CSV files from {Countries.Length} countries...");
            
            var firstNames = new Dictionary<string, NameData>();
            var lastNames = new Dictionary<string, NameData>();
            
            // Process each country's CSV file
            foreach (var country in Countries)
            {
                var csvPath = Path.Combine(dataDir, $"{country}.csv");
                
                if (!File.Exists(csvPath))
                {
                    Console.WriteLine($"Warning: {Path.GetFullPath(csvPath)} not found, skipping...");
                    continue;
                }
                
                Console.WriteLine($"Processing {country}.csv...");
                ProcessCountryFile(csvPath, firstNames, lastNames);
            }
            
            Console.WriteLine($"Total unique first names across all countries: {firstNames.Count}");
            Console.WriteLine($"Total unique last names across all countries: {lastNames.Count}");
            
            // Detect overlaps and apply 90% rule
            ApplyOverlapDetection(firstNames, lastNames);
            
            // Generate output files
            GenerateDataFiles(firstNames, lastNames, outputDir);
            
            Console.WriteLine("Processing complete!");
        }
        
        static void ProcessCountryFile(string csvPath, Dictionary<string, NameData> firstNames, Dictionary<string, NameData> lastNames)
        {
            var lines = File.ReadAllLines(csvPath);
            var lineCount = 0;
            
            foreach (var line in lines)
            {
                lineCount++;
                if (lineCount % 100000 == 0)
                {
                    Console.WriteLine($"  Processed {lineCount} lines...");
                }
                
                var parts = line.Split(',');
                if (parts.Length < 3) continue;
                
                var firstName = parts[0].Trim();
                var lastName = parts[1].Trim();
                var gender = parts.Length > 2 ? parts[2].Trim() : "";
                
                if (string.IsNullOrEmpty(firstName) || string.IsNullOrEmpty(lastName)) continue;
                
                // Process first name
                ProcessName(firstNames, firstName, gender, true, false);
                
                // Process last name
                ProcessName(lastNames, lastName, gender, false, true);
            }
            
            Console.WriteLine($"  Completed {Path.GetFileName(csvPath)}: {lineCount} lines processed");
        }
        
        static void ProcessName(Dictionary<string, NameData> nameDict, string name, string gender, bool isFirst, bool isLast)
        {
            if (!nameDict.ContainsKey(name))
            {
                nameDict[name] = new NameData { Name = name, IsFirstName = isFirst, IsLastName = isLast };
            }
            
            var nameData = nameDict[name];
            
            // Update name type flags
            if (isFirst) nameData.IsFirstName = true;
            if (isLast) nameData.IsLastName = true;
            
            // Update gender counts
            switch (gender.ToUpper())
            {
                case "M":
                    nameData.MaleCount++;
                    break;
                case "F":
                    nameData.FemaleCount++;
                    break;
                default:
                    nameData.UnknownCount++;
                    break;
            }
        }
        
        static void ApplyOverlapDetection(Dictionary<string, NameData> firstNames, Dictionary<string, NameData> lastNames)
        {
            Console.WriteLine("Applying overlap detection...");
            
            var overlappingNames = firstNames.Keys.Intersect(lastNames.Keys).ToList();
            Console.WriteLine($"Found {overlappingNames.Count} overlapping names");
            
            foreach (var name in overlappingNames)
            {
                var firstData = firstNames[name];
                var lastData = lastNames[name];
                
                var firstTotal = firstData.TotalCount;
                var lastTotal = lastData.TotalCount;
                var grandTotal = firstTotal + lastTotal;
                
                if (grandTotal == 0) continue;
                
                var firstRatio = (double)firstTotal / grandTotal;
                var lastRatio = (double)lastTotal / grandTotal;
                
                // Apply 90% rule
                if (firstRatio >= 0.9)
                {
                    // Name is predominantly a first name
                    lastNames.Remove(name);
                    Console.WriteLine($"'{name}' removed from last names (first name dominance: {firstRatio:P1})");
                }
                else if (lastRatio >= 0.9)
                {
                    // Name is predominantly a last name
                    firstNames.Remove(name);
                    Console.WriteLine($"'{name}' removed from first names (last name dominance: {lastRatio:P1})");
                }
                else
                {
                    // Name appears in both with significant frequency
                    // Merge the data
                    firstData.MaleCount += lastData.MaleCount;
                    firstData.FemaleCount += lastData.FemaleCount;
                    firstData.UnknownCount += lastData.UnknownCount;
                    firstData.IsLastName = true;
                    
                    lastNames.Remove(name);
                    Console.WriteLine($"'{name}' merged as both first/last name (first: {firstRatio:P1}, last: {lastRatio:P1})");
                }
            }
        }
        
        static void GenerateDataFiles(Dictionary<string, NameData> firstNames, Dictionary<string, NameData> lastNames, string outputDir)
        {
            Console.WriteLine("Generating data files...");
            
            // Combine all names
            var allNames = new List<NameData>();
            allNames.AddRange(firstNames.Values);
            allNames.AddRange(lastNames.Values);
            
            // Sort by name for consistent output
            allNames = allNames.OrderBy(n => n.Name).ToList();
            
            // Write first names data
            var firstDir = Path.Combine(outputDir, "first");
            Directory.CreateDirectory(firstDir);
            var firstOutput = allNames.Where(n => n.IsFirstName).Select(n => n.ToOutputLine()).ToList();
            File.WriteAllLines(Path.Combine(firstDir, "data.txt"), firstOutput);
            Console.WriteLine($"Written {firstOutput.Count} first names to data.txt");
            
            // Write last names data
            var lastDir = Path.Combine(outputDir, "last");
            Directory.CreateDirectory(lastDir);
            var lastOutput = allNames.Where(n => n.IsLastName && !n.IsFirstName).Select(n => n.ToOutputLine()).ToList();
            File.WriteAllLines(Path.Combine(lastDir, "data.txt"), lastOutput);
            Console.WriteLine($"Written {lastOutput.Count} last names to data.txt");
            
            // Statistics
            var bothCount = allNames.Count(n => n.IsFirstName && n.IsLastName);
            var maleCount = allNames.Count(n => n.GetGenderCategory() == "M");
            var femaleCount = allNames.Count(n => n.GetGenderCategory() == "F");
            var androCount = allNames.Count(n => n.GetGenderCategory() == "A");
            var unknownCount = allNames.Count(n => n.GetGenderCategory() == "U");
            
            Console.WriteLine($"Statistics:");
            Console.WriteLine($"  Names appearing as both first/last: {bothCount}");
            Console.WriteLine($"  Male names: {maleCount}");
            Console.WriteLine($"  Female names: {femaleCount}");
            Console.WriteLine($"  Androgenous names: {androCount}");
            Console.WriteLine($"  Unknown gender names: {unknownCount}");
        }
    }
}
