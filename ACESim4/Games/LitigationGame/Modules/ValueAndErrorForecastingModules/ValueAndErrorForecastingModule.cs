using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "ForecastingModule")]
    [Serializable]
    public class ValueAndErrorForecastingModule : GameModule, ICodeBasedSettingGenerator
    {

        public LitigationGame LitigationGame { get { return (LitigationGame)Game; } }
        public ValueAndErrorForecastingModuleProgress ForecastingProgress { get { return (ValueAndErrorForecastingModuleProgress)GameModuleProgress; } set { GameModuleProgress = value; } }
        public ValueAndErrorForecastingInputs ValueAndErrorForecastingInputs { get { return (ValueAndErrorForecastingInputs)GameModuleInputs; } }


        public override void ExecuteModule()
        {
            if (Game.CurrentActionPointName == "BeforeForecasting")
            {
                DetermineWhetherModuleIsForProbabilityOrDamages();
            }
            else if (Game.CurrentActionPointName == "AfterForecasting")
            {
                if (LitigationGame.DisputeContinues())
                    PostForecasting();
            }
            else
            {
                if (LitigationGame.DisputeContinues())
                {
                    if (ForecastingProgress.IsProbabilityNotDamages)
                        SetForecasts(LitigationGame.DisputeGeneratorModule.DGProgress.EvidentiaryStrengthLiability); // we use evidentiary strength rather than probability itself as the basis, so that noise will apply to evidentiary strength rather than to ultimate probability
                    else
                        SetForecasts(LitigationGame.DisputeGeneratorModule.DGProgress.BaseDamagesIfPWinsAsPctOfClaim);
                }
            }
        }

        public virtual void PostForecasting()
        {
        }

        public virtual void SetForecasts(double valueBeingForecast)
        {
        }

        public void DetermineWhetherModuleIsForProbabilityOrDamages()
        {
            ForecastingProgress.IsProbabilityNotDamages = GameModuleName == "ProbabilityForecastingModule";
        }

        public enum PartyOfForecast
        {
            Plaintiff,
            Defendant
        }

        public void GetForecastFromProxy(double proxy, double noiseLevel, PartyOfForecast partyOfForecast, out double? valueEstimate, out double? errorEstimate)
        {
            double valueEstimateDouble, errorEstimateDouble;
            GetForecastFromProxy(proxy, noiseLevel, partyOfForecast, out valueEstimateDouble, out errorEstimateDouble);
            valueEstimate = valueEstimateDouble;
            errorEstimate = errorEstimateDouble;
        }


        public virtual double GetInitialProxyForWhetherPartyExpectsToWin(PartyOfForecast partyOfForecast)
        {
            throw new Exception("Internal error: This must be overridden by the subclass.");
        }

        public virtual void GetForecastFromProxy(double proxy, double noiseLevel, PartyOfForecast partyOfForecast, out double valueEstimate, out double errorEstimate)
        {
            throw new Exception("Internal error: This must be overridden by the subclass.");
        }

        public virtual double GetEstimateOfOtherError(double ownEstimate, double ownAbsoluteError, double otherNoiseLevel)
        {
            throw new Exception("Internal error: This must be overriden by the subclass.");
        }

        public virtual void UpdateUnderlyingDistribution()
        {
        }

        /// <summary>
        /// Update both p and d forecasts based on new information acquired by both. Note that this is overriden for independent estimates module.
        /// </summary>
        /// <param name="independentPNoiseLevel"></param>
        /// <param name="independentDNoiseLevel"></param>
        public virtual void UpdateCombinedForecasts(double independentPNoiseLevel, double independentDNoiseLevel)
        {
            double originalCurrentEquivalentDNoiseLevel = (double)ForecastingProgress.CurrentEquivalentDNoiseLevel;
            double originalCurrentEquivalentPNoiseLevel = (double)ForecastingProgress.CurrentEquivalentPNoiseLevel;
            GetCombinedForecast((double)ForecastingProgress.PEstimatePResult, (double)ForecastingProgress.PEstimatePError, (double)ForecastingProgress.CurrentEquivalentPNoiseLevel, independentPNoiseLevel, PartyOfForecast.Plaintiff, out ForecastingProgress.PEstimatePResult, out ForecastingProgress.PEstimatePError, out ForecastingProgress.CurrentEquivalentPNoiseLevel);
            GetCombinedForecast((double)ForecastingProgress.DEstimatePResult, (double)ForecastingProgress.DEstimateDError, (double)ForecastingProgress.CurrentEquivalentDNoiseLevel, independentDNoiseLevel, PartyOfForecast.Defendant, out ForecastingProgress.DEstimatePResult, out ForecastingProgress.DEstimateDError, out ForecastingProgress.CurrentEquivalentDNoiseLevel);
            ForecastingProgress.PEstimateDResult = ForecastingProgress.IsProbabilityNotDamages ? 1.0 - (double)ForecastingProgress.PEstimatePResult : 0.0 - (double)ForecastingProgress.PEstimatePResult;
            ForecastingProgress.DEstimateDResult = ForecastingProgress.IsProbabilityNotDamages ? 1.0 - (double)ForecastingProgress.DEstimatePResult : 0.0 - (double)ForecastingProgress.DEstimatePResult;
            //ForecastingProgress.PEstimateDError = GetEstimateOfOtherError((double)ForecastingProgress.PEstimatePResult, (double)ForecastingProgress.PEstimatePError, (double)ForecastingProgress.CurrentEquivalentDNoiseLevel);
            //ForecastingProgress.DEstimatePError = GetEstimateOfOtherError((double)ForecastingProgress.DEstimateDResult, (double)ForecastingProgress.DEstimateDError, (double)ForecastingProgress.CurrentEquivalentPNoiseLevel);
        }

        public virtual void GetCombinedForecast(double originalForecast, double originalAbsoluteError, double originalNoiseLevel, double independentNoiseLevel, PartyOfForecast partyOfForecast, out double? newEstimate, out double? newError, out double? newNoiseLevel)
        {
            double obfuscated = GetAdditionalProxy(independentNoiseLevel, partyOfForecast == PartyOfForecast.Plaintiff);
            double newEstimateDouble;
            double newErrorDouble;
            double newNoiseLevelDouble;
            GetCombinedForecast(originalForecast, originalAbsoluteError, originalNoiseLevel, obfuscated, independentNoiseLevel, partyOfForecast, out newEstimateDouble, out newErrorDouble, out newNoiseLevelDouble);
            newEstimate = newEstimateDouble;
            newError = newErrorDouble;
            newNoiseLevel = newNoiseLevelDouble;
        }

        internal double GetAdditionalRandomSeed(int index, bool randomSeedIsForPlaintiff)
        {
            // This is clumsy, but to fix it and use List<double>, we would need some way of specifying attributes on each element in a list to do the input seed swap and flip.
            if (randomSeedIsForPlaintiff)
            {
                switch (index)
                {
                    case 0:
                        return ValueAndErrorForecastingInputs.PRandomSeed00;
                    case 1:
                        return ValueAndErrorForecastingInputs.PRandomSeed01;
                    case 2:
                        return ValueAndErrorForecastingInputs.PRandomSeed02;
                    case 3:
                        return ValueAndErrorForecastingInputs.PRandomSeed03;
                    case 4:
                        return ValueAndErrorForecastingInputs.PRandomSeed04;
                    case 5:
                        return ValueAndErrorForecastingInputs.PRandomSeed05;
                    case 6:
                        return ValueAndErrorForecastingInputs.PRandomSeed06;
                    case 7:
                        return ValueAndErrorForecastingInputs.PRandomSeed07;
                    case 8:
                        return ValueAndErrorForecastingInputs.PRandomSeed08;
                    case 9:
                        return ValueAndErrorForecastingInputs.PRandomSeed09;
                    case 10:
                        return ValueAndErrorForecastingInputs.PRandomSeed10;
                    default: throw new Exception();
                }
            }
            else
            {

                switch (index)
                {
                    case 0:
                        return ValueAndErrorForecastingInputs.DRandomSeed00;
                    case 1:
                        return ValueAndErrorForecastingInputs.DRandomSeed01;
                    case 2:
                        return ValueAndErrorForecastingInputs.DRandomSeed02;
                    case 3:
                        return ValueAndErrorForecastingInputs.DRandomSeed03;
                    case 4:
                        return ValueAndErrorForecastingInputs.DRandomSeed04;
                    case 5:
                        return ValueAndErrorForecastingInputs.DRandomSeed05;
                    case 6:
                        return ValueAndErrorForecastingInputs.DRandomSeed06;
                    case 7:
                        return ValueAndErrorForecastingInputs.DRandomSeed07;
                    case 8:
                        return ValueAndErrorForecastingInputs.DRandomSeed08;
                    case 9:
                        return ValueAndErrorForecastingInputs.DRandomSeed09;
                    case 10:
                        return ValueAndErrorForecastingInputs.DRandomSeed10;
                    default: throw new Exception();
                }
            }
        }

        internal double GetAdditionalProxy(double independentNoiseLevel, bool randomSeedIsForPlaintiff)
        {
            
            double randSeed;
            if (randomSeedIsForPlaintiff)
            {
                randSeed = GetAdditionalRandomSeed(ForecastingProgress.PNumRandomSeedsUsed, true);
                ForecastingProgress.PNumRandomSeedsUsed++;
            }
            else
            {
                randSeed = GetAdditionalRandomSeed(ForecastingProgress.DNumRandomSeedsUsed, false);
                ForecastingProgress.DNumRandomSeedsUsed++;
            }

            double obfuscation = independentNoiseLevel * (double)alglib.normaldistr.invnormaldistribution(randSeed);
            double obfuscated = (double)ForecastingProgress.ActualPIssueStrength + obfuscation; 
            return obfuscated;
        }

        public virtual void GetCombinedForecast(double originalForecast, double originalAbsoluteError, double originalNoiseLevel, double independentProxy, double independentNoiseLevel, PartyOfForecast partyOfForecast, out double newEstimate, out double newError, out double newNoiseLevel)
        {
            double independentEstimate;
            double independentError;
            GetForecastFromProxy(independentProxy, independentNoiseLevel, partyOfForecast, out independentEstimate, out independentError);
            if (originalAbsoluteError == 0)
            {
                newEstimate = originalForecast;
                newError = 0;
                newNoiseLevel = 0;
                return;
            }
            else if (independentNoiseLevel == 0)
            {
                newEstimate = independentEstimate;
                newError = 0;
                newNoiseLevel = 0;
                return;
            }
            const double ratioOfStandardDeviationToAverageAbsoluteError = 1.2533; // approximate conversion, assumes normal distribution
            double original_sd = originalAbsoluteError * ratioOfStandardDeviationToAverageAbsoluteError; 
            double independent_sd = independentError * ratioOfStandardDeviationToAverageAbsoluteError;
            double original_var = original_sd * original_sd;
            double independent_var = independent_sd * independent_sd;
            newEstimate = ((originalForecast / original_var) + (independentEstimate / independent_var)) / (1 / original_var + 1 / independent_var);
            double new_sd = Math.Sqrt(1 / (1 / original_var + 1 / independent_var));
            newError = new_sd / ratioOfStandardDeviationToAverageAbsoluteError;
            newNoiseLevel = Math.Sqrt(1 / (1 / (originalNoiseLevel * originalNoiseLevel) + 1 / (independentNoiseLevel * independentNoiseLevel)));
        }

        public virtual double GetNoiseLevelOfSinglePieceOfInformationCorrespondingToNPiecesOfInformationAtOtherNoiseLevel(double noiseLevelForSinglePieceOfInformation, double numPiecesOfInformationAtThatNoiseLevel)
        {
            // The noise level is a standard deviation so square it for the variance.
            double variance = noiseLevelForSinglePieceOfInformation * noiseLevelForSinglePieceOfInformation;
            double varianceOfNumPieces = variance / numPiecesOfInformationAtThatNoiseLevel;
            double equivalentNoiseLevel = Math.Sqrt(varianceOfNumPieces);
            return equivalentNoiseLevel;
        }

        public override void Score()
        {
            throw new NotImplementedException("This must be overridden.");
        }

        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            ValueAndErrorForecastingModule copy = new ValueAndErrorForecastingModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy); 
            theGameModuleProgress = ValueAndErrorForecastingModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }

        public virtual object GenerateSetting(string options)
        {
            throw new Exception("Override in subclass.");
        }

        internal virtual ValueAndErrorForecastingModule GenerateNewForecastingModule(List<Decision> decisions)
        {
            return new ValueAndErrorForecastingModule() // subclasses should override this
            {
                DecisionsCore = decisions
            };
        }


        internal virtual string GetForecastAbbreviation()
        {
            return "BPF";
        }

        internal virtual string GetForecastName()
        {
            return "Forecast";
        }

        internal virtual StrategyBounds GetStrategyBounds()
        {
            return new StrategyBounds()
            {
                LowerBound = 0.0,
                UpperBound = 1.0
            };
        }

        //internal virtual List<string> GetInputNames()
        //{
        //    return new List<string>() {
        //            "Input1"
        //        };
        //}

        //internal virtual List<string> GetInputAbbreviations()
        //{
        //    return new List<string>() {
        //            "I1"
        //        };
        //}

        public override OrderingConstraint? DetermineOrderingConstraint(List<ActionGroup> originalList, ActionGroup actionGroupWithinThisModule, ActionGroup secondActionGroup, bool forEvolution)
        {
            if (!forEvolution && (secondActionGroup.Name.Contains("LitigationCost") || secondActionGroup.Name.Contains("BeginningDropOrDefault")))
                return OrderingConstraint.Before;
            if (forEvolution && (secondActionGroup.Name.Contains("LitigationCost") || secondActionGroup.Name.Contains("BeginningDropOrDefault")))
                return OrderingConstraint.After;
            return null;
        }
    }
}
