using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Morpheus;

[Flags]
public enum NameRole
{
    None = 0,
    First = 1,
    Surname = 2
}

public static class Levenshtein
{
    public static int Distance(string value1, string value2)
    {
        if (value2.Length == 0)
        {
            return value1.Length;
        }

        int[] costs = new int[value2.Length];
        
        for (int i = 0; i < costs.Length;)
        {
            costs[i] = ++i;
        }

        for (int i = 0; i < value1.Length; i++)
        {
            int cost = i;
            int previousCost = i;

            char value1Char = value1[i];

            for (int j = 0; j < value2.Length; j++)
            {
                int currentCost = cost;

                cost = costs[j];

                if (value1Char != value2[j])
                {
                    if (previousCost < currentCost)
                    {
                        currentCost = previousCost;
                    }

                    if (cost < currentCost)
                    {
                        currentCost = cost;
                    }

                    ++currentCost;
                }
                
                costs[j] = currentCost;
                previousCost = currentCost;
            }
        }

        return costs[^1];
    }
}

/// <summary>
/// The internal implementation of the BK-Tree.
/// </summary>
internal class BKTree
{
    private Node _root;

    private class Node
    {
        public NameEntry Data { get; }
        public Dictionary<int, Node> Children { get; } = new();
        public Node(NameEntry data) => Data = data;
    }

    public void Add(NameEntry entry)
    {
        if (_root == null)
        {
            _root = new Node(entry);
            return;
        }

        Node currentNode = _root;
        while (true)
        {
            int distance = Levenshtein.Distance(currentNode.Data.IndexKey, entry.IndexKey);
            if (distance == 0) return;

            if (currentNode.Children.TryGetValue(distance, out Node childNode))
            {
                currentNode = childNode;
            }
            else
            {
                currentNode.Children.Add(distance, new Node(entry));
                break;
            }
        }
    }

    public List<NameEntry> Search(string queryWord, int maxDistance)
    {
        var results = new List<NameEntry>();
        if (_root == null) return results;

        var candidates = new Queue<Node>();
        candidates.Enqueue(_root);

        while (candidates.Count > 0)
        {
            Node node = candidates.Dequeue();
            int distance = Levenshtein.Distance(node.Data.IndexKey, queryWord);

            if (distance <= maxDistance)
            {
                results.Add(node.Data);
            }

            for (int i = Math.Max(1, distance - maxDistance); i <= distance + maxDistance; i++)
            {
                if (node.Children.TryGetValue(i, out Node child))
                {
                    candidates.Enqueue(child);
                }
            }
        }
        return results;
    }

