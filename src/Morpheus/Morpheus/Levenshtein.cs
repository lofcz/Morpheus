using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO.Compression;

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
/// The BK-Tree implementation for fuzzy string matching.
/// </summary>
public class BKTree
{
    private Node? _root;

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

            if (currentNode.Children.TryGetValue(distance, out Node? childNode))
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

    // ==== NEW COMPACT STRUCTS ====
    internal readonly struct FlatNode
    {
        public readonly int NameOffset;      // byte offset in UTF-8 blob
        public readonly ushort NameLength;   // UTF-8 byte length
        public readonly int FirstEdgeIndex;  // -1 if leaf
        public readonly byte EdgeCount;      // number of outgoing edges
        public readonly NameRole Role;       // 1 byte after cast
        public readonly byte GenderTag;      // 0=Androgyne,1=Unknown,2=Male,3=Female
        public readonly ushort MaleRatio;    // *10000 (only if androgyne)
        public readonly ushort FemaleRatio;

        public FlatNode(int nameOfs, ushort nameLen, int firstEdge, byte edgeCnt, NameRole role,
                        byte genderTag, ushort male, ushort female)
        {
            NameOffset = nameOfs;
            NameLength = nameLen;
            FirstEdgeIndex = firstEdge;
            EdgeCount = edgeCnt;
            Role = role;
            GenderTag = genderTag;
            MaleRatio = male;
            FemaleRatio = female;
        }
    }

    internal readonly struct FlatEdge
    {
        public readonly byte Distance;   // Levenshtein distance to child
        public readonly int ChildIndex;  // index in FlatNode[]
        public FlatEdge(byte d,int idx){Distance=d;ChildIndex=idx;}
    }

    // ==== BKTree fields for compact representation ====
    private FlatNode[] _flatNodes = Array.Empty<FlatNode>();
    private FlatEdge[] _flatEdges = Array.Empty<FlatEdge>();
    private byte[] _nameBlob = Array.Empty<byte>();
    private string[] _indexKeys = Array.Empty<string>();

