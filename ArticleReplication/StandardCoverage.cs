using ACESim;
using LitigCharts;
using System.Text.Json.Nodes;

namespace ArticleReplication;

/// <summary>Independent coverage and filing audit of the standard reporting command's receipt.</summary>
public static class StandardCoverage
{
    public static void Validate(string requestFile,string output)
    {
        var request=Files.Read<FinalArticleResultsCommand.Request>(requestFile);
        string inventoryFile=Path.Combine(request.ResultsDirectory,"Run records/standard-diagram-inventory.json");
        var inventory=Files.Object(inventoryFile);
        if(inventory["Passed"]?.GetValue<bool>()!=true)throw new InvalidDataException("Standard rendering did not complete.");
        string Key(string kind,IEnumerable<string> ids)=>kind+"|"+string.Join("|",ids.Order(StringComparer.Ordinal));
        var expected=new Dictionary<string,int>();
        void Expect(string kind,IEnumerable<FinalArticleCase> cases,int count=1)
        { string key=Key(kind,cases.Select(c=>c.Id));expected[key]=expected.GetValueOrDefault(key)+count; }
        foreach(var c in request.Cases)Expect("standard-individual",[c.Case],6);
        foreach(var g in request.Cases.Select(c=>c.Case).GroupBy(c=>(Family:c.Family is "baseline" or "cost-multiplier"?"Baseline":c.Family+" - "+c.Variant,c.CostMultiplier)))
        {
            var risks=g.GroupBy(c=>(c.AlphaP,c.AlphaD)).ToArray();
            foreach(var risk in risks){Expect("standard-welfare",risk);Expect("standard-dispositions",risk);Expect("standard-participation",risk);}
            if(risks.Length>1){Expect("standard-welfare",g);Expect("standard-dispositions",g);}
        }
        foreach(var g in request.PlannedCases.GroupBy(c=>(c.Distribution,c.PartySigma,c.CourtSigma,c.Signals)))
            Expect("standard-signals",g,g.Key.Distribution=="direct-binary"?8:10);
        Expect("standard-structure",[],5);
        var actual=new Dictionary<string,int>();var files=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var checkedFiles=new List<object>();
        var artifacts=inventory["Artifacts"]!.AsArray();
        foreach(var a in artifacts)
        {
            string kind=a!["Kind"]!.GetValue<string>();string[] ids=a["CaseIds"]!.AsArray().Select(i=>i!.GetValue<string>()).ToArray();
            if(ids.Distinct().Count()!=ids.Length)throw new InvalidDataException("Duplicate diagram case binding.");
            string key=Key(kind,ids);actual[key]=actual.GetValueOrDefault(key)+1;
            string source=Path.GetFullPath(a["Source"]!.GetValue<string>()),pdf=Path.GetFullPath(a["Output"]!.GetValue<string>()),png=Path.GetFullPath(a["Preview"]!.GetValue<string>());
            string root=kind switch
            {
                "standard-individual"=>Path.Combine(request.ResultsDirectory,"Individual simulations",ids.Single()),
                "standard-welfare" or "standard-dispositions" or "standard-participation"=>Path.Combine(request.ResultsDirectory,"Aggregated Data"),
                "standard-signals"=>Path.Combine(request.SupplementalDirectory,"Liability signals diagrams"),
                "standard-structure"=>Path.Combine(request.SupplementalDirectory,"Game tree diagrams/Standard"),
                _=>throw new InvalidDataException("Unexpected standard diagram kind.")
            };
            string sourceDir=Path.GetDirectoryName(source)!;
            if(Path.GetFileName(sourceDir)!="Sources"||!source.StartsWith(Path.GetFullPath(root).TrimEnd('\\','/')+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)
                ||!pdf.Equals(ArticleResultsLayout.RenderedArtifact(source,".pdf"),StringComparison.OrdinalIgnoreCase)
                ||!png.Equals(ArticleResultsLayout.RenderedArtifact(source,".png"),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Diagram filed outside its expected folder: "+source);
            foreach(var (file,hashKey) in new[]{(source,"SourceSha256"),(pdf,"OutputSha256"),(png,"PreviewSha256")})
            {
                if(!files.Add(file)||!Files.Sha(file).Equals(a[hashKey]!.GetValue<string>(),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Missing, duplicate or changed standard artifact: "+file);
                checkedFiles.Add(new{Path=file,Sha256=Files.Sha(file),Bytes=new FileInfo(file).Length});
            }
            using(var s=File.OpenRead(pdf)){byte[] b=new byte[5];if(s.Read(b)!=5||!b.SequenceEqual("%PDF-"u8.ToArray()))throw new InvalidDataException("Invalid PDF: "+pdf);}
            using(var s=File.OpenRead(png)){byte[] b=new byte[8];if(s.Read(b)!=8||!b.SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))throw new InvalidDataException("Invalid PNG: "+png);}
            if(!File.Exists(Path.Combine(Path.GetDirectoryName(pdf)!,"Standard reports.md")))throw new InvalidDataException("Missing folder index.");
        }
        if(expected.Count!=actual.Count||expected.Any(kv=>actual.GetValueOrDefault(kv.Key)!=kv.Value))throw new InvalidDataException("Standard case/kind coverage differs from the resolved plan.");
        string[] pending=request.PlannedCases.Select(c=>c.Id).Except(request.Cases.Select(c=>c.Case.Id)).Order().ToArray();
        if(!pending.SequenceEqual(inventory["PendingCases"]!.AsArray().Select(x=>x!.GetValue<string>()).Order()))throw new InvalidDataException("Missing cases were hidden or misreported.");
        // Compare displayed signal marginals with production conditional beliefs (existing 1e-7 display criterion).
        int signalChecks=0;
        foreach(var group in artifacts.Where(a=>a!["Kind"]!.GetValue<string>()=="standard-signals").GroupBy(a=>Path.GetDirectoryName(a!["Source"]!.GetValue<string>())))
        foreach(string suffix in new[]{" - color"," - bw"})
        {
            JsonArray Panels(string tag)=>Files.Object(Path.ChangeExtension(group.Single(a=>Path.GetFileNameWithoutExtension(a!["Source"]!.GetValue<string>()).EndsWith(tag+suffix,StringComparison.Ordinal))!["Source"]!.GetValue<string>(),".json"))["Panels"]!.AsArray();
            var forward=Panels(" - party").Single(p=>p!["DestinationTitle"]!.GetValue<string>().StartsWith("Party signal",StringComparison.Ordinal))!["JointMass"]!.AsArray();
            var joint=Panels(" - party to party").Single()!["JointMass"]!.AsArray();
            for(int i=0;i<joint.Count;i++)
            {
                double marginal=forward.Sum(row=>row![i]!.GetValue<double>()),conditional=joint[i]!.AsArray().Sum(x=>x!.GetValue<double>());
                if(Math.Abs(marginal-conditional)>1e-7)throw new InvalidDataException("Signal marginal disagrees with production beliefs.");
                signalChecks++;
            }
        }
        Files.Save(output,new{Passed=true,CheckedUtc=DateTime.UtcNow,RequestSha256=Files.Sha(requestFile),InventorySha256=Files.Sha(inventoryFile),ExpectedArtifacts=expected.Values.Sum(),ArtifactCount=artifacts.Count,Files=checkedFiles,SignalMarginalChecks=signalChecks,PendingCases=pending,VisualReviewRequired=true,SolvesStarted=0});
        Console.WriteLine($"Standard coverage passed: {artifacts.Count} diagrams, {checkedFiles.Count} filed source/PDF/PNG files, {signalChecks} signal checks.");
    }
}
