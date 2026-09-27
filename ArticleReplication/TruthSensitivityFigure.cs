namespace ArticleReplication;

public static class TruthSensitivityFigure
{
    public const string Stem="Truth-formula robustness";
    public static string Generate(string csv,string collection)
    {
        var rows=Reports.ReadCsv(csv);var measures=WelfareFigure.Measures.Take(4).ToArray();
        var costs=rows.Select(r=>double.Parse(r["CostMultiplier"])).Distinct().Order().ToArray();
        double[] ks=[.5,1,2];
        if(!rows.Select(r=>double.Parse(r["Exponent"])).Distinct().Order().SequenceEqual(ks))throw new InvalidDataException("This figure requires the approved 0.5, 1, 2 maps.");
        string[] labels=[@"Meritorious plaintiff\\shortfall",@"Nonliable defendant\\burden",@"Liable defendant\\excess burden",@"Gross outcome\\error"];
        var lines=new List<string>{WelfareFigure.Preamble,@"\begin{tikzpicture}[font=\small]"};
        string Mark(double x,double y,double k)=>k==1?$"\\fill ({x:F7},{y:F7}) circle[radius=1.8pt];":k==.5?$"\\draw[fill=white,line width=.55pt] ({x:F7},{y:F7}) circle[radius=1.8pt];":$"\\node[diamond,draw,fill=white,inner sep=0pt,minimum size=4.5pt,line width=.55pt] at ({x:F7},{y:F7}) {{}};";
        for(int panel=0;panel<2;panel++)
        {
            string risk=panel==0?"Risk neutral":"Risk averse";double top=(1-panel)*(costs.Length*.70+2.05)+2.3+costs.Length*.70;
            lines.Add($"\\node[anchor=west,font=\\bfseries] at (.9,{top+.65:F5}) {{{risk}}};");
            for(int j=0;j<4;j++)
            {
                double x0=1.25+j*3.65,width=3.0;
                var vals=rows.Where(r=>r["Measure"]==measures[j]).Select(r=>double.Parse(r["BritishMinusAmerican"])).ToArray();
                double lo=Math.Min(0,vals.Min()),hi=Math.Max(0,vals.Max()),pad=Math.Max(.0001,(hi-lo)*.12);lo-=pad;hi+=pad;
                double X(double v)=>x0+width*(v-lo)/(hi-lo);
                lines.Add($"\\node[align=center,font=\\footnotesize] at ({x0+width/2:F5},{top:F5}) {{{labels[j]}}};");
                for(int i=0;i<costs.Length;i++)
                {
                    double y=top-.75-i*.70,cost=costs[i];
                    if(j==0)lines.Add($"\\node[anchor=east] at ({x0-.20:F5},{y:F5}) {{$\\times {cost:G}$}};");
                    lines.Add($"\\fill[black!{(i%2==0?4:0)}] ({x0:F5},{y-.29:F5}) rectangle ({x0+width:F5},{y+.29:F5});");
                    var v=ks.Select(k=>double.Parse(rows.Single(r=>r["Risk"]==risk&&double.Parse(r["CostMultiplier"])==cost&&r["Measure"]==measures[j]&&double.Parse(r["Exponent"])==k)["BritishMinusAmerican"])).ToArray();
                    lines.Add($"\\draw[black!55,line width=.5pt] ({X(v.Min()):F7},{y:F7})--({X(v.Max()):F7},{y:F7});");
                    foreach(int a in new[]{0,2,1})lines.Add(Mark(X(v[a]),y,ks[a]));
                }
                double bottom=top-.75-(costs.Length-1)*.70;
                lines.Add($"\\draw[black!40,densely dashed] ({X(0):F7},{top-.40:F5})--({X(0):F7},{bottom-.32:F5});");
                double axis=bottom-.53;lines.Add($"\\draw[black!50] ({x0:F5},{axis:F5})--({x0+width:F5},{axis:F5});");
                foreach(double tick in new[]{lo+pad,0,hi-pad}.Distinct().Order())
                    if(tick==0||Math.Abs(X(tick)-X(0))>.38)lines.Add($"\\draw ({X(tick):F7},{axis:F5})--++(0,-.06) node[below,font=\\scriptsize] {{{tick:0.###}}};");
            }
        }
        lines.Add(@"\node at (8.2,1.13) {British minus American};");
        foreach(var item in new[]{(3.0,.5,"Weaker link ($k=0.5$)"),(7.1,1.0,"Baseline ($k=1$)"),(10.9,2.0,"Stronger link ($k=2$)")}){lines.Add(Mark(item.Item1,.48,item.Item2));lines.Add($"\\node[anchor=west,font=\\footnotesize] at ({item.Item1+.15:F5},.48) {{{item.Item3}}};");}
        lines.AddRange([@"\end{tikzpicture}",@"\end{document}"]);
        string relative=$"Supplemental materials/Truth sensitivity/Sources/{Stem}.tex",path=Path.Combine(collection,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllLines(path,lines);return relative;
    }
    public static async Task FromRun(string run,string output)
    {
        if(Directory.Exists(output))throw new IOException("Fresh output required.");
        var validation=Files.Object(Path.Combine(run,"article/Results/Aggregated Data/Truth sensitivity/validation.json"));if(validation["Passed"]?.GetValue<bool>()!=true)throw new InvalidDataException("Validated truth results required.");
        string source=Generate(Path.Combine(run,"article/Results/Aggregated Data/Truth sensitivity/truth-sensitivity.csv"),output);
        await Rendering.Compile([new(source,$"Supplemental materials/Truth sensitivity/{Stem}.pdf","TruthSensitivity")],output,output,1);
    }
}