    // ================== SAVE TO FILE (flat) ==================
    public void SaveToFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream, Encoding.UTF8);
        
        if (_root == null) 
        {
            writer.Write("BKTREE03");
            writer.Write((byte)0);      // flags: 0 = uncompressed
            writer.Write(0);            // node count
            writer.Write(0);            // edge count
            writer.Write(0);            // blob length
            return;
        }
        
        // Build flat lists
        var nodes = new List<FlatNode>();
        var edges = new List<FlatEdge>();
        var utf8 = new List<byte>();

        int AddNodeRecursive(Node n)
        {
            int nameOfs = utf8.Count;
            var nameBytes = Encoding.UTF8.GetBytes(n.Data.Name);
            utf8.AddRange(nameBytes);
            ushort nameLen = (ushort)nameBytes.Length;

            byte genderTag = GenderToTag(n.Data.Gender, out ushort male, out ushort female);

            nodes.Add(new FlatNode(nameOfs, nameLen, -1, 0, n.Data.Role, genderTag, male, female));
            int currentIdx = nodes.Count - 1;

            var localEdges = new List<FlatEdge>();
            foreach (var kvp in n.Children)
            {
                byte dist = (byte)kvp.Key;
                int childIdx = AddNodeRecursive(kvp.Value);
                localEdges.Add(new FlatEdge(dist, childIdx));
            }

            int firstEdgeIdx = edges.Count;
            edges.AddRange(localEdges);
            byte edgeCnt = (byte)localEdges.Count;

            nodes[currentIdx] = new FlatNode(nameOfs, nameLen, edgeCnt == 0 ? -1 : firstEdgeIdx, edgeCnt, n.Data.Role,
                                             genderTag, male, female);
            return currentIdx;
        }

        AddNodeRecursive(_root);

        // Prepare raw sections as byte arrays
        byte[] nodesBytes;
        byte[] edgesBytes;
        byte[] blobBytes = utf8.ToArray();

        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            foreach (var fn in nodes)
            {
                bw.Write(fn.NameOffset);
                bw.Write(fn.NameLength);
                bw.Write(fn.FirstEdgeIndex);
                bw.Write(fn.EdgeCount);
                bw.Write((byte)fn.Role);
                bw.Write(fn.GenderTag);
                bw.Write(fn.MaleRatio);
                bw.Write(fn.FemaleRatio);
            }
            bw.Flush();
            nodesBytes = ms.ToArray();
        }

        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            foreach (var e in edges)
            {
                bw.Write(e.Distance);
                bw.Write(e.ChildIndex);
            }
            bw.Flush();
            edgesBytes = ms.ToArray();
        }

        // Compress sections with Deflate (Fastest)
        static byte[] DeflateFast(byte[] data)
        {
            using var outMs = new MemoryStream();
            using (var ds = new DeflateStream(outMs, new CompressionLevel?(CompressionLevel.Fastest).Value, leaveOpen: true))
            {
                ds.Write(data, 0, data.Length);
            }
            return outMs.ToArray();
        }

        var nodesCompressed = DeflateFast(nodesBytes);
        var edgesCompressed = DeflateFast(edgesBytes);
        var blobCompressed  = DeflateFast(blobBytes);

        // --- write header ---
        writer.Write("BKTREE03");
        writer.Write((byte)1); // flags: bit0=compressed
        writer.Write(nodes.Count);          // node count
        writer.Write(edges.Count);          // edge count
        writer.Write(blobBytes.Length);     // original blob length

        // write compressed sizes for three sections
        writer.Write(nodesCompressed.Length);
        writer.Write(edgesCompressed.Length);
        writer.Write(blobCompressed.Length);

        // payloads
        writer.Write(nodesCompressed);
        writer.Write(edgesCompressed);
        writer.Write(blobCompressed);
    }

    private static byte GenderToTag(GenderInfo g,out ushort male,out ushort female)
    {
        male=female=0;
        if(g is AndrogyneGender ag)
        {
            male=(ushort)((ag.MaleRatio??0f)*10000);
            female=(ushort)((ag.FemaleRatio??0f)*10000);
            return 0;
        }
        if(g is SimpleGender sg)
        {
            return (byte)((int)sg.Type+1); // 1..3
        }
        return 1; // Unknown
    }

    // ================== LOAD FROM FILE (flat) ==================
    public static BKTree LoadFromFile(string path)
    {
        var tree = new BKTree();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        
        var header = reader.ReadString();
        int flags = reader.ReadByte();
        int nodeCount = reader.ReadInt32();
        int edgeCount = reader.ReadInt32();
        int blobLen   = reader.ReadInt32();

        if (nodeCount == 0)
            return tree;

        byte[] nodesBuf;
        byte[] edgesBuf;
        byte[] blobBuf;

        bool compressed = (flags & 1) != 0;
        if (compressed)
        {
            int nodesZ = reader.ReadInt32();
            int edgesZ = reader.ReadInt32();
            int blobZ  = reader.ReadInt32();

            static byte[] InflateExact(BinaryReader br, int compLen, int rawLen)
            {
                var comp = br.ReadBytes(compLen);
                using var inMs = new MemoryStream(comp);
                using var ds = new DeflateStream(inMs, CompressionMode.Decompress);
                var raw = new byte[rawLen];
                int read = 0; int n;
                while ((n = ds.Read(raw, read, raw.Length - read)) > 0) read += n;
                return raw;
            }

            // We must know raw sizes of nodes/edges; derive from counts and field sizes
            int nodeRecordSize = 4 + 2 + 4 + 1 + 1 + 1 + 2 + 2; // match Save layout
            int edgeRecordSize = 1 + 4;
            int nodesRawLen = nodeCount * nodeRecordSize;
            int edgesRawLen = edgeCount * edgeRecordSize;

            nodesBuf = InflateExact(reader, nodesZ, nodesRawLen);
            edgesBuf = InflateExact(reader, edgesZ, edgesRawLen);
            blobBuf  = InflateExact(reader, blobZ, blobLen);
        }
        else
        {
            // Uncompressed path (legacy)
            var nodes = new FlatNode[nodeCount];
            var edges = new FlatEdge[edgeCount];
            var blob  = new byte[blobLen];

            for (int i = 0; i < nodeCount; i++)
            {
                int nameOfs = reader.ReadInt32();
                ushort nameLen = reader.ReadUInt16();
                int firstEdge = reader.ReadInt32();
                byte edgeCnt = reader.ReadByte();
                NameRole role = (NameRole)reader.ReadByte();
                byte genderTag = reader.ReadByte();
                ushort male = reader.ReadUInt16();
                ushort female = reader.ReadUInt16();
                nodes[i] = new FlatNode(nameOfs, nameLen, firstEdge, edgeCnt, role, genderTag, male, female);
            }
            for (int i = 0; i < edgeCount; i++)
            {
                byte dist = reader.ReadByte();
                int child = reader.ReadInt32();
                edges[i] = new FlatEdge(dist, child);
            }
            stream.Read(blob, 0, blobLen);

            tree._flatNodes = nodes;
            tree._flatEdges = edges;
            tree._nameBlob = blob;
            tree._indexKeys = new string[nodeCount];
            return tree;
        }

        // Parse decompressed buffers
        var nodesArr = new FlatNode[nodeCount];
        var edgesArr = new FlatEdge[edgeCount];
        using (var ms = new MemoryStream(nodesBuf))
        using (var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true))
        {
            for (int i = 0; i < nodeCount; i++)
            {
                int nameOfs = br.ReadInt32();
                ushort nameLen = br.ReadUInt16();
                int firstEdge = br.ReadInt32();
                byte edgeCnt = br.ReadByte();
                NameRole role = (NameRole)br.ReadByte();
                byte genderTag = br.ReadByte();
                ushort male = br.ReadUInt16();
                ushort female = br.ReadUInt16();
                nodesArr[i] = new FlatNode(nameOfs, nameLen, firstEdge, edgeCnt, role, genderTag, male, female);
            }
        }
        using (var ms = new MemoryStream(edgesBuf))
        using (var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true))
        {
            for (int i = 0; i < edgeCount; i++)
            {
                byte dist = br.ReadByte();
                int child = br.ReadInt32();
                edgesArr[i] = new FlatEdge(dist, child);
            }
        }

        tree._flatNodes = nodesArr;
        tree._flatEdges = edgesArr;
        tree._nameBlob  = blobBuf;
        tree._indexKeys = new string[nodeCount];
        return tree;
    }
    
    // ================== SEARCH (flat) ==================
    public List<NameEntry> Search(string queryName,int maxDistance)
    {
        if(_flatNodes.Length==0)
        {
            // fallback to legacy object tree (during build time)
            return _root==null? new List<NameEntry>() : SearchLegacy(queryName,maxDistance);
        }
        var results = new List<NameEntry>();
        string normalizedQuery = Normalizer.RemoveDiacritics(queryName).ToLowerInvariant().Trim();
        if(_flatNodes.Length==0) return results;
        var queue = new Queue<int>();
        queue.Enqueue(0); // root is 0
        while(queue.Count>0)
        {
            int idx = queue.Dequeue();
            var node = _flatNodes[idx];
            string key = _indexKeys[idx] ??= GetIndexKey(idx);
            int dist = Levenshtein.Distance(key, normalizedQuery);
            if(dist<=maxDistance)
                results.Add(ToNameEntry(idx));
            // enqueue children whose edge distance is within band
            if(node.EdgeCount==0) continue;
            int first = node.FirstEdgeIndex;
            for(int e=first;e<first+node.EdgeCount;e++)
            {
                var edge=_flatEdges[e];
                int d=edge.Distance;
                if(d>=dist-maxDistance && d<=dist+maxDistance)
                    queue.Enqueue(edge.ChildIndex);
            }
        }
        return results;
    }

    private NameEntry ToNameEntry(int idx)
    {
        var n=_flatNodes[idx];
        string disp = Encoding.UTF8.GetString(_nameBlob,n.NameOffset,n.NameLength);
        GenderInfo gender = n.GenderTag switch{0=>new AndrogyneGender(n.MaleRatio/10000f,n.FemaleRatio/10000f),
                                              2=>new SimpleGender(SimpleGender.GenderType.Male),
                                              3=>new SimpleGender(SimpleGender.GenderType.Female),
                                              _=>new SimpleGender(SimpleGender.GenderType.Unknown)};
        return new NameEntry(disp, _indexKeys[idx]!, gender, n.Role);
    }

    private string GetIndexKey(int idx)
    {
        var n=_flatNodes[idx];
        string disp = Encoding.UTF8.GetString(_nameBlob,n.NameOffset,n.NameLength);
        return Normalizer.RemoveDiacritics(disp).ToLowerInvariant().Trim();
    }

    private List<NameEntry> SearchLegacy(string query,int maxDist)
    {
        var list=new List<NameEntry>();
        if(_root==null) return list;
        string normalizedQuery = Normalizer.RemoveDiacritics(query).ToLowerInvariant().Trim();
        var q=new Queue<Node>();q.Enqueue(_root);
        while(q.Count>0)
        {
            var node=q.Dequeue();
            int dist=Levenshtein.Distance(node.Data.IndexKey,normalizedQuery);
            if(dist<=maxDist) list.Add(node.Data);
            foreach(var kvp in node.Children)
            {
                int d=kvp.Key;
                if(d>=dist-maxDist && d<=dist+maxDist) q.Enqueue(kvp.Value);
            }
        }
        return list;
    }
}

