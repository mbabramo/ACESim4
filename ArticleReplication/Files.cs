using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ArticleReplication;

public static class Files
{
    public static readonly JsonSerializerOptions Json=new(){WriteIndented=true,PropertyNameCaseInsensitive=true,
        Converters={new JsonStringEnumConverter()}};
    public static T Read<T>(string path)=>JsonSerializer.Deserialize<T>(File.ReadAllText(path),Json)??throw new InvalidDataException(path);
    public static JsonObject Object(string path)=>JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    public static string Sha(string path){using var s=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();}
    public static string Digest<T>(T value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,Json))).ToLowerInvariant();
    public static void Save(string path,object value,bool replace=false)
    {Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);using var s=new FileStream(path,replace?FileMode.Create:FileMode.CreateNew);JsonSerializer.Serialize(s,value,Json);}
    public static string Under(string root,string relative)
    {
        if(Path.IsPathRooted(relative))throw new InvalidDataException("Expected a portable relative path.");
        string r=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,p=Path.GetFullPath(Path.Combine(r,relative));
        if(!p.StartsWith(r,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Path escaped its declared root.");
        for(string? d=Path.GetDirectoryName(p);d!=null&&d.StartsWith(r,StringComparison.OrdinalIgnoreCase);d=Path.GetDirectoryName(d))
            if(Directory.Exists(d)&&(File.GetAttributes(d)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked output/input directory not permitted.");
        if(File.Exists(p)&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked input file not permitted.");
        return p;
    }
    public static void CopyVerified(string from,string to,string? expected=null)
    {string sha=Sha(from);if(expected!=null&&!sha.Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Changed input: "+from);
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(from,to,false);if(Sha(to)!=sha)throw new IOException("Copy mismatch.");}
    public static void EqualScience(JsonNode? a,JsonNode? b,string context)
    {
        if(a is null||b is null){if(a!=null||b!=null)throw new InvalidDataException(context);return;}
        if(a is JsonObject x&&b is JsonObject y)
        {if(x.Count!=y.Count||x.Any(k=>!y.ContainsKey(k.Key)))throw new InvalidDataException(context+": fields");foreach(var k in x)EqualScience(k.Value,y[k.Key],context+"/"+k.Key);return;}
        if(a is JsonArray u&&b is JsonArray v)
        {if(u.Count!=v.Count)throw new InvalidDataException(context+": length");for(int i=0;i<u.Count;i++)EqualScience(u[i],v[i],context+"/"+i);return;}
        if(a.GetValueKind()==JsonValueKind.Number&&b.GetValueKind()==JsonValueKind.Number)
        {double x1=a.GetValue<double>(),x2=b.GetValue<double>();if(!double.IsFinite(x1)||x1!=x2)throw new InvalidDataException(context+": numeric difference");return;}
        if(!JsonNode.DeepEquals(a,b))throw new InvalidDataException(context+": difference");
    }
}
