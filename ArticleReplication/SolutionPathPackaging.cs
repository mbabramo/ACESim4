using System.Text;
using System.Text.RegularExpressions;

namespace ArticleReplication;

/// <summary>Lossless local script chunks keep large viewers below GitHub's per-file limit.</summary>
public static class SolutionPathPackaging
{
    const string Open="<script id=\"trace-data\" type=\"application/gzip\">";
    const int ChunkCharacters=32*1024*1024;
    public static object[] Package(string directory)
    {
        if(!Directory.Exists(directory))return [];
        var records=new List<object>();
        foreach(string file in Directory.GetFiles(directory,"*.html"))
        {
            if(new FileInfo(file).Length<=80*1024*1024)continue;
            string html=File.ReadAllText(file);int start=html.IndexOf(Open,StringComparison.Ordinal);
            if(start<0)throw new InvalidDataException("Large viewer lacks its compressed trace.");
            start+=Open.Length;int end=html.IndexOf("</script>",start,StringComparison.Ordinal);
            if(end<start)throw new InvalidDataException("Unclosed compressed trace.");
            string data=html[start..end];
            if(data.Length<ChunkCharacters||data.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not ('+' or '/' or '=')))throw new InvalidDataException("Invalid base64 trace.");
            string stem=Path.GetFileNameWithoutExtension(file);string folder=stem+"-data";
            if(!Regex.IsMatch(folder,@"^[A-Za-z0-9_.-]+$"))throw new InvalidDataException("Unsafe viewer asset name.");
            string assets=Path.Combine(directory,folder);Directory.CreateDirectory(assets);
            var scripts=new StringBuilder("</script>\n<script>globalThis.articleTraceChunks=[];</script>\n");
            var recovered=new StringBuilder(data.Length);int chunks=0;
            const string prefix="globalThis.articleTraceChunks.push(\"",suffix="\");\n";
            for(int offset=0;offset<data.Length;offset+=ChunkCharacters)
            {
                string chunk=data.Substring(offset,Math.Min(ChunkCharacters,data.Length-offset));string name=$"part-{chunks++:D3}.js";
                string path=Path.Combine(assets,name);
                using(var writer=new StreamWriter(new FileStream(path,FileMode.CreateNew),new UTF8Encoding(false)))writer.Write(prefix+chunk+suffix);
                string stored=File.ReadAllText(path);
                if(!stored.StartsWith(prefix,StringComparison.Ordinal)||!stored.EndsWith(suffix,StringComparison.Ordinal))throw new InvalidDataException("Malformed viewer chunk.");
                recovered.Append(stored[prefix.Length..^suffix.Length]);
                scripts.Append("<script src=\"").Append(folder).Append('/').Append(name).Append("\"></script>\n");
            }
            if(!string.Equals(data,recovered.ToString(),StringComparison.Ordinal))throw new InvalidDataException("Viewer payload changed during packaging.");
            scripts.Append("<script>document.getElementById('trace-data').textContent=globalThis.articleTraceChunks.join('');delete globalThis.articleTraceChunks;</script>");
            string output=html[..start]+scripts+html[(end+"</script>".Length)..];
            File.WriteAllText(file,output,new UTF8Encoding(false));
            records.Add(new{Viewer=Path.GetFileName(file),Chunks=chunks,Base64Characters=data.Length,CompressedPayloadExactlyPreserved=true});
        }
        return records.ToArray();
    }
}
