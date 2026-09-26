using System.Text.Json.Nodes;

namespace ArticleReplication;

/// <summary>Complete saved policies, including deterministic off-path completions. No display cutoff.</summary>
public static class StrategyExhibit
{
    private static string G(double x)=>x.ToString("G17");
    private static double D(JsonNode? x)=>x!.GetValue<double>();
    private static string S(JsonNode? x)=>x!.GetValue<string>();
    private static string Print(JsonNode? x)=>x==null?"undefined":D(x).ToString("G4").ToLowerInvariant();
    private static string Tex(string value)=>string.Concat(value.Select(c=>c switch{'\\'=>@"\textbackslash{}",'&'=>@"\&",'%'=>@"\%",'$'=>@"\$",'#'=>@"\#",'_'=>@"\_",'{'=>@"\{",'}'=>@"\}",_=>c.ToString()}));
    public static string Generate(JsonObject audit,JsonObject profile)
    {
        var c=audit["Case"]!;var metrics=profile["Metrics"]!;var rows=profile["Strategies"]!.AsArray().Select(x=>x!.AsObject()).ToArray();
        JsonObject[] Select(string decision,int? exit=null)
        {
            var selected=rows.Where(r=>S(r["Decision"])==decision&&(r["OwnExit"]?.GetValue<int>())==exit).OrderBy(r=>r["Signal"]!.GetValue<int>()).ToArray();
            if(selected.Length!=c["Signals"]!.GetValue<int>()||selected.Select(r=>r["Signal"]!.GetValue<int>()).Distinct().Count()!=selected.Length)throw new InvalidDataException("Incomplete policy panel: "+decision);
            return selected;
        }
        string Plot(JsonObject[] rs,string heading)
        {
            var reached=new List<string>();var off=new List<string>();
            foreach(var r in rs)
            {
                if(!r["Actions"]!.AsArray().Select(S).SequenceEqual(new[]{"Yes","No"}))throw new InvalidDataException("Expected explicit Yes/No labels.");
                (D(r["Reach"])>0?reached:off).Add($"({r["Signal"]},{G(D(r["Probabilities"]![0]))})");
            }
            return @"\begin{tikzpicture}\begin{axis}["+$"width=4.85in,height=2.5in,title={{{Tex(heading)}}},xmin=.5,xmax={rs.Length+.5:G},ymin=0,ymax=1,xtick={{1,...,{rs.Length}}},ytick={{0,.25,.5,.75,1}},"+
                @"xlabel={Private signal},ylabel={Probability of Yes},grid=major,tick label style={font=\small},label style={font=\small},title style={font=\normalsize},clip=false]"+"\n"+
                @"\addplot[only marks,mark=*,mark size=2pt,color=navy] coordinates {"+string.Join(' ',reached)+"};\n"+
                @"\addplot[only marks,mark=o,mark size=2.4pt,color=gray] coordinates {"+string.Join(' ',off)+"};\n"+@"\end{axis}\end{tikzpicture}";
        }
        double[] offers=c["Offers"]!.AsArray().Select(D).ToArray();
        string Matrix(JsonObject[] rs,string heading,bool wide=false)
        {
            int n=rs.Length,m=offers.Length;var lines=new List<string>{@"\begin{tikzpicture}\begin{axis}[",
                $"width={(wide?"10.0":"4.85")}in,height=2.55in,title={{{Tex(heading)}}},",
                $"xmin=.5,xmax={m+.5:G},ymin=.5,ymax={n+.5:G},y dir=reverse,",
                $"xtick={{1,...,{m}}},xticklabels={{{string.Join(',',offers.Select(v=>v.ToString("G3")))}}},",
                $"ytick={{1,...,{n}}},yticklabels={{{string.Join(',',rs.Select(r=>r["Signal"]!.ToString()+(D(r["Reach"])==0?"*":"")))}}},",
                @"xlabel={Offer (damages = 1)},ylabel={Private signal},",
                @"tick label style={font=\scriptsize},label style={font=\small},title style={font=\normalsize},",
                @"colormap={policy}{color(0cm)=(white);color(1cm)=(navy)},point meta min=0,point meta max=1,axis on top,clip=false]",
                @"\addplot[matrix plot*,mesh/cols="+m+@",point meta=explicit] table[meta=p] {","x y p"};
            for(int y=0;y<n;y++)
            {
                var r=rs[y];var p=r["Probabilities"]!.AsArray();var labels=r["Actions"]!.AsArray().Select(a=>double.Parse(S(a))).ToArray();
                if(p.Count!=m||!labels.SequenceEqual(offers.Select(v=>double.Parse(v.ToString("F2")))))throw new InvalidDataException("Complete declared offer support changed.");
                for(int x=0;x<m;x++)lines.Add($"{x+1} {y+1} {G(D(p[x]))}");
            }
            lines.Add("};");
            for(int y=0;y<n;y++)for(int x=0;x<m;x++)
            {
                double p=D(rs[y]["Probabilities"]![x]);if(p==0)continue;string label=p.ToString("G4").ToLowerInvariant();
                if(label.Contains('e')){var bits=label.Split('e');label="$"+bits[0]+@"\!\times\!10^{"+int.Parse(bits[1])+"}$";}
                lines.Add(@"\node[font="+(m>10&&!wide?@"\tiny":@"\scriptsize")+",text="+(p>=.55?"white":"black")+$"] at (axis cs:{x+1},{y+1}) {{{label}}};");
            }
            lines.Add(@"\end{axis}\end{tikzpicture}");return string.Join('\n',lines);
        }
        double ap=D(c["AlphaP"]),ad=D(c["AlphaD"]);
        string risk=ap==0&&ad==0?"Risk neutral":ap==ad?$"Symmetric risk aversion (alpha={ap:G})":$"Plaintiff alpha={ap:G}; defendant alpha={ad:G}";
        string fee=S(c["FeeRule"]) switch{"american"=>"American","complete"=>"British",_=>"Trial-only fee shifting"};
        string family=c["IsExternalImport"]?.GetValue<bool>()==true?"Standard model":S(c["Family"])+" / "+S(c["Variant"]);
        string header=@"\noindent{\Large\bfseries "+Tex($"{family}; {fee}; {risk}; cost multiplier {D(c["CostMultiplier"]):G}")+@"}\par\smallskip"+"\n"+
            @"{\footnotesize Case: \texttt{"+Tex(S(c["Id"]))+@"}. Primary profile 1; agreement enabled.}\par\medskip"+"\n";
        var text=new List<string>{@"\documentclass[10pt]{article}",@"\usepackage[paperwidth=11in,paperheight=8.5in,margin=.4in,footskip=16pt]{geometry}",
            @"\usepackage[T1]{fontenc}\usepackage{lmodern}\usepackage{pgfplots}\usepackage{booktabs}",@"\pgfplotsset{compat=1.18}\definecolor{navy}{RGB}{25,69,102}",
            @"\pagestyle{plain}\setlength{\parindent}{0pt}",@"\begin{document}",header,
            @"{\large\bfseries Filing, answering and private exit commitments}\par\smallskip",
            @"Filled dots: reached conditional policy. Hollow gray dots: saved off-path policy; the conditional event is undefined.\par",
            @"Exit commitments occur privately before the simultaneous agreement decisions.\par\medskip",@"\begin{tabular}{@{}cc@{}}",
            Plot(Select("PFile"),"Plaintiff: file")+"&"+Plot(Select("DAnswer"),"Defendant: answer given filing")+@"\\",
            Plot(Select("PAbandon"),"Plaintiff: commit to abandon if bargaining fails")+"&"+Plot(Select("DDefault"),"Defendant: commit to default if bargaining fails"),@"\end{tabular}\par\smallskip",
            @"{\small Filing: "+Print(metrics["Filing"])+"; joint filing/answering: "+Print(metrics["JointFileAnswer"])+"; answering given filing: "+Print(metrics["AnsweringGivenFiling"])+".}\n",@"\newpage",header,
            @"{\large\bfseries Agreement conditional on private signal and own exit commitment}\par\smallskip",
            @"Both parties choose without observing the other party\textquotesingle s agreement choice or private exit commitment.\par",
            @"Offers occur only if both agree. Refusal activates the original commitment-dependent outcomes.\par\medskip",@"\begin{tabular}{@{}cc@{}}",
            Plot(Select("PAgreeToBargain",1),"Plaintiff agrees | committed to abandon")+"&"+Plot(Select("DAgreeToBargain",1),"Defendant agrees | committed to default")+@"\\",
            Plot(Select("PAgreeToBargain",2),"Plaintiff agrees | committed to continue")+"&"+Plot(Select("DAgreeToBargain",2),"Defendant agrees | committed to contest"),@"\end{tabular}\par\smallskip",
            @"{\small Joint agreement-stage reach: "+rows.Where(r=>S(r["Decision"])=="PAgreeToBargain").Sum(r=>D(r["Reach"])).ToString("G4")+@". Conditional joint outcomes: both agree "+Print(metrics["BothAgreeGivenStage"])+"; only P declines "+Print(metrics["OnlyPlaintiffDeclinesGivenStage"])+"; only D declines "+Print(metrics["OnlyDefendantDeclinesGivenStage"])+"; both decline "+Print(metrics["BothDeclineGivenStage"])+@". These use the full joint signal/history distribution. Hollow dots retain saved off-path policies.}"};
        string[] offerHeader=[@"\newpage",header,@"{\large\bfseries Complete mixed offer distributions}\par\smallskip",@"Rows show the saved policy at each private signal and own commitment, after both parties agree to bargain.\par",@"An asterisk marks an unreachable information set: its saved completion is shown, but no reached conditional distribution exists.\par\medskip"];
        string[] legend=[@"{\small Color and cell labels show action probabilities (white = 0; darkest blue = 1). Empty cell labels mean exact zero.",@"Every positive entry is printed, including arbitrarily small probabilities. Full precision is retained in the editable TeX and profile JSON.}"];
        if(offers.Length>10)foreach(var (d,l,first,second) in new[]{("POffer","Plaintiff","abandon","continue"),("DOffer","Defendant","default","contest")})
        {text.AddRange(offerHeader);text.Add(Matrix(Select(d,1),l+" | committed to "+first,true));text.Add(@"\par\medskip");text.Add(Matrix(Select(d,2),l+" | committed to "+second,true));text.Add(@"\par\smallskip");text.AddRange(legend);}
        else{text.AddRange(offerHeader);text.Add(@"\begin{tabular}{@{}cc@{}}");text.Add(Matrix(Select("POffer",1),"Plaintiff | committed to abandon")+"&"+Matrix(Select("DOffer",1),"Defendant | committed to default")+@"\\");text.Add(Matrix(Select("POffer",2),"Plaintiff | committed to continue")+"&"+Matrix(Select("DOffer",2),"Defendant | committed to contest"));text.Add(@"\end{tabular}\par\smallskip");text.AddRange(legend);}
        text.Add(@"\end{document}");return string.Join('\n',text);
    }
}