    public void SaveToFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream, Encoding.UTF8);
        
        if (_root == null) 
        {
            writer.Write("BKTREE03"); // Version 3 - optimized format
            writer.Write(0); // Count = 0
            return;
        }
        
        // Count nodes first
        var nodeCount = CountNodes(_root);
        
        // Write header
        writer.Write("BKTREE03"); // Version 3 - direct tree serialization
        writer.Write(nodeCount);
        
        // Serialize the tree structure directly
        SerializeNode(writer, _root);
    }
    
    private int CountNodes(Node node)
    {
        int count = 1;
        foreach (var child in node.Children.Values)
        {
            count += CountNodes(child);
        }
        return count;
    }
    
    private void SerializeNode(BinaryWriter writer, Node node)
    {
        // Write node data (compact format)
        WriteCompactString(writer, node.Data.Name);
        writer.Write((byte)node.Data.Role);
        
        // Write gender info (compact)
        var gender = node.Data.Gender;
        if (gender is SimpleGender sg)
        {
            writer.Write((byte)((int)sg.Type + 1)); // 1-4 for Simple genders
        }
        else if (gender is AndrogyneGender ag)
        {
            writer.Write((byte)0); // 0 for Androgyne
            writer.Write((ushort)((ag.MaleRatio ?? 0.0f) * 10000)); // Store as ushort (0-10000)
            writer.Write((ushort)((ag.FemaleRatio ?? 0.0f) * 10000));
        }
        else
        {
            writer.Write((byte)1); // Unknown = 1 (same as SimpleGender.Unknown)
        }
        
        // Write children count and edges
        writer.Write((byte)node.Children.Count);
        foreach (var kvp in node.Children)
        {
            writer.Write((byte)kvp.Key); // Distance (0-255 should be enough)
            SerializeNode(writer, kvp.Value); // Recursively serialize child
        }
    }
    
    private void WriteCompactString(BinaryWriter writer, string str)
    {
        // More compact string storage
        var bytes = Encoding.UTF8.GetBytes(str);
        if (bytes.Length < 255)
        {
            writer.Write((byte)bytes.Length);
            writer.Write(bytes);
        }
        else
        {
            writer.Write((byte)255);
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }
    }

    public static BKTree LoadFromFile(string path)
    {
        var tree = new BKTree();
        
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        
        // Check format version
        var header = reader.ReadString();
        var nodeCount = reader.ReadInt32();
        
        if (nodeCount > 0)
        {
            tree._root = DeserializeNode(reader);
        }
        
        return tree;
    }
    
    private static Node DeserializeNode(BinaryReader reader)
    {
        // Read node data
        var name = ReadCompactString(reader);
        var role = (NameRole)reader.ReadByte();
        
        // Read gender info
        var genderByte = reader.ReadByte();
        GenderInfo genderInfo;
        
        if (genderByte == 0) // Androgyne
        {
            var maleRatio = reader.ReadUInt16() / 10000.0f;
            var femaleRatio = reader.ReadUInt16() / 10000.0f;
            genderInfo = new AndrogyneGender(maleRatio, femaleRatio);
        }
        else // Simple gender (1-4)
        {
            var simpleType = (SimpleGender.GenderType)(genderByte - 1);
            genderInfo = new SimpleGender(simpleType);
        }
        
        // Create index key
        string indexKey = Normalizer.RemoveDiacritics(name).ToLowerInvariant().Trim();
        var entry = new NameEntry(name, indexKey, genderInfo, role);
        var node = new Node(entry);
        
        // Read children
        var childCount = reader.ReadByte();
        for (int i = 0; i < childCount; i++)
        {
            var distance = reader.ReadByte();
            var childNode = DeserializeNode(reader);
            node.Children[distance] = childNode;
        }
        
        return node;
    }
    
    private static string ReadCompactString(BinaryReader reader)
    {
        var length = reader.ReadByte();
        int actualLength;
        if (length == 255)
        {
            actualLength = reader.ReadUInt16();
        }
        else
        {
            actualLength = length;
        }
        var bytes = reader.ReadBytes(actualLength);
        return Encoding.UTF8.GetString(bytes);
    }
}

public static class BKTreeBuilder
{
    public static void BuildIndex(string jsonInputPath, string indexOutputPath)
    {
        var jsonString = File.ReadAllText(jsonInputPath);
        // The custom GenderInfoConverter will handle the complex JSON
        var jsonEntries = JsonSerializer.Deserialize<List<JsonNameEntry>>(jsonString);

        if (jsonEntries == null) throw new InvalidDataException("JSON parsing failed.");

        var tree = new BKTree();
        foreach (var jsonEntry in jsonEntries)
        {
            // The Gender property is now a GenderInfo object thanks to the converter
            string indexKey = Normalizer.RemoveDiacritics(jsonEntry.Name).ToLowerInvariant().Trim();
            var entry = new NameEntry(jsonEntry.Name, indexKey, jsonEntry.Gender, jsonEntry.Role);
            tree.Add(entry);
        }

        tree.SaveToFile(indexOutputPath);
    }
}

/// <summary>
/// RUNTIME SEARCHER: Your application will use this class to perform fast, fuzzy searches.
/// </summary>
public class NameSearcher
{
    private readonly BKTree _tree;

    /// <summary>
    /// Creates a new NameSearcher by loading a pre-built index from disk.
    /// </summary>
    /// <param name="indexFilePath">The path to the 'index.bk' file created by BKTreeBuilder.</param>
    public NameSearcher(string indexFilePath)
    {
        if (!File.Exists(indexFilePath))
        {
            throw new FileNotFoundException("The specified index file was not found.", indexFilePath);
        }
        _tree = BKTree.LoadFromFile(indexFilePath);
    }

