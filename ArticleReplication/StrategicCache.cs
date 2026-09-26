using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace ArticleReplication;

public static class StrategicCache
{
    private static Stream Open(string file)=>file.EndsWith(".gz")?new GZipStream(File.OpenRead(file),CompressionMode.Decompress):File.OpenRead(file);
    private static string ExpandedHash(string file){using var s=Open(file);return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();}
    public static void Import(string bundle,BundleManifest manifest,ResolvedArticlePlan plan,Dictionary<string,(JsonObject Audit,JsonObject Profile)> profiles,string output)
    {
        Directory.CreateDirectory(output);var records=new List<object>();var available=new HashSet<string>();
        var pairs=manifest.Renders.Where(r=>r.Output.StartsWith("Supplemental materials/Equilibrium strategy changes/")&&r.CaseIds.All(profiles.ContainsKey)).GroupBy(r=>Path.GetDirectoryName(r.Source)!);
        foreach(var pair in pairs)
        {
            var entry=pair.First();if(entry.CaseIds.Length!=2)throw new InvalidDataException("Unbound strategic cache.");
            var expectedDirections=plan.Strategic.Where(p=>entry.CaseIds.Contains(p.Source)&&entry.CaseIds.Contains(p.Target)).ToArray();
            if(expectedDirections.Length==0)continue;
            string dir=Files.Under(bundle,pair.Key);var receipt=Files.Object(Path.Combine(dir,"validation.json"));var checks=receipt["Checks"]!;
            if(receipt["Passed"]?.GetValue<bool>()!=true||checks["CompleteEndpointProbabilitiesIdentical"]?.GetValue<bool>()!=true||checks["TieAndOffPathChecksComplete"]?.GetValue<bool>()!=true||checks["CoalitionsPerPlayer"]!.GetValue<int>()!=16||checks["OrdersIndependentlyChecked"]!.GetValue<int>()!=24)throw new InvalidDataException("Incomplete strategic receipt.");
            string request=Path.Combine(dir,"request.json");if(Files.Sha(request)!=receipt["Request"]!["Sha256"]!.GetValue<string>())throw new InvalidDataException("Changed strategic request.");
            var evidence=new List<object>();
            foreach(var f in checks["Outputs"]!.AsArray().Append(checks["Manifest"]))
            {
                string basename=Files.LegacyBaseName(f!["Path"]!.GetValue<string>()),file=Path.Combine(dir,basename+".gz");
                string sha=ExpandedHash(file);if(sha!=f["Sha256"]!.GetValue<string>().ToLowerInvariant())throw new InvalidDataException("Changed decompressed strategic result: "+basename);
                evidence.Add(new{File=basename,Sha256=sha});
            }
            using var stream=Open(Path.Combine(dir,"equilibrium-changes-manifest.json.gz"));var data=JsonNode.Parse(stream)!.AsObject();
            if(data["Schema"]!.GetValue<string>()!="3"||data["MaxIntegralUtility"]!.GetValue<int>()!=100000||data["RoundOffChanceDigits"]!.GetValue<int>()!=6)throw new InvalidDataException("Strategic calculation convention changed.");
            foreach(var source in data["Sources"]!.AsArray())
            {
                string option=source!["Selection"]!["OptionSetName"]!.GetValue<string>();
                var current=profiles.Single(p=>p.Value.Audit["OptionSetName"]!.GetValue<string>()==option);
                foreach(var (oldName,key) in new[]{("Equilibrium","Equilibrium"),("ActionReport","Actions")})
                    if(source[oldName]!["Sha256"]!.GetValue<string>()!=current.Value.Audit["Inputs"]![key]!["Sha256"]!.GetValue<string>())throw new InvalidDataException("Strategic endpoint differs from revalidated profile.");
            }
            string id=new DirectoryInfo(Path.GetDirectoryName(dir)!).Name;string target=Path.Combine(output,id);Directory.CreateDirectory(target);
            foreach(var file in Directory.GetFiles(dir).Where(f=>f.EndsWith(".json")||f.EndsWith(".gz")))Files.CopyVerified(file,Path.Combine(target,Path.GetFileName(file)));
            foreach(var p in expectedDirections)available.Add(p.Id);
            records.Add(new{Pair=id,CaseIds=entry.CaseIds,Passed=true,FullEndpointsRevalidated=true,DecompressedOriginalHashesVerified=evidence,FreshCoalitionCalculations=0});
        }
        var pending=plan.Strategic.Where(p=>!available.Contains(p.Id)).ToArray();
        if(pending.Any(p=>profiles.ContainsKey(p.Source)&&profiles.ContainsKey(p.Target)))throw new InvalidDataException("Missing strategic computation for available endpoints; explicit computation is required.");
        Files.Save(Path.Combine(output,"validation.json"),new{Passed=true,CachedPairs=records,AvailableDirections=available.Count,ExpectedDirections=plan.Strategic.Length,Pending=pending,SolvesStarted=0,Validation="Verified original full diagnostics and exact complete endpoint identities; cached coalition computations were not rerun."});
    }
}
