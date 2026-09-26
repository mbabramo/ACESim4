using System.Globalization;
using System.Text.Json;
using ACESim;
using ACESimBase.Games.LitigGame.ManualReports;
using LitigCharts;
internal static class Verify { public static async Task Main(string[] args) {
CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
ACESimBase.Util.Debugging.TabbedText.DisableOutput();
var root=Path.GetFullPath(args[0]);var results=new List<object>();
foreach(string requestFile in Directory.GetFiles(Path.Combine(root,"inputs/run-v1"),"*-part-0.json"))
{
 using var request=JsonDocument.Parse(File.ReadAllBytes(requestFile));var r=request.RootElement;
 var spec=r.GetProperty("Case").Deserialize<FinalArticleCase>(FinalArticleExecution.Json);
 var options=FinalArticleCaseFactory.Create(spec);var d=await ArticleWorkedPathExtraction.InitializeAsync(options);
 d.EvolutionSettings.UseCurrentStrategyForBestResponse=true;d.EvolutionSettings.UseAcceleratedBestResponse=true;
 d.EvolutionSettings.RoundOffLowProbabilitiesBeforeAcceleratedBestResponse=false;d.EvolutionSettings.RoundOffLowProbabilitiesBeforeReporting=false;
 var p=r.GetProperty("Profiles")[0];var v=File.ReadAllText(p.GetProperty("FrozenFile").GetString()).Trim().Split(',').Select(ACESimBase.Games.EFGFileGame.EFGFileReader.RationalStringToDouble).ToArray();
 ArticleWorkedPathExtraction.LoadProfile(d,v);
 var nodes=d.InformationSets.OrderBy(n=>n.PlayerIndex).ThenBy(n=>n.InformationSetNodeNumber).ToArray();
 var original=nodes.Select(n=>n.GetCurrentProbabilitiesAsArray()).ToArray();
 using var saved=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(r.GetProperty("Output").GetString(),"game-and-directions.json")));
 foreach(int direction in new[]{0,r.GetProperty("Directions").GetInt32()-1}.Distinct())foreach(double eps in new[]{0,r.GetProperty("Epsilons").EnumerateArray().Max(e=>e.GetDouble())})for(byte player=0;player<2;player++)
 {
  for(int i=0;i<nodes.Length;i++)
  {
   double[] q=saved.RootElement.GetProperty("Directions")[direction][i].EnumerateArray().Select(x=>x.GetDouble()).ToArray();
   // The legacy traversal reads the separate opponent-probability buffer.
   // Synchronize both buffers, without rounding or pruning any tremble.
   nodes[i].SetCurrentAndAverageProbabilities(nodes[i].PlayerIndex==player?original[i]:original[i].Select((x,k)=>(1-eps)*x+eps*q[k]).ToArray());
  }
  d.CalculateBestResponse(false);double accelerated=d.Status.BestResponseUtilities[player];
  double native=d.CalculateBestResponse(player,ActionStrategies.CurrentProbability);
  double difference=Math.Abs(accelerated-native);
  if(!double.IsFinite(difference)||difference>1e-8)throw new InvalidDataException($"Independent BR mismatch: {spec.Id} p{player} {eps} {difference:R}");
  double welfareDifference=0,utilityDifference=0;
  if(eps>0)
  {
   using var check=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(r.GetProperty("Output").GetString(),p.GetProperty("Id").GetString(),$"p{player}-d{direction}-e0.010.json")));
   var selection=check.RootElement.GetProperty("InformationSets").EnumerateArray().ToDictionary(x=>x.GetProperty("InformationSetNodeNumber").GetInt32(),x=>x.GetProperty("Selected").EnumerateArray().Select(y=>y.GetDouble()).ToArray());
   foreach(var n in nodes)if(n.PlayerIndex==player)n.SetCurrentAndAverageProbabilities(selection[n.InformationSetNodeNumber]);
   utilityDifference=Math.Abs(d.GetAverageUtilities(false)[player]-check.RootElement.GetProperty("SelectedResponseUtility").GetDouble());
   if(!double.IsFinite(utilityDifference)||utilityDifference>1e-8)throw new InvalidDataException("Selected response full-tree utility mismatch");
   d.ActionStrategy=ActionStrategies.CurrentProbability;d.SaveWeightedGameProgressesAfterEachReport=true;d.SavedWeightedGameProgresses.Clear();
   await d.GenerateReportsByPlaying(false);
   var w=SavedProfileWelfare.Evaluate(options,d.SavedWeightedGameProgresses).Headline;
   double[] actual={w.MeritoriousPlaintiffShortfall,w.NonliableDefendantBurden,w.LiableDefendantExcessBurden,w.GrossOutcomeError,w.RealLitigationExpenditures};
   var expected=check.RootElement.GetProperty("ResponseOutcome").GetProperty("Welfare").EnumerateArray().Select(x=>x.GetDouble()).ToArray();
   welfareDifference=actual.Zip(expected,(x,y)=>Math.Abs(x-y)).Max();
   if(!double.IsFinite(welfareDifference)||welfareDifference>1e-10)throw new InvalidDataException("Synchronized-buffer welfare mismatch");
  }
  results.Add(new{Case=spec.Id,Player=player,Epsilon=eps,Direction=direction,Accelerated=accelerated,IndependentTreePass=native,AbsoluteDifference=difference,SelectedResponseUtilityDifference=utilityDifference,WelfareDifference=welfareDifference});
 }
}
using(var stream=new FileStream(Path.Combine(root,"reports/independent-br-validation.json"),FileMode.CreateNew))JsonSerializer.Serialize(stream,new{Passed=true,Checks=results.Count,Results=results,SolvesStarted=0},new JsonSerializerOptions{WriteIndented=true});
Console.WriteLine($"Passed {results.Count} independent full-tree best-response comparisons.");

}}
