using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LitigCharts;

/// <summary>Four panels with separate offer strips and unpooled continue/exit histories.</summary>
public static class ArticleStrategyFigures
{
    public sealed record CaseData(string Label, string OptionSet, string Source, string Sha256,
        PublicationFigures.StrategyPoint[] Filing, PublicationFigures.StrategyPoint[] Answering,
        PublicationFigures.StrategyPoint[] DemandContinue, PublicationFigures.StrategyPoint[] DemandExit,
        PublicationFigures.StrategyPoint[] OfferContinue, PublicationFigures.StrategyPoint[] OfferExit);
    public sealed record Figure(string Latex, CaseData[] Cases, string Caption);
    private static string N(double x) => x.ToString("0.######", CultureInfo.InvariantCulture);
    private static string Style(int s) => s switch { 0 => "solid", 1 => "dashed", _ => "densely dotted" };
    public static Figure Generate(PublicationFigures.StrategyCase[] cases)
    {
        if (cases.Length is < 1 or > 3 || cases.Select(c => c.Label).Distinct().Count() != cases.Length)
            throw new InvalidDataException("Expected one to three distinctly labeled fee rules.");
        var data = cases.Select(c =>
        {
            var rows = PublicationFigures.ReadCsv(c.ActionReport);
            var grid = PublicationFigures.GridForOptionSet(c.OptionSetName);
            PublicationFigures.StrategyPoint[] Points(string d, int? exit = null) =>
                PublicationFigures.BuildStrategySeries(rows, c, d, grid.SignalCount, grid.Offers, exit);
            return new CaseData(c.Label, c.OptionSetName, Path.GetFullPath(c.ActionReport),
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(c.ActionReport))),
                Points("P Files"), Points("D Answers"), Points("P Offer", 2), Points("P Offer", 1),
                Points("D Offer", 2), Points("D Offer", 1));
        }).ToArray();
        return new(Render(data), data, Caption);
    }
    private static string Shape(int series) => series switch { 0 => "circle", 1 => "rectangle", _ => "diamond,aspect=1" };
    private static void ParticipationMarker(StringBuilder b, int series, double x, double y)
    {
        double size = series switch { 0 => 3.5, 1 => 6.8, _ => 16 };
        b.AppendLine($@"\node[draw=black,fill={(series == 0 ? "black" : "white")},{Shape(series)},inner sep=0pt,minimum size={N(size)}pt,line width=.45pt] at ({N(x)},{N(y)}) {{}};");
    }
    private static void OfferMarker(StringBuilder b, int series, double x, double y, bool exit = false, double p = 1)
    {
        double size = 2.4 + 3.4 * Math.Sqrt(p);
        b.AppendLine($@"\node[draw=black,fill={(exit ? "white" : "black")},{Shape(series)},inner sep=0pt,minimum size={N(size)}pt,line width=.5pt] at ({N(x)},{N(y)}) {{}};");
    }
    private static string Render(CaseData[] data)
    {
        var b = new StringBuilder("""
            \documentclass[10pt,tikz,border=5pt]{standalone}
            \usepackage{lmodern}
            \usetikzlibrary{shapes.geometric}
            \begin{document}
            \begin{tikzpicture}[font=\small]
            """);
        for (int s = 0; s < data.Length; s++)
        {
            double x = .6 + s * 5.5;
            b.AppendLine($@"\draw[{Style(s)},line width=.7pt] ({N(x)},12.95)--({N(x+.65)},12.95);");
            ParticipationMarker(b,s,x+.325,12.95);
            b.AppendLine($@"\node[anchor=west] at ({N(x+.8)},12.95) {{{data[s].Label}}};");
        }
        for (int panel = 0; panel < 4; panel++)
        {
            bool offer = panel >= 2, plaintiff = panel % 2 == 0;
            double x0 = 1 + (panel % 2)*8.4;
            string title = panel switch {0=>"Filing",1=>"Answering",2=>"Plaintiff demand",_=>"Defendant offer"};
            b.AppendLine($@"\node[anchor=west,font=\bfseries] at ({N(x0)},{(offer?"6.0":"12.25")}) {{({(char)('a'+panel)}) {title}}};");
            int strips = offer ? data.Length : 1;
            for(int strip=0;strip<strips;strip++)
            {
                double height=offer? 3.9/strips : 3.9;
                double y0=offer? .85+(strips-strip-1)*(height+.36) : 7.75;
                b.AppendLine($@"\begin{{scope}}[shift={{({N(x0)},{N(y0)})}},x=6.4cm,y={N(height)}cm]");
                foreach(double tick in offer?new[]{0.0,.5,1.0}:new[]{0.0,.25,.5,.75,1.0})
                {
                    b.AppendLine($@"\draw[black!15] (0,{N(tick)})--(1,{N(tick)});");
                    b.AppendLine($@"\node[anchor=east,font=\footnotesize] at (-.018,{N(tick)}) {{{N(tick)}}};");
                }
                b.AppendLine(@"\draw (0,1)--(0,0)--(1,0);");
                if(offer) b.AppendLine($@"\node[anchor=south east,font=\scriptsize,fill=white,inner sep=1pt] at (1,1) {{{data[strip].Label}}};");
                if(!offer || strip==strips-1)
                {
                    foreach(var point in data[0].Filing)
                        b.AppendLine($@"\node[anchor=north,font=\footnotesize] at ({N(point.SignalValue)},{(offer?"-.04":"-.13")}) {{{point.SignalValue.ToString("0.00",CultureInfo.InvariantCulture)}}};");
                }
                if (offer)
                {
                    foreach (bool exit in new[] { false, true })
                    {
                        var points = plaintiff ? (exit ? data[strip].DemandExit : data[strip].DemandContinue) :
                            (exit ? data[strip].OfferExit : data[strip].OfferContinue);
                        foreach (var point in points.Where(p => !p.OffPath))
                        foreach (var action in point.Actions.Where(a => a.Probability > 0))
                            OfferMarker(b, strip, point.SignalValue + (exit ? .009 : -.009), action.Value, exit, action.Probability);
                    }
                }
                else
                {
                    // Draw every step first, then nest markers from largest to smallest at the exact coordinates.
                    for (int s = 0; s < data.Length; s++)
                    {
                        var points = plaintiff ? data[s].Filing : data[s].Answering;
                        for (int i = 0; i < points.Length; i++)
                        {
                            var point = points[i]; if (point.OffPath) continue;
                            double probability = point.Actions.Single(a => a.Value == 1).Probability;
                            double half = .5 / points.Length;
                            b.AppendLine($@"\draw[{Style(s)},line width=.6pt] ({N(point.SignalValue-half)},{N(probability)})--({N(point.SignalValue+half)},{N(probability)});");
                            if (i > 0 && !points[i-1].OffPath)
                                b.AppendLine($@"\draw[{Style(s)},line width=.6pt] ({N(point.SignalValue-half)},{N(points[i-1].Actions.Single(a=>a.Value==1).Probability)})--({N(point.SignalValue-half)},{N(probability)});");
                        }
                    }
                    for (int s = data.Length - 1; s >= 0; s--)
                    foreach (var point in (plaintiff ? data[s].Filing : data[s].Answering).Where(p => !p.OffPath))
                        ParticipationMarker(b, s, point.SignalValue, point.Actions.Single(a => a.Value == 1).Probability);
                }
                b.AppendLine(@"\end{scope}");
            }
            b.AppendLine($@"\node at ({N(x0+3.2)},{(offer?".18":"6.58")}) {{{(plaintiff?"Plaintiff":"Defendant")} signal}};");
            b.AppendLine($@"\node[rotate=90] at ({N(x0-.8)},{(offer?"3.0":"9.70")}) {{{(offer?"Offer":"Probability")}}};");
        }
        OfferMarker(b,0,2,-.48); b.AppendLine(@"\node[anchor=west] at (2.18,-.48) {Continue};");
        OfferMarker(b,0,5.4,-.48,true);b.AppendLine(@"\node[anchor=west] at (5.58,-.48) {Exit committed};");
        b.AppendLine(@"\node[anchor=east] at (10,-.48) {$p$:};");
        int n=0;foreach(double p in new[]{.25,.5,1.0})
        {
            double x=10.3+n++*1.7;OfferMarker(b,0,x,-.48,false,p);
            b.AppendLine($@"\node[anchor=west] at ({N(x+.18)},-.48) {{{N(p)}}};");
        }
        return b.AppendLine("\\end{tikzpicture}\n\\end{document}").ToString();
    }
    public const string Caption = "Filing and answering strategies by own signal, and offer supports conditional on each reached continue or exit-commitment history. All four panels use common signal orientation toward stronger plaintiff merits. Filing conditions on own signal; answering also conditions on filing. American uses circles, Trial Fee-Shifting squares and Complete Fee-Shifting diamonds, where available. In the upper panels, a small filled circle, outlined square and larger outlined diamond nest at coincident values; their fixed sizes identify fee rules, and their centers use exact signal and probability coordinates without offsets. Lower panels use aligned fee-rule strips. Filled offer symbols denote commitment to continue, open symbols commitment to exit if bargaining fails; settlement may prevent that exit. Offer-marker size increases with the conditional action probability according to the displayed probability key; exact probabilities and reach are in JSON. Only offer markers have graphical offsets of 0.009 signal units. Unreached histories are blank, and no mean offers are substituted. Results describe the selected equilibria. The filename and selection data identify costs, preferences and other primitives; all displayed cases use a matched specification and cost.";
}
