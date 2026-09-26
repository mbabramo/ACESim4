using ACESim;
using ACESimBase.Games.LitigGame.ManualReports;
using ACESimBase.GameSolvingAlgorithms;
using ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm;
using ACESimBase.GameSolvingSupport.ExactValues;
using Rationals;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace LitigCharts;
public static class FloatPivotDiagnosis
{
    private sealed class Stop : Exception { }
    private static readonly JsonSerializerOptions Json = new() { Converters={new JsonStringEnumConverter()} };
    private static Rational Binary(double value)
    {
        long bits=BitConverter.DoubleToInt64Bits(value);
        int exponent=(int)((bits>>52)&2047);
        if(exponent==2047) throw new ArithmeticException("Nonfinite input");
        BigInteger mantissa=bits&0xfffffffffffffL;
        if(exponent!=0) mantissa+=BigInteger.One<<52;
        if(bits<0) mantissa=-mantissa;
        int shift=(exponent==0 ? -1022 : exponent-1023)-52;
        return shift>=0 ? new Rational(mantissa<<shift) : new Rational(mantissa,BigInteger.One<<-shift);
    }
    public static async Task<int> RunAsync(string[] args)
    {
        // Isolated read-only diagnosis of one original pilot start. No equilibrium publication.
        string requestPath=args[0],output=args[1];int floatCap=int.Parse(args[2]),exactCap=int.Parse(args[3]);
        if(Directory.Exists(output)) throw new IOException("Fresh diagnosis directory required");
        Directory.CreateDirectory(output);
        var request=JsonSerializer.Deserialize<ArticleApproximateCommand.Request>(File.ReadAllText(requestPath),Json);
        var developer=(SequenceForm)await ArticleWorkedPathExtraction.InitializeAsync(FinalArticleCaseFactory.Create(request.Case));
        developer.EvolutionSettings.UseAcceleratedBestResponse=true;
        developer.EvolutionSettings.UseCurrentStrategyForBestResponse=true;
        developer.EvolutionSettings.RoundOffLowProbabilitiesBeforeAcceleratedBestResponse=false;
        var counts=developer.InformationSets.OrderBy(i=>i.PlayerIndex).ThenBy(i=>i.InformationSetNodeNumber).Select(i=>(int)i.NumPossibleActions).ToArray();
        var ranges=Enumerable.Range(0,2).Select(p=>developer.FinalUtilitiesNodes.Max(n=>n.Utilities[p])-developer.FinalUtilitiesNodes.Min(n=>n.Utilities[p])).ToArray();
        var policy=new ArticleApproximatePolicy(request.RoundingCutoff,request.GainUnits);
        ECTALemke<ExactValue> exact=null;
        ECTAStrategyDiagnostics<InexactValue> diagnostics=null;
        double[][] matrix=null;double[] rhs=null,cover=null;
        using var floating=new StreamWriter(Path.Combine(output,"floating.jsonl"));
        void Write(StreamWriter writer,object value){writer.WriteLine(JsonSerializer.Serialize(value,Json));writer.Flush();}
        object Metrics<T>(ECTALemke<T> lemke,ECTAPivotSnapshot s) where T:IMaybeExact<T>,new()
        {
            double error=0,scaled=0;
            for(int i=0;i<rhs.Length;i++)
            {
                double expected=rhs[i]+cover[i]*s.Auxiliary,scale=1+Math.Abs(expected)+Math.Abs(s.W[i]);
                for(int j=0;j<rhs.Length;j++){double term=matrix[i][j]*s.Z[j];expected+=term;scale+=Math.Abs(term);}
                error=Math.Max(error,Math.Abs(expected-s.W[i]));scaled=Math.Max(scaled,Math.Abs(expected-s.W[i])/scale);
            }
            string basis=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(lemke.basicCobasicIndexToVariable)));
            return new { s.Pivot,s.LeavingVariable,s.EnteringVariable,s.Auxiliary,s.Final,Basis=basis,
                MinimumZ=s.Z.Min(),MinimumW=s.W.Min(),EquationResidual=error,ScaledEquationResidual=scaled,
                Determinant=lemke.determinant.ToString(),s.AugmentedComplementarityViolation };
        }
        try
        {
            developer.TraceECTA<InexactValue>(seed:request.StartIndex,probabilityFloor:0.001,maxPivots:floatCap,
                beforeSolve:tree=>{
                    diagnostics=new(tree,developer.TraceOutcomeUtilities());
                    var l=tree.Lemke;matrix=l.lcpM.Select(row=>row.Select(v=>v.AsDouble).ToArray()).ToArray();rhs=l.rhsq.Select(v=>v.AsDouble).ToArray();cover=l.coveringVectorD.Select(v=>v.AsDouble).ToArray();
                    exact=new(rhs.Length);
                    IMaybeExact<ExactValue> Convert(double v){var r=Binary(v);var x=IMaybeExact<ExactValue>.FromRational(r);if(x.AsRational!=r)throw new Exception("Exact rational input changed");return x;}
                    for(int i=0;i<rhs.Length;i++){exact.rhsq[i]=Convert(rhs[i]);exact.coveringVectorD[i]=Convert(cover[i]);for(int j=0;j<rhs.Length;j++)exact.lcpM[i][j]=Convert(matrix[i][j]);}
                    File.WriteAllText(Path.Combine(output,"inputs.json"),JsonSerializer.Serialize(new {Matrix=matrix,Rhs=rhs,Cover=cover,Prior=diagnostics.PriorProbabilities,Ranges=ranges,InexactValue.Tolerance,ExactInput="Exact binary rational values of the floating LCP; constructed directly from IEEE-754 sign, mantissa and exponent; rational-to-double display conversion is not used to construct inputs."},Json));
                },afterPivot:(tree,s)=>{
                    object evaluation=null;
                    if(s.Pivot<=10 || s.Pivot%100==0 || s.Pivot==655 || s.Pivot==floatCap)
                    {
                        var projection=diagnostics.Project(s);var unrounded=diagnostics.Evaluate(projection.Probabilities);
                        var rounded=policy.Round(projection.Probabilities,counts);var check=diagnostics.Evaluate(rounded);
                        developer.SetInformationSetsToEquilibrium(rounded);developer.CalculateBestResponse(false);
                        var full=developer.Status.BestResponseImprovement.ToArray();
                        evaluation=new {projection.FlowResidual,UnroundedGain=policy.Scale(unrounded.UnilateralGains,ranges),RoundedGain=policy.Scale(check.UnilateralGains,ranges),FullBRGain=policy.Scale(full,ranges),FullBRRaw=full,IndependentBRRaw=check.UnilateralGains,ProfileHash=ArticleApproximateSearch.ProfileHash(rounded)};
                    }
                    Write(floating,new {State=Metrics(tree.Lemke,s),Evaluation=evaluation});
                    if(s.Pivot>=floatCap)throw new Stop();
                });
        }catch(Stop){}
        using var exactLog=new StreamWriter(Path.Combine(output,"exact.jsonl"));
        exact.PivotObserver=s=>{Write(exactLog,Metrics(exact,s));if(s.Pivot>=exactCap)throw new Stop();};
        try{exact.RunLemke(new ECTALemkeOptions{maxPivotSteps=exactCap});}catch(Stop){}
        File.WriteAllText(Path.Combine(output,"completed.json"),JsonSerializer.Serialize(new {Complete=true,FloatCap=floatCap,ExactCap=exactCap,NoPublishedEquilibrium=true},Json));
        return 0;
    }
}