public static class BKTreeBuilder
{
    public static void BuildIndex(IEnumerable<NameEntry> nameEntries, string indexOutputPath)
    {
        var tree = new BKTree();
        foreach (var entry in nameEntries)
        {
            tree.Add(entry);
        }
        tree.SaveToFile(indexOutputPath);
    }

    // CONVENIENCE: Build from combined data without JSON intermediate
    public static void BuildIndexFromCombined(Dictionary<string, CombinedItem> combined, string indexOutputPath)
    {
        var tree = new BKTree();
        foreach (var kvp in combined.Values)
        {
            var entry = new NameEntry(kvp.DisplayName, kvp.IndexKey, kvp.Gender, kvp.Role);
            tree.Add(entry);
        }
        tree.SaveToFile(indexOutputPath);
    }
}

// Data structure for building index from combined first name + surname data
public class CombinedItem
{
    public string DisplayName { get; set; } = string.Empty; // diacritics-preserved display name
    public string IndexKey { get; set; } = string.Empty;    // normalized key
    public GenderInfo Gender { get; set; } = new SimpleGender(SimpleGender.GenderType.Unknown);
    public NameRole Role { get; set; } = NameRole.First;
    public int FirstCount { get; set; }
    public int SurnameCount { get; set; }
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
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("gender")]
    public GenderInfo Gender { get; set; } = new SimpleGender(SimpleGender.GenderType.Unknown);

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