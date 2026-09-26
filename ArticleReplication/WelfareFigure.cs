using System.Text.Json.Nodes;

namespace ArticleReplication;

public static class WelfareFigure
{
    public static readonly string[] Measures=["MeritoriousPlaintiffShortfall","NonliableDefendantBurden","LiableDefendantExcessBurden","GrossOutcomeError","RealLitigationExpenditures"];
    public const string Preamble="""
\documentclass[10pt,tikz,border=5pt]{standalone}
\usepackage[T1]{fontenc}
\usepackage{lmodern,amsmath,booktabs,array,tabularx,microtype,tikz}
\usetikzlibrary{patterns,patterns.meta,shapes.geometric,arrows.meta,calc,positioning}
\begin{document}

""";
    public static string Mark(double x,double y,string rule,double probability=1,double diameter=3.5)
    {
        if(probability<=0||probability>1)throw new InvalidDataException("Marker probability outside (0,1].");
        double area=Math.PI*Math.Pow(diameter/2,2)*probability,width=.35*Math.Sqrt(probability),h=Math.Sqrt(area/2)-width/Math.Sqrt(2),halo=Math.Sqrt(probability);
        string prefix=$"\\begin{{scope}}[shift={{({x:F8},{y:F8})}}]";
        string path=rule=="american"?$"\\fill[black] (0pt,0pt) circle[radius={Math.Sqrt(area/Math.PI):F9}pt];":
            $"\\draw[draw=black,line width={width:F9}pt,line join=miter,miter limit=10,preaction={{draw=white,line width={halo:F9}pt,line join=miter}}] (0pt,{h:F9}pt)--({h:F9}pt,0pt)--(0pt,{-h:F9}pt)--({-h:F9}pt,0pt)--cycle;";
        return prefix+path+@"\end{scope}";
    }
    public static void Generate(ResolvedArticlePlan plan,Dictionary<string,(JsonObject Audit,JsonObject Profile)> profiles,string collection)
    {
        var main=plan.Cases.Where(c=>c.Family is "baseline" or "cost-multiplier").ToArray();
        if(main.Any(c=>!profiles.ContainsKey(c.Id)))throw new InvalidDataException("A main welfare case is missing; never substitute zero.");
        var cs=main.ToDictionary(c=>(c.AlphaP,c.CostMultiplier,c.FeeRule));var costs=plan.Settings.MainCostMultipliers.Order().ToArray();
        string[] labels=[@"Meritorious plaintiff\\shortfall",@"Nonliable defendant\\burden",@"Liable defendant\\excess burden",@"Gross outcome\\error",@"Real litigation\\expenditures"];
        var lines=new List<string>{@"\begin{tikzpicture}[font=\small]"};var values=new List<object>();
        double Measure(string id,string measure)=>profiles[id].Audit["Welfare"]!["Headline"]![measure]!.GetValue<double>();
        for(int ri=0;ri<2;ri++)
        {
            double top=13-ri*7+(2-ri)*(costs.Length-5)*.83;
            lines.Add($"\\node[anchor=west,font=\\bfseries] at (1.0,{top+.6:G17}) {{{(ri==0?"Risk neutral":"Risk averse")}}};");
            for(int j=0;j<Measures.Length;j++)
            {
                string field=Measures[j];double x0=1.15+j*3.15,bound=main.Max(c=>Measure(c.Id,field))*1.08;
                double step=bound>0?Math.Pow(10,Math.Floor(Math.Log10(bound)))/2:1;bound=Math.Ceiling(bound/step)*step;if(bound==0)bound=1;
                lines.Add($"\\node[align=center,font=\\footnotesize] at ({x0+1.35:G17},{top-.05:G17}) {{{labels[j]}}};");
                for(int i=0;i<costs.Length;i++)
                {
                    double cost=costs[i],y=top-1-i*.83;
                    if(j==0)lines.Add($"\\node[anchor=east] at ({x0-.18:G17},{y:G17}) {{$\\times {cost:G}$}};");
                    lines.Add($"\\fill[black!{(i%2==0?4:0)}] ({x0:G17},{y-.35:G17}) rectangle ({x0+2.7:G17},{y+.35:G17});");
                    foreach(string rule in new[]{"complete","american"})
                    {
                        string id=cs[(ri==0?0:2,cost,rule)].Id;double value=Measure(id,field);
                        lines.Add(Mark(x0+2.7*value/bound,y+(rule=="american"?.11:-.11),rule));
                        values.Add(new{Case=id,Measure=field,Value=value,AxisMaximum=bound});
                    }
                }
                double axisY=top-4.75-(costs.Length-5)*.83;lines.Add($"\\draw ({x0:G17},{axisY:G17})--({x0+2.7:G17},{axisY:G17});");
                foreach(double frac in new[]{0,.5,1})lines.Add($"\\node[anchor={(frac==0?"north west":frac==1?"north east":"north")},font=\\footnotesize,inner xsep=0pt] at ({x0+2.7*frac:G17},{axisY-.08:G17}) {{{bound*frac:F2}}};");
            }
        }
        lines.AddRange([Mark(5.5,.3,"american"),@"\node[anchor=west] at (5.85,.3) {American};",Mark(10,.3,"complete"),@"\node[anchor=west] at (10.3,.3) {British};",@"\end{tikzpicture}",@"\end{document}"]);
        string dir=Path.Combine(collection,"Figures/Sources");Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir,"Figure 7 - Welfare outcomes.tex"),Preamble+string.Join('\n',lines));
        Files.Save(Path.Combine(dir,"Figure 7 - Welfare outcomes.generated-data.json"),new{PlottedValues=values,MainProfiles=main.Length,CostMultipliers=costs,Source="C# article plan and freshly validated profiles"});
    }
}