    /// <summary>
    /// Performs a fuzzy search for a name.
    /// </summary>
    /// <param name="queryName">The name to search for.</param>
    /// <param name="maxDistance">The maximum Levenshtein distance for a match (e.g., 1 for one typo).</param>
    /// <returns>A list of matching NameEntry objects, each containing the name and gender.</returns>
    public List<NameEntry> Search(string queryName, int maxDistance)
    {
        // Normalize query to match index keys (lowercase, no diacritics)
        string normalizedQuery = Normalizer.RemoveDiacritics(queryName).ToLowerInvariant().Trim();
        return _tree.Search(normalizedQuery, maxDistance);
    }
}

[JsonConverter(typeof(GenderInfoConverter))] // Custom converter for JSON deserialization
public abstract class GenderInfo
{
    public abstract string TypeIdentifier { get; }
    public abstract override string ToString();
}

/// <summary>
/// Represents a simple, non-ratio gender (Male, Female, Unknown).
/// </summary>
public class SimpleGender : GenderInfo
{
    public enum GenderType { Unknown, Male, Female }
    public GenderType Type { get; }

    public override string TypeIdentifier => "S"; // 'S' for Simple

    public SimpleGender(GenderType type) { Type = type; }

    public override string ToString() => Type.ToString();
}

/// <summary>
/// Represents an androgyne gender with optional male/female ratios.
/// </summary>
public class AndrogyneGender : GenderInfo
{
    public float? MaleRatio { get; }
    public float? FemaleRatio { get; }

    public override string TypeIdentifier => "A"; // 'A' for Androgyne

    public AndrogyneGender(float? maleRatio = null, float? femaleRatio = null)
    {
        MaleRatio = maleRatio;
        FemaleRatio = femaleRatio;
    }

    public override string ToString()
    {
        if (MaleRatio.HasValue && FemaleRatio.HasValue)
        {
            return $"Androgyne (M: {MaleRatio:P0}, F: {FemaleRatio:P0})";
        }
        return "Androgyne";
    }
}


// --- 2. The updated NameEntry struct ---

/// <summary>
/// The primary data structure, now holding a GenderInfo object and index key for search.
/// </summary>
public readonly struct NameEntry
{
    public string Name { get; }        // Display name with proper formatting
    public string IndexKey { get; }    // Search key: lowercase, no diacritics
    public GenderInfo Gender { get; }
    public NameRole Role { get; }

    public NameEntry(string name, string indexKey, GenderInfo gender, NameRole role = NameRole.First)
    {
        Name = name;
        IndexKey = indexKey;
        Gender = gender;
        Role = role;
    }

    public override string ToString()
    {
        return $"{Name} ({Gender})";
    }
}


// --- 3. Helper classes for JSON deserialization ---

/// <summary>
/// A helper class for deserializing the input JSON file.
/// </summary>
internal class JsonNameEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("gender")]
    public GenderInfo Gender { get; set; }

    [JsonPropertyName("role")]
    public NameRole Role { get; set; }
}

/// <summary>
/// Custom JsonConverter to handle the complex 'gender' field in the JSON.
/// It decides whether to create a SimpleGender or AndrogyneGender object.
/// </summary>
internal class GenderInfoConverter : JsonConverter<GenderInfo>
{
    public override GenderInfo Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            var genderInt = reader.GetInt32();
            return new SimpleGender((SimpleGender.GenderType)(genderInt));
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            float? maleRatio = null;
            float? femaleRatio = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) break;

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    var propName = reader.GetString();
                    reader.Read(); // Move to value
                    if (propName == "male") maleRatio = reader.GetSingle();
                    if (propName == "female") femaleRatio = reader.GetSingle();
                }
            }
            return new AndrogyneGender(maleRatio, femaleRatio);
        }

        throw new JsonException("Invalid format for gender field.");
    }

    public override void Write(Utf8JsonWriter writer, GenderInfo value, JsonSerializerOptions options)
    {
        // We only need to read from JSON, not write to it.
        throw new NotImplementedException();
    }
}