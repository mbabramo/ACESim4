using ACESim;
using LitigCharts;
using System.Text.Json.Nodes;

namespace ArticleReplication;

/// <summary>Approved article presentation, populated exclusively from newly revalidated profiles/replays.</summary>
public static class MainFigures
{
    public static readonly string[] Stems=["Figure 1 - Information structure","Figure 3 - Dispositions","Figure 4 - Participation and offers","Figure 5 - Risk-averse dispositions","Figure 6 - Risk-averse participation and offers"];
    public static bool Owns(string relative)=>Stems.Append(WorkedFigure.Stem).Any(s=>relative.StartsWith("Figures/Sources/"+s+".",StringComparison.Ordinal));
    public static async Task FromValidatedRun(string run,string output,bool tablesOnly=false)
    {
        if(Directory.Exists(output))throw new IOException("Use a fresh figures directory.");
        if(Files.Object(Path.Combine(run,"primary-validation.json"))["Passed"]?.GetValue<bool>()!=true)throw new InvalidDataException("Primary run did not pass.");
        var plan=Files.Read<ResolvedArticlePlan>(Path.Combine(run,"resolved-plan.json"));
        var profiles=plan.Cases.Where(c=>Directory.Exists(Path.Combine(run,"ReportResults/Primary",c.Id)))
            .ToDictionary(c=>c.Id,c=>{
                string dir=Path.Combine(run,"ReportResults/Primary",c.Id);var audit=Files.Object(Path.Combine(dir,"validation.json"));
                if(audit["Passed"]?.GetValue<bool>()!=true)throw new InvalidDataException("Failed primary profile.");
                var outputs=audit["Outputs"]!.AsArray();
                string oldReplay=outputs.Single(f=>Files.LegacyBaseName(f!["Path"]!.GetValue<string>())=="replayed-report.csv")!["Path"]!.GetValue<string>().Replace('\\','/');
                string oldRoot=oldReplay[..^"replayed-report.csv".Length];
                foreach(var f in outputs)
                {
                    string path=f!["Path"]!.GetValue<string>().Replace('\\','/');
                    if(!path.StartsWith(oldRoot,StringComparison.Ordinal)||Files.Sha(Files.Under(dir,path[oldRoot.Length..]))!=f["Sha256"]!.GetValue<string>())
                        throw new InvalidDataException("Changed validated source.");
                }
                return(Audit:audit,Profile:Files.Object(Directory.GetFiles(Path.Combine(dir,"Sources/Profiles"),"*.json").Single()));
            });
        Directory.CreateDirectory(output);
        if(tablesOnly){MainTables.Generate(plan,profiles,output);await MainTables.Compile(output);return;}
        Generate(plan,profiles,output,run);
        await WorkedFigure.Generate(plan,profiles,output);
        foreach(string stem in Stems.Append(WorkedFigure.Stem))
        {
            string source=Path.Combine(output,"Figures/Sources",stem+".tex"),render=Path.Combine(output,"render",stem);Directory.CreateDirectory(render);
            await Commands.Run(Path.Combine(output,"logs"),stem,"lualatex",["-interaction=nonstopmode","-halt-on-error","-output-directory="+render,source],render);
            Files.CopyVerified(Path.Combine(render,stem+".pdf"),Path.Combine(output,"Figures",stem+".pdf"));
            await Commands.Run(Path.Combine(output,"logs"),stem+"-preview","pdftoppm",["-png","-singlefile","-r","110",Path.Combine(render,stem+".pdf"),Path.Combine(output,"Figures",stem)],render);
        }
        Files.Save(Path.Combine(output,"completed.json"),new{Passed=true,Figures=Stems.Append(WorkedFigure.Stem),SourcePrimaryValidationSha256=Files.Sha(Path.Combine(run,"primary-validation.json")),VisualReviewPending=true});
    }
    public static void Generate(ResolvedArticlePlan plan,Dictionary<string,(JsonObject Audit,JsonObject Profile)> profiles,string collection,string work)
    {
        var baseline=plan.Cases.Single(c=>c.Family=="baseline"&&c.FeeRule=="american"&&c.AlphaP==0&&c.AlphaD==0);
        var signal=ArticleSignalDiagrams.Generate("Baseline",true,FinalArticleCaseFactory.Create(baseline)).Single(d=>d.FileStem=="Continuous merits - party - bw");
        Save(Stems[0],signal.Latex,new{Case=baseline,signal.Panels},signal.Description);
        for(int risk=0;risk<2;risk++)
        {
            var cases=plan.Cases.Where(c=>c.Family=="baseline"&&c.AlphaP==2*risk&&c.AlphaD==2*risk).ToDictionary(c=>c.FeeRule);
            if(cases.Count!=2||cases.Values.Any(c=>!profiles.ContainsKey(c.Id)))throw new InvalidDataException("Main figures require both complete baseline profiles.");
            Strategy(risk==0?Stems[2]:Stems[4],risk,cases);
            Dispositions(risk==0?Stems[1]:Stems[3],cases);
        }
        void Save(string stem,string latex,object data,string caption)
        {
            string directory=Path.Combine(collection,"Figures/Sources");Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,stem+".tex"),latex);
            Files.Save(Path.Combine(directory,stem+".generated-data.json"),data);
            File.WriteAllText(Path.Combine(collection,"Figures",stem+".txt"),caption+"\n");
        }
        void Strategy(string stem,int risk,Dictionary<string,FinalArticleCase> cases)
        {
            var rows=new List<(string[] Decisions,string[] Titles,bool Offers)>{(new[]{"PFile","DAnswer"},new[]{"Filing","Answering"},false)};
            // Omit redundant commitment panels only after verifying every reached commitment is to continue.
            bool allContinue=cases.Values.All(c=>profiles[c.Id].Profile["Strategies"]!.AsArray().Where(r=>r!["Decision"]!.GetValue<string>() is "PAbandon" or "DDefault")
                .Where(r=>r!["Reach"]!.GetValue<double>()>0).All(r=>r!["Probabilities"]![r["Actions"]!.AsArray().Select(a=>a!.GetValue<string>()).ToList().IndexOf("Yes")]!.GetValue<double>()==0));
            if(risk==1||!allContinue)rows.Add((new[]{"PAbandon","DDefault"},new[]{"Commit to abandon","Commit to default"},false));
            rows.Add((new[]{"PAgreeToBargain","DAgreeToBargain"},new[]{"Agree to bargain: plaintiff","Agree to bargain: defendant"},false));
            rows.Add((new[]{"POffer","DOffer"},new[]{"Plaintiff demand","Defendant offer"},true));
            var lines=new List<string>{@"\begin{tikzpicture}[font=\small]"};var points=new List<object>();var omitted=new List<object>();
            for(int ri=0;ri<rows.Count;ri++)for(int ci=0;ci<2;ci++)
            {
                var row=rows[ri];string decision=row.Decisions[ci];double x0=1+ci*8.5,y0=(rows.Count-1-ri)*4.7+1;
                lines.Add($"\\node[anchor=west,font=\\bfseries] at ({x0:G17},{y0+3.45:G17}) {{({(char)('a'+ri*2+ci)}) {row.Titles[ci]}}};");
                lines.Add($"\\begin{{scope}}[shift={{({x0:G17},{y0:G17})}},x=6.7cm,y=2.95cm]");
                foreach(double y in new[]{0,.25,.5,.75,1}){lines.Add($"\\draw[black!15] (0,{y:G})--(1,{y:G});");lines.Add($"\\node[anchor=east,font=\\footnotesize] at (-.025,{y:G}) {{{y:G}}};");}
                lines.Add(@"\draw (0,1)--(0,0)--(1,0);");
                for(int s=1;s<=baseline.Signals;s++)lines.Add($"\\node[anchor=north,font=\\scriptsize] at ({(s-.5)/baseline.Signals:F2},-.04) {{{(s-.5)/baseline.Signals:F2}}};");
                foreach(string rule in new[]{"american","complete"})foreach(var r in profiles[cases[rule].Id].Profile["Strategies"]!.AsArray().Where(r=>r!["Decision"]!.GetValue<string>()==decision))
                {
                    double reach=r!["Reach"]!.GetValue<double>();if(reach==0){omitted.Add(new{Rule=rule,Decision=decision,Row=r});continue;}
                    if(!double.IsFinite(reach)||reach<0)throw new InvalidDataException("Invalid information-set reach.");
                    int s=r["Signal"]!.GetValue<int>();double x=(s-.5)/cases[rule].Signals;var actions=r["Actions"]!.AsArray().Select(a=>a!.GetValue<string>()).ToArray();var probabilities=r["Probabilities"]!.AsArray().Select(a=>a!.GetValue<double>()).ToArray();
                    for(int ai=0;ai<actions.Length;ai++)
                    {
                        if(row.Offers?probabilities[ai]<=0:actions[ai]!="Yes")continue;
                        double pr=probabilities[ai],y=row.Offers?double.Parse(actions[ai]):pr,weight=row.Offers?pr:1;
                        lines.Add(WelfareFigure.Mark(x,y,rule,weight,5));
                        points.Add(new{Rule=rule,Decision=decision,Signal=s,OwnExit=r["OwnExit"],Action=actions[ai],Probability=pr,Reach=reach,X=x,Y=y,MarkerAreaWeight=weight,MarkerAreaPt2=Math.PI*6.25*weight});
                    }
                }
                lines.Add(@"\end{scope}");lines.Add($"\\node at ({x0+3.35:G17},{y0-.67:G17}) {{{(ci==0?"Plaintiff":"Defendant")} signal}};");
                lines.Add($"\\node[rotate=90] at ({x0-1.03:G17},{y0+1.48:G17}) {{{(row.Offers?"Offer":"Probability")}}};");
            }
            lines.AddRange([WelfareFigure.Mark(1.25,-.45,"american",1,5),@"\node[anchor=west] at (1.5,-.45) {American};",WelfareFigure.Mark(4,-.45,"complete",1,5),@"\node[anchor=west] at (4.25,-.45) {British};",@"\node[anchor=west,font=\footnotesize,align=left] at (6.2,-.45) {For mixed offer strategies: area $\propto$ action probability.};",@"\end{tikzpicture}",@"\end{document}"]);
            Save(stem,WelfareFigure.Preamble+string.Join('\n',lines),new{Cases=cases.Values.Select(c=>c.Id),DisplayedPoints=points,OmittedZeroReach=omitted,AllReachedCommitmentsContinue=allContinue},"American (filled circles) and British (open diamonds). Blank positions are unreached. All reached positive-probability offer actions are displayed. Complete policies remain in the individual simulation reports. Equal probability has equal marker area across shapes, including the diamond's black outer stroke and excluding the white halo.");
        }
        void Dispositions(string stem,Dictionary<string,FinalArticleCase> cases)
        {
            string[] labels=["Not filed","Not answered","Settled","P abandons","D defaults","P loses at trial","P wins at trial"];
            string[] styles=[@"preaction={fill=white,draw=none},pattern={Dots[distance=4pt,radius=.35pt]},pattern color=black",@"preaction={fill=black,draw=none},pattern={Dots[distance=2.8pt,radius=.35pt]},pattern color=white","fill=black!50",@"preaction={fill=white,draw=none},pattern=north east lines,pattern color=black",@"preaction={fill=black,draw=none},pattern=north east lines,pattern color=white","fill=white","fill=black"];
            var data=new List<object>();var lines=new List<string>{@"\begin{tikzpicture}[font=\small]"};
            for(int v=0;v<=100;v+=20){double x=2+v*.13;lines.Add($"\\draw[black!15] ({x:G17},.5)--({x:G17},2.4);");lines.Add($"\\node[anchor=north] at ({x:G17},.4) {{{v}\\%}};");}
            for(int k=0;k<2;k++)
            {
                string rule=k==0?"american":"complete",id=cases[rule].Id;var all=Reports.ReadCsv(Path.Combine(work,"ReportResults/Primary",id,"replayed-report.csv")).Single(r=>r["Filter"]=="All");double N(string key)=>double.Parse(all[key]);
                var values=new[]{N("PDoesntFile"),N("DDoesntAnswer"),N("SettlesBR1"),N("PAbandonsBR1"),N("DDefaultsBR1"),N("P Loses"),N("P Wins")};
                PublicationFigures.ValidateDisposition(values,N("Trial"));
                double y=2-k*.9,left=2;lines.Add($"\\node[anchor=east] at (1.8,{y:G17}) {{{(k==0?"American":"British")}}};");
                for(int j=0;j<values.Length;j++)
                {
                    double v=values[j],right=left+v*13;
                    if(v>0)lines.Add($"\\path[{styles[j]},draw=black,line width=.25pt] ({left:F8},{y-.26:G17}) rectangle ({right:F8},{y+.26:G17});");
                    if(v>=.035){bool dark=j is 1 or 4 or 6;lines.Add($"\\node[text={(dark?"white":"black")},fill={(dark?"black":"white")},font=\\footnotesize,inner sep=1pt] at ({(left+right)/2:F8},{y:G17}) {{{v*100:F1}}};");}
                    left=right;
                }
                data.Add(new{CaseId=id,Rule=rule,Values=labels.Zip(values).ToDictionary(x=>x.First,x=>x.Second)});
            }
            lines.Add(@"\node at (8.5,-.25) {Potential disputes};");
            for(int j=0;j<labels.Length;j++){double x=(j%3)*5.1,y=-1-(j/3)*.57;lines.Add($"\\path[{styles[j]},draw=black,line width=.25pt] ({x:G17},{y-.13:G17}) rectangle ({x+.45:G17},{y+.13:G17});");lines.Add($"\\node[anchor=west] at ({x+.56:G17},{y:G17}) {{{labels[j]}}};");}
            lines.AddRange([@"\end{tikzpicture}",@"\end{document}"]);
            Save(stem,WelfareFigure.Preamble+string.Join('\n',lines),new{Dispositions=data,PatternStyles=styles},"Terminal dispositions as shares of potential disputes. White-background patterns denote pro-defendant outcomes; black-background patterns denote pro-plaintiff outcomes; settlements are gray. Agreement refusal is not an additional terminal disposition.");
        }
    }
}
