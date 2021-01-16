using ACESim.Util;
using ACESimBase;
using ACESimBase.GameSolvingSupport;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ACESim
{
    public class LitigGameLauncher : Launcher
    {

        // We can use this to allow for multiple options sets. These can then run in parallel. But note that we can also have multiple runs with a single option set using different settings by using GameDefinition scenarios; this is useful when there is a long initialization and it makes sense to complete one set before starting the next set.

        private bool HigherRiskAversion = false;
        private bool PRiskAverse = false;
        public bool DRiskAverse = false;
        public bool TestDisputeGeneratorVariations = false;
        public bool IncludeRunningSideBetVariations = false;
        public bool LimitToAmerican = true;
        public double[] CostsMultipliers = new double[] { 1.0 }; // 0.1, 0.25, 0.5, 1.0, 1.5, 2.0, 4.0 };
        public double StdevPlayerNoise = 0.3; // baseline is 0.3

        public override GameDefinition GetGameDefinition() => new LitigGameDefinition();

        public override GameOptions GetSingleGameOptions() => LitigGameOptionsGenerator.GetLitigGameOptions();

        private enum OptionSetChoice
        {
            JustOneOption,
            Fast,
            ShootoutPermutations,
            VariousUncertainties,
            VariedAlgorithms,
            Custom2,
            ShootoutGameVariations,
            BluffingVariations,
            KlermanEtAl,
            KlermanEtAl_MultipleStrengthPoints,
            KlermanEtAl_Options,
            KlermanEtAl_DamagesUncertainty,
            Simple1BR,
            Simple2BR,
            FeeShiftingArticle,
        }

        public override List<GameOptions> GetOptionsSets()
        {
            List<GameOptions> optionSets = new List<GameOptions>();
            OptionSetChoice optionSetChoice = OptionSetChoice.Simple2BR;  // <<-- Choose option set here
            switch (optionSetChoice)
            {
                case OptionSetChoice.JustOneOption:
                    AddToOptionsListWithName(optionSets, "singleoptionset", LitigGameOptionsGenerator.DamagesUncertainty_2BR());
                    break;
                case OptionSetChoice.Fast:
                    AddFast(optionSets);
                    break;
                case OptionSetChoice.ShootoutPermutations:
                    AddShootoutPermutations(optionSets);
                    break;
                case OptionSetChoice.ShootoutGameVariations:
                    AddShootoutGameVariations(optionSets);
                    break;
                case OptionSetChoice.VariousUncertainties:
                    AddVariousUncertainties(optionSets);
                    break;
                case OptionSetChoice.VariedAlgorithms:
                    AddWithVariedAlgorithms(optionSets);
                    break;
                case OptionSetChoice.Custom2:
                    AddCustom2(optionSets);
                    break;
                case OptionSetChoice.BluffingVariations:
                    AddBluffingOptionsSets(optionSets);
                    break;
                case OptionSetChoice.KlermanEtAl:
                    AddKlermanEtAlPermutations(optionSets, LitigGameOptionsGenerator.KlermanEtAl);
                    break;
                case OptionSetChoice.KlermanEtAl_MultipleStrengthPoints:
                    AddKlermanEtAlPermutations(optionSets, LitigGameOptionsGenerator.KlermanEtAl_MultipleStrengthPoints);
                    break;
                case OptionSetChoice.KlermanEtAl_Options:
                    AddKlermanEtAlPermutations(optionSets, LitigGameOptionsGenerator.KlermanEtAl_WithOptions);
                    break;
                case OptionSetChoice.KlermanEtAl_DamagesUncertainty:
                    AddKlermanEtAlPermutations(optionSets, LitigGameOptionsGenerator.KlermanEtAl_WithDamagesUncertainty);
                    break;
                case OptionSetChoice.Simple1BR:
                    AddSimple1BRGames(optionSets);
                    break;
                case OptionSetChoice.Simple2BR:
                    AddSimple2BRGames(optionSets);
                    break;
                case OptionSetChoice.FeeShiftingArticle:
                    AddFeeShiftingArticleGames(optionSets);
                    break;
            }

            optionSets = optionSets.OrderBy(x => x.Name).ToList();

            bool simplify = false; // Enable for debugging purposes to speed up execution without going all the way to "fast" option
            if (simplify)
                foreach (var optionSet in optionSets)
                    optionSet.Simplify();

            return optionSets;
        }


        private void AddCustom2(List<GameOptions> optionSets)
        {

            // now, liability and damages only
            foreach (RiskAversion riskAverse in new RiskAversion[] { RiskAversion.DOnlyRiskAverse })
            {
                foreach ((string name, double costsMultiplier) in new (string name, double costsMultiplier)[] { ("basecosts", 1.0) })
                {
                    optionSets.Add(GetAndTransformWithRiskAversion("noshootout", name, LitigGameOptionsGenerator.BothUncertain_2BR, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                    optionSets.Add(GetAndTransformWithRiskAversion("shootout", name, LitigGameOptionsGenerator.Shootout, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                }
            }
        }

        private void AddWithVariedAlgorithms(List<GameOptions> optionSets)
        {
            void Helper(List<GameOptions> optionSets, string optionSetNamePrefix, Action<EvolutionSettings> modifyEvolutionSettings)
            {
                optionSets.Add(GetAndTransformWithRiskAversion(optionSetNamePrefix + "damages_unc", "basecosts", LitigGameOptionsGenerator.DamagesUncertainty_1BR, x => { x.CostsMultiplier = 1.0; x.ModifyEvolutionSettings = modifyEvolutionSettings; }, RiskAversion.RiskNeutral));
                //optionSets.Add(GetAndTransform(optionSetNamePrefix + "damages_unc2BR", "basecosts", LitigGameOptionsGenerator.DamagesUncertainty_2BR, x => { x.CostsMultiplier = 1.0; x.ModifyEvolutionSettings = modifyEvolutionSettings; }, false));
                optionSets.Add(GetAndTransformWithRiskAversion(optionSetNamePrefix + "liability_unc", "basecosts", LitigGameOptionsGenerator.LiabilityUncertainty_1BR, x => { x.CostsMultiplier = 1.0; x.ModifyEvolutionSettings = modifyEvolutionSettings; }, RiskAversion.RiskNeutral));
                //optionSets.Add(GetAndTransform(optionSetNamePrefix + "liability_unc2BR", "basecosts", LitigGameOptionsGenerator.LiabilityUncertainty_2BR, x => { x.CostsMultiplier = 1.0; x.ModifyEvolutionSettings = modifyEvolutionSettings; }, false));
            }
            Helper(optionSets, "BRD", es =>
            {
                es.Algorithm = GameApproximationAlgorithm.BestResponseDynamics;
            });
            Helper(optionSets, "FP", es =>
            {
                es.Algorithm = GameApproximationAlgorithm.FictitiousPlay;
            });
            Helper(optionSets, "RM", es =>
            {
                es.Algorithm = GameApproximationAlgorithm.RegretMatching;
            });
            Helper(optionSets, "MW", es =>
            {
                es.Algorithm = GameApproximationAlgorithm.MultiplicativeWeights;
            });
        }

        private void AddVariousUncertainties(List<GameOptions> optionSets)
        {

            optionSets.Add(GetAndTransformWithRiskAversion("damages_unc", "basecosts", LitigGameOptionsGenerator.DamagesUncertainty_1BR, x => { x.CostsMultiplier = 1.0; }, RiskAversion.RiskNeutral));
            optionSets.Add(GetAndTransformWithRiskAversion("damages_unc2BR", "basecosts", LitigGameOptionsGenerator.DamagesUncertainty_2BR, x => { x.CostsMultiplier = 1.0; }, RiskAversion.RiskNeutral));
            optionSets.Add(GetAndTransformWithRiskAversion("liability_unc", "basecosts", LitigGameOptionsGenerator.LiabilityUncertainty_1BR, x => { x.CostsMultiplier = 1.0; }, RiskAversion.RiskNeutral));
            optionSets.Add(GetAndTransformWithRiskAversion("liability_unc2BR", "basecosts", LitigGameOptionsGenerator.LiabilityUncertainty_2BR, x => { x.CostsMultiplier = 1.0; }, RiskAversion.RiskNeutral));
            optionSets.Add(GetAndTransformWithRiskAversion("both_unc", "basecosts", LitigGameOptionsGenerator.BothUncertain_1BR, x => { x.CostsMultiplier = 1.0; }, RiskAversion.RiskNeutral));
            optionSets.Add(GetAndTransformWithRiskAversion("both_unc2BR", "basecosts", LitigGameOptionsGenerator.BothUncertain_2BR, x => { x.CostsMultiplier = 1.0; }, RiskAversion.RiskNeutral));

        }

        private void AddFast(List<GameOptions> optionSets)
        {
            RiskAversion riskAverse = RiskAversion.RiskNeutral;
            foreach ((string name, double costsMultiplier) in new (string name, double costsMultiplier)[] { ("basecosts", 1.0), ("highcosts", 3.0) })
            {
                optionSets.Add(GetAndTransformWithRiskAversion("fast1", name, LitigGameOptionsGenerator.SuperSimple, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
            }
        }

        private void AddShootoutMainPermutatio(List<GameOptions> optionSets)
        {
            RiskAversion riskAverse = RiskAversion.RiskNeutral;
            foreach ((string name, double costsMultiplier) in new (string name, double costsMultiplier)[] { ("basecosts", 1.0), ("highcosts", 3.0) })
            {
                optionSets.Add(GetAndTransformWithRiskAversion("noshootout", name, LitigGameOptionsGenerator.BothUncertain_2BR, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                optionSets.Add(GetAndTransformWithRiskAversion("shootout", name, LitigGameOptionsGenerator.Shootout, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                optionSets.Add(GetAndTransformWithRiskAversion("sotrip", name, LitigGameOptionsGenerator.Shootout_Triple, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
            }
        }

        private void AddShootoutPermutations(List<GameOptions> optionSets)
        {
            foreach (RiskAversion riskAverse in new RiskAversion[] { RiskAversion.RiskNeutral, RiskAversion.RiskAverse })
            {
                foreach ((string name, double costsMultiplier) in new (string name, double costsMultiplier)[] { ("lowcosts", 1.0 / 3.0), ("basecosts", 1.0), ("highcosts", 3.0) })
                {
                    //optionSets.Add(GetAndTransform("liab", name, LitigGameOptionsGenerator.LiabilityUncertainty_2BR, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                    //optionSets.Add(GetAndTransform("dam", name, LitigGameOptionsGenerator.DamagesUncertainty_2BR, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                    optionSets.Add(GetAndTransformWithRiskAversion("noshootout", name, LitigGameOptionsGenerator.BothUncertain_2BR, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                    optionSets.Add(GetAndTransformWithRiskAversion("shootout", name, LitigGameOptionsGenerator.Shootout, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                    optionSets.Add(GetAndTransformWithRiskAversion("soallrounds", name, LitigGameOptionsGenerator.Shootout_AllRounds, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                    optionSets.Add(GetAndTransformWithRiskAversion("soabandon", name, LitigGameOptionsGenerator.Shootout_IncludingAbandoment, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                    optionSets.Add(GetAndTransformWithRiskAversion("soallraban", name, LitigGameOptionsGenerator.Shootout_AllRoundsIncludingAbandoment, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                    optionSets.Add(GetAndTransformWithRiskAversion("sotrip", name, LitigGameOptionsGenerator.Shootout_Triple, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                }
            }
        }

        int Simple1BR_maxNumLiabilityStrengthPoints = 4;
        int Simple1BR_maxNumLiabilitySignals = 4;
        int Simple1BR_maxNumOffers = 4;

        private void AddSimple1BRGames(List<GameOptions> optionSets)
        {
            for (byte numLiabilityStrengthPoints = 2; numLiabilityStrengthPoints <= Simple1BR_maxNumLiabilityStrengthPoints; numLiabilityStrengthPoints++)
                for (byte numLiabilitySignals = 2; numLiabilitySignals <= Simple1BR_maxNumLiabilitySignals; numLiabilitySignals++)
                    for (byte numOffers = 2; numOffers <= Simple1BR_maxNumOffers; numOffers++)
                    {
                        optionSets.Add(GetAndTransformWithRiskAversion("simple1BR", numLiabilityStrengthPoints.ToString() + "," + numLiabilitySignals.ToString() + "," + numOffers.ToString(), LitigGameOptionsGenerator.GetSimple1BROptions, x =>
                        {
                            x.NumLiabilityStrengthPoints = numLiabilityStrengthPoints;
                            x.NumLiabilitySignals = numLiabilitySignals;
                            x.NumOffers = numOffers;
                        }, RiskAversion.RiskNeutral));
                    }
        }
        private void AddSimple2BRGames(List<GameOptions> optionSets)
        {
            for (byte numLiabilityStrengthPoints = 2; numLiabilityStrengthPoints <= Simple1BR_maxNumLiabilityStrengthPoints; numLiabilityStrengthPoints++)
                for (byte numLiabilitySignals = 2; numLiabilitySignals <= Simple1BR_maxNumLiabilitySignals; numLiabilitySignals++)
                    for (byte numOffers = 2; numOffers <= Simple1BR_maxNumOffers; numOffers++)
                    {
                        optionSets.Add(GetAndTransformWithRiskAversion("simple2BR", numLiabilityStrengthPoints.ToString() + "," + numLiabilitySignals.ToString() + "," + numOffers.ToString(), LitigGameOptionsGenerator.GetSimple2BROptions, x =>
                        {
                            x.NumLiabilityStrengthPoints = numLiabilityStrengthPoints;
                            x.NumLiabilitySignals = numLiabilitySignals;
                            x.NumOffers = numOffers;
                        }, RiskAversion.RiskNeutral));
                    }
        }

        private void AddKlermanEtAlPermutations(List<GameOptions> optionSets, Func<LitigGameOptions> myGameOptionsFunc)
        {
            foreach (double exogProb in new double[] { 0.05, 0.1, 0.15, 0.2, 0.25, 0.3, 0.35, 0.4, 0.45, 0.5, 0.55, 0.6, 0.65, 0.7, 0.75, 0.8, 0.85, 0.9, 0.95 })
            {
                optionSets.Add(GetAndTransformWithRiskAversion("klerman", exogProb.ToString(), myGameOptionsFunc, x =>
                {
                    x.IncludeCourtSuccessReport = false;
                    x.IncludeSignalsReport = false;
                    x.FirstRowOnly = true;
                    ((LitigGameExogenousDisputeGenerator)x.LitigGameDisputeGenerator).ExogenousProbabilityTrulyLiable = exogProb;
                }, RiskAversion.RiskNeutral));
            }
        }

        private void AddShootoutGameVariations(List<GameOptions> optionSets)
        {
            foreach (RiskAversion riskAverse in new RiskAversion[] { RiskAversion.RiskNeutral })
            {
                // different levels of costs, same for both parties
                foreach ((string name, double costsMultiplier) in new (string name, double costsMultiplier)[] { ("basecosts", 1.0), ("lowcosts", 1.0 / 3.0), ("highcosts", 3) })
                {
                    optionSets.Add(GetAndTransformWithRiskAversion("noshootout", name, LitigGameOptionsGenerator.BothUncertain_2BR, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                    optionSets.Add(GetAndTransformWithRiskAversion("shootout", name, LitigGameOptionsGenerator.Shootout, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                }
                // try lowering all costs dramatically except trial costs
                foreach ((string name, double costsMultiplier) in new (string name, double costsMultiplier)[] { ("lowpretrialcosts", 1.0 / 10.0) })
                {
                    optionSets.Add(GetAndTransformWithRiskAversion("noshootout", name, LitigGameOptionsGenerator.BothUncertain_2BR, x => { x.CostsMultiplier = costsMultiplier; x.PTrialCosts *= 1.0 / costsMultiplier; x.DTrialCosts *= 1.0 / costsMultiplier; }, riskAverse));
                    optionSets.Add(GetAndTransformWithRiskAversion("shootout", name, LitigGameOptionsGenerator.Shootout, x => { x.CostsMultiplier = costsMultiplier; x.PTrialCosts *= 1.0 / costsMultiplier; x.DTrialCosts *= 1.0 / costsMultiplier; }, riskAverse));
                }
                // Now, we'll do asymmetric trial costs. We assume that other costs are the same. This might make sense in the event that trial has a publicity cost for one party. 
                //foreach ((string name, double dCostsMultiplier) in new (string name, double dCostsMultiplier)[] { ("phighcost", 0.5), ("dhighcost", 2.0) })
                //{
                //    Action<LitigGameOptions> transform = x =>
                //    {
                //        x.DTrialCosts *= dCostsMultiplier;
                //        x.PTrialCosts /= dCostsMultiplier;
                //    };
                //    optionSets.Add(GetAndTransform("noshootout", name, LitigGameOptionsGenerator.Usual, transform, riskAverse));
                //    optionSets.Add(GetAndTransform("shootout", name, LitigGameOptionsGenerator.Shootout, transform, riskAverse));
                //}
            }
            // now, add a set with regular and asymmetric risk aversion
            foreach (RiskAversion riskAverse in new RiskAversion[] { RiskAversion.RiskAverse, RiskAversion.POnlyRiskAverse, RiskAversion.DOnlyRiskAverse })
            {
                foreach ((string name, double costsMultiplier) in new (string name, double costsMultiplier)[] { ("basecosts", 1.0) })
                {
                    optionSets.Add(GetAndTransformWithRiskAversion("noshootout", name, LitigGameOptionsGenerator.BothUncertain_2BR, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                    optionSets.Add(GetAndTransformWithRiskAversion("shootout", name, LitigGameOptionsGenerator.Shootout, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                }
            }

            // now, vary strength of information
            foreach (RiskAversion riskAverse in new RiskAversion[] { RiskAversion.RiskNeutral })
            {
                foreach ((string name, double costsMultiplier) in new (string name, double costsMultiplier)[] { ("basecosts", 1.0) })
                {
                    foreach ((string infoName, double pInfoChange, double dInfoChange) in new (string infoName, double pInfoChange, double dInfoChange)[] { ("betterinfo", 0.5, 0.5), ("worseinfo", 2.0, 2.0), ("dbetterinfo", 2.0, 0.5) })
                    {
                        Action<LitigGameOptions> transform = x =>
                        {
                            x.CostsMultiplier = costsMultiplier;
                            x.PDamagesNoiseStdev *= pInfoChange;
                            x.PLiabilityNoiseStdev *= pInfoChange;
                            x.DDamagesNoiseStdev *= dInfoChange;
                            x.DLiabilityNoiseStdev *= dInfoChange;
                        };
                        optionSets.Add(GetAndTransformWithRiskAversion("noshootout", infoName + "-" + name, LitigGameOptionsGenerator.BothUncertain_2BR, transform, riskAverse));
                        optionSets.Add(GetAndTransformWithRiskAversion("shootout", infoName + "-" + name, LitigGameOptionsGenerator.Shootout, transform, riskAverse));
                    }
                }
            }

            // now, liability and damages only
            foreach (RiskAversion riskAverse in new RiskAversion[] { RiskAversion.RiskNeutral })
            {
                foreach ((string name, double costsMultiplier) in new (string name, double costsMultiplier)[] { ("basecosts", 1.0) })
                {
                    Action<LitigGameOptions> transform = x => { };
                    optionSets.Add(GetAndTransformWithRiskAversion("dam-noshootout", name, LitigGameOptionsGenerator.DamagesUncertainty_2BR, transform, riskAverse));
                    optionSets.Add(GetAndTransformWithRiskAversion("dam-shootout", name, LitigGameOptionsGenerator.DamagesShootout, transform, riskAverse));
                    optionSets.Add(GetAndTransformWithRiskAversion("liab-noshootout", name, LitigGameOptionsGenerator.LiabilityUncertainty_2BR, transform, riskAverse));
                    optionSets.Add(GetAndTransformWithRiskAversion("liab-shootout", name, LitigGameOptionsGenerator.LiabilityShootout, transform, riskAverse));
                }
            }
        }

        enum RiskAversion
        {
            RiskNeutral,
            RiskAverse,
            POnlyRiskAverse,
            DOnlyRiskAverse
        }

        public void AddBluffingOptionsSets(List<GameOptions> optionSets)
        {
            LitigGameOptions GetBluffingBase(bool hiddenOffers, bool onlyTwoRounds = false, bool liabilityAlsoUncertain = false, bool cfrPlus = false, double? costsMultiplier = null, bool pRiskAversion = false, bool dRiskAversion = false, double pNoiseMultiplier = 1.0, double dNoiseMultiplier = 1.0)
            {
                LitigGameOptions options = (onlyTwoRounds, liabilityAlsoUncertain) switch
                {
                    (false, false) => LitigGameOptionsGenerator.DamagesUncertainty_3BR(),
                    (false, true) => LitigGameOptionsGenerator.BothUncertain_3BR(),
                    (true, false) => LitigGameOptionsGenerator.DamagesUncertainty_2BR(),
                    (true, true) => LitigGameOptionsGenerator.BothUncertain_2BR(),
                };
                options.SkipFileAndAnswerDecisions = true;
                options.AllowAbandonAndDefaults = false;
                if (hiddenOffers)
                    options.SimultaneousOffersUltimatelyRevealed = false;
                else
                    options.SimultaneousOffersUltimatelyRevealed = true;
                if (cfrPlus)
                    options.ModifyEvolutionSettings = e => { e.UseCFRPlusInRegretMatching = true; };
                if (costsMultiplier is double costsMultiplierChange)
                    options.CostsMultiplier = costsMultiplierChange;
                if (pRiskAversion)
                    options.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = options.PInitialWealth, Alpha = 10 * 0.000001 };
                if (dRiskAversion)
                    options.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = options.DInitialWealth, Alpha = 10 * 0.000001 };
                if (pNoiseMultiplier != 1.0)
                {
                    options.PLiabilityNoiseStdev *= pNoiseMultiplier;
                    options.PDamagesNoiseStdev *= pNoiseMultiplier;
                }
                if (dNoiseMultiplier != 1.0)
                {
                    options.DLiabilityNoiseStdev *= dNoiseMultiplier;
                    options.DDamagesNoiseStdev *= dNoiseMultiplier;
                }
                return options;
            }

            AddToOptionsListWithName(optionSets, "baseline", GetBluffingBase(false));
            AddToOptionsListWithName(optionSets, "baseline-hidden", GetBluffingBase(true));

            AddToOptionsListWithName(optionSets, "twobr", GetBluffingBase(false, onlyTwoRounds: true));
            AddToOptionsListWithName(optionSets, "twobr-hidden", GetBluffingBase(true, onlyTwoRounds: true));

            AddToOptionsListWithName(optionSets, "bothunc", GetBluffingBase(false, liabilityAlsoUncertain: true));
            AddToOptionsListWithName(optionSets, "bothunc-hidden", GetBluffingBase(true, liabilityAlsoUncertain: true));

            AddToOptionsListWithName(optionSets, "cfrplus", GetBluffingBase(false, cfrPlus: true));
            AddToOptionsListWithName(optionSets, "cfrplus-hidden", GetBluffingBase(true, cfrPlus: true));

            AddToOptionsListWithName(optionSets, "locost", GetBluffingBase(false, costsMultiplier: 1.0 / 3.0));
            AddToOptionsListWithName(optionSets, "locost-hidden", GetBluffingBase(true, costsMultiplier: 1.0 / 3.0));
            AddToOptionsListWithName(optionSets, "hicost", GetBluffingBase(false, costsMultiplier: 3.0));
            AddToOptionsListWithName(optionSets, "hicost-hidden", GetBluffingBase(true, costsMultiplier: 3.0));

            AddToOptionsListWithName(optionSets, "pra", GetBluffingBase(false, pRiskAversion: true));
            AddToOptionsListWithName(optionSets, "pra-hidden", GetBluffingBase(true, pRiskAversion: true));
            AddToOptionsListWithName(optionSets, "dra", GetBluffingBase(false, dRiskAversion: true));
            AddToOptionsListWithName(optionSets, "dra-hidden", GetBluffingBase(true, dRiskAversion: true));
            AddToOptionsListWithName(optionSets, "bra", GetBluffingBase(false, pRiskAversion: true, dRiskAversion: true));
            AddToOptionsListWithName(optionSets, "bra-hidden", GetBluffingBase(true, pRiskAversion: true, dRiskAversion: true));

            AddToOptionsListWithName(optionSets, "goodinf", GetBluffingBase(false, pNoiseMultiplier: 0.5, dNoiseMultiplier: 0.5));
            AddToOptionsListWithName(optionSets, "goodinf-hidden", GetBluffingBase(true, pNoiseMultiplier: 0.5, dNoiseMultiplier: 0.5));
            AddToOptionsListWithName(optionSets, "badinf", GetBluffingBase(false, pNoiseMultiplier: 2.0, dNoiseMultiplier: 2.0));
            AddToOptionsListWithName(optionSets, "badinf-hidden", GetBluffingBase(true, pNoiseMultiplier: 2.0, dNoiseMultiplier: 2.0));
            AddToOptionsListWithName(optionSets, "pgdbinf", GetBluffingBase(false, pNoiseMultiplier: 0.5, dNoiseMultiplier: 2.0));
            AddToOptionsListWithName(optionSets, "pgdbinf-hidden", GetBluffingBase(true, pNoiseMultiplier: 0.5, dNoiseMultiplier: 2.0));
            AddToOptionsListWithName(optionSets, "pbdginf", GetBluffingBase(false, pNoiseMultiplier: 2.0, dNoiseMultiplier: 0.5));
            AddToOptionsListWithName(optionSets, "pbdginf-hidden", GetBluffingBase(true, pNoiseMultiplier: 2.0, dNoiseMultiplier: 0.5));
        }

        public List<GameOptions> GetOptionsSets2()
        {
            List<GameOptions> optionSets = new List<GameOptions>();

            List<ILitigGameDisputeGenerator> disputeGenerators;

            if (TestDisputeGeneratorVariations)
                disputeGenerators = new List<ILitigGameDisputeGenerator>()
                {
                    new LitigGameNegligenceDisputeGenerator(),
                    new LitigGameAppropriationDisputeGenerator(),
                    new LitigGameContractDisputeGenerator(),
                    new LitigGameDiscriminationDisputeGenerator(),
                };
            else
                disputeGenerators = new List<ILitigGameDisputeGenerator>()
                {
                    new LitigGameExogenousDisputeGenerator()
                    {
                        ExogenousProbabilityTrulyLiable = 0.5,
                        StdevNoiseToProduceLiabilityStrength = 0.5
                    }
                };

            foreach (double costMultiplier in CostsMultipliers)
                foreach (ILitigGameDisputeGenerator d in disputeGenerators)
                {
                    var options = LitigGameOptionsGenerator.BaseOptions();
                    options.PLiabilityNoiseStdev = options.DLiabilityNoiseStdev = StdevPlayerNoise;
                    options.CostsMultiplier = costMultiplier;
                    if (HigherRiskAversion)
                    {
                        if (PRiskAverse)
                            options.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { Alpha = 10.0 / 1000000.0 };
                        if (DRiskAverse)
                            options.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { Alpha = 10.0 / 1000000.0 };
                    }
                    else
                    {
                        if (PRiskAverse)
                            options.PUtilityCalculator = new LogRiskAverseUtilityCalculator() { InitialWealth = options.PInitialWealth };
                        if (DRiskAverse)
                            options.DUtilityCalculator = new LogRiskAverseUtilityCalculator() { InitialWealth = options.DInitialWealth };
                    }
                    options.LitigGameDisputeGenerator = d;
                    string generatorName = d.GetGeneratorName();
                    string fullName = generatorName;
                    if (costMultiplier != 1)
                        fullName += $" costs {costMultiplier}";
                    optionSets.AddRange(GetOptionsVariations(fullName, () => options));
                }
            return optionSets;
        }

        private List<GameOptions> GetOptionsVariations(string description, Func<LitigGameOptions> initialOptionsFunc)
        {
            var list = new List<GameOptions>();
            LitigGameOptions options;

            if (!LimitToAmerican)
            {
                options = initialOptionsFunc();
                options.LitigGameRunningSideBets = new LitigGameRunningSideBets()
                {
                    MaxChipsPerRound = 2,
                    ValueOfChip = 50000,
                    CountAllChipsInAbandoningRound = true,
                    TrialCostsMultiplierAsymptote = 3.0,
                    TrialCostsMultiplierWithDoubleStakes = 1.3,
                };
                AddToOptionsListWithName(list, description + " RunSide", options);
            }

            if (IncludeRunningSideBetVariations)
            {
                options = initialOptionsFunc();
                options.LitigGameRunningSideBets = new LitigGameRunningSideBets()
                {
                    MaxChipsPerRound = 2,
                    ValueOfChip = 50000,
                    CountAllChipsInAbandoningRound = false,
                    TrialCostsMultiplierAsymptote = 3.0,
                    TrialCostsMultiplierWithDoubleStakes = 1.3,
                };
                AddToOptionsListWithName(list, description + " RunSideEscap", options);

                options = initialOptionsFunc();
                options.LitigGameRunningSideBets = new LitigGameRunningSideBets()
                {
                    MaxChipsPerRound = 2,
                    ValueOfChip = 100000,
                    CountAllChipsInAbandoningRound = true,
                    TrialCostsMultiplierAsymptote = 3.0,
                    TrialCostsMultiplierWithDoubleStakes = 1.3,
                };
                AddToOptionsListWithName(list, description + " RunSideLarge", options);

                options = initialOptionsFunc();
                options.LitigGameRunningSideBets = new LitigGameRunningSideBets()
                {
                    MaxChipsPerRound = 2,
                    ValueOfChip = 50000,
                    TrialCostsMultiplierAsymptote = 3.0,
                    TrialCostsMultiplierWithDoubleStakes = 2.0,
                };
                AddToOptionsListWithName(list, description + " RunSideExp", options);
            }

            if (!LimitToAmerican)
            {
                options = initialOptionsFunc();
                options.LoserPays = true;
                options.LoserPaysMultiple = 1.0;
                options.LoserPaysAfterAbandonment = false;
                options.IncludeAgreementToBargainDecisions = true; // with the British rule, one might not want to make an offer at all
                AddToOptionsListWithName(list, description + " British", options);
            }

            options = initialOptionsFunc();
            AddToOptionsListWithName(list, description + " American", options);

            return list;
        }

        void AddToOptionsListWithName(List<GameOptions> list, string name, GameOptions options)
        {
            options.Name = name;
            list.Add(options);
        }

        // The following is used by the test classes
        public static LitigGameProgress PlayLitigGameOnce(LitigGameOptions options,
            Func<Decision, GameProgress, byte> actionsOverride)
        {
            LitigGameDefinition gameDefinition = new LitigGameDefinition();
            gameDefinition.Setup(options);
            List<Strategy> starterStrategies = Strategy.GetStarterStrategies(gameDefinition);

            if (GameProgressLogger.LoggingOn)
                gameDefinition.PrintOutOrderingInformation();

            bool useDirectGamePlayer = false; // potentially useful to test directgameplayer, but slower
            LitigGameProgress gameProgress = null;
            if (useDirectGamePlayer)
            {
                gameProgress = (LitigGameProgress)new LitigGameFactory().CreateNewGameProgress(true, new IterationID());
                DirectGamePlayer directGamePlayer = new DeepCFRDirectGamePlayer(DeepCFRMultiModelMode.DecisionSpecific, gameDefinition, gameProgress, true, false, default);
                gameProgress = (LitigGameProgress)directGamePlayer.PlayWithActionsOverride(actionsOverride);
            }
            else
            {
                GamePlayer gamePlayer = new GamePlayer(starterStrategies, false, gameDefinition, true);
                gameProgress = (LitigGameProgress)gamePlayer.PlayUsingActionOverride(actionsOverride);
            }

            return gameProgress;
        }


        void AddFeeShiftingArticleGames(List<GameOptions> options)
        {

            List<List<Func<LitigGameOptions, LitigGameOptions>>> transformations = new List<List<Func<LitigGameOptions, LitigGameOptions>>>()
            {
                CostsMultiplierTransformations(),
                RiskAversionTransformations()
            };
            var gameOptions = ApplyPermutationsOfTransformations(() => LitigGameOptionsGenerator.FeeShiftingArticleBase(), transformations);
            options.AddRange(gameOptions);
        }

        #region Transformation methods 


        List<Func<LitigGameOptions, LitigGameOptions>> CostsMultiplierTransformations()
        {
            List<Func<LitigGameOptions, LitigGameOptions>> results = new List<Func<LitigGameOptions, LitigGameOptions>>();
            foreach (double multiplier in CostsMultipliers)
                results.Add(o => GetAndTransform_CostsMultiplier(o, multiplier));
            return results;
        }

        List<Func<LitigGameOptions, LitigGameOptions>> RiskAversionTransformations() => new List<Func<LitigGameOptions, LitigGameOptions>>() { GetAndTransform_RiskAverse, GetAndTransform_RiskAverse, GetAndTransform_RiskAverse, GetAndTransform_RiskAverse };

        LitigGameOptions GetAndTransform_RiskAverse(LitigGameOptions options) => GetAndTransform(options, "-ra", g =>
        {
            g.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.PInitialWealth, Alpha = 10 * 0.000001 };
            g.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.DInitialWealth, Alpha = 10 * 0.000001 };
        });

        LitigGameOptions GetAndTransform_RiskNeutral(LitigGameOptions options) => GetAndTransform(options, "-rn", g =>
        {
            g.PUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = g.PInitialWealth };
            g.DUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = g.DInitialWealth };
        });
        LitigGameOptions GetAndTransform_POnlyRiskAverse(LitigGameOptions options) => GetAndTransform(options, "-ara", g =>
        {
            g.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.PInitialWealth, Alpha = 10 * 0.000001 };
            g.DUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = g.DInitialWealth };
        });
        LitigGameOptions GetAndTransform_DOnlyRiskAverse(LitigGameOptions options) => GetAndTransform(options, "-dara", g =>
        {
            g.PUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = g.PInitialWealth };
            g.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.DInitialWealth, Alpha = 10 * 0.000001 };
        });

        LitigGameOptions GetAndTransform_CostsMultiplier(LitigGameOptions options, double multiplier) => GetAndTransform(options, "-ra", g =>
        {
            g.CostsMultiplier = multiplier;
        });



        GameOptions GetAndTransformWithRiskAversion(string baseName, string suffix, Func<LitigGameOptions> baseOptionsFn, Action<LitigGameOptions> transform, RiskAversion riskAversion)
        {
            LitigGameOptions o = baseOptionsFn();
            transform(o);
            string nameRevised = baseName;
            if (riskAversion == RiskAversion.RiskAverse)
            {
                o.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = o.PInitialWealth, Alpha = 10 * 0.000001 };
                o.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = o.DInitialWealth, Alpha = 10 * 0.000001 };
                nameRevised += "-ra";
            }
            else if (riskAversion == RiskAversion.RiskNeutral)
            {
                o.PUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = o.PInitialWealth };
                o.DUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = o.DInitialWealth };
            }
            else if (riskAversion == RiskAversion.POnlyRiskAverse)
            {
                o.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = o.PInitialWealth, Alpha = 10 * 0.000001 };
                o.DUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = o.DInitialWealth };
                nameRevised += "-ara";
            }
            else if (riskAversion == RiskAversion.DOnlyRiskAverse)
            {
                o.PUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = o.PInitialWealth };
                o.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = o.DInitialWealth, Alpha = 10 * 0.000001 };
                nameRevised += "-dara";
            }
            o.Name = nameRevised;
            return o;
        }

        #endregion
    }
}
