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
    public class MyGameLauncher : Launcher
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

        public override GameDefinition GetGameDefinition() => new MyGameDefinition();

        public override GameOptions GetSingleGameOptions() => MyGameOptionsGenerator.GetMyGameOptions();

        private enum OptionSetChoice
        {
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
            KlermanEtAl_DamagesUncertainty
        }

        public override List<(string optionSetName, GameOptions options)> GetOptionsSets()
        {
            List<(string optionSetName, GameOptions options)> optionSets = new List<(string optionSetName, GameOptions options)>();
            OptionSetChoice optionSetChoice = OptionSetChoice.KlermanEtAl_Options;
            switch (optionSetChoice)
            {
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
                    AddKlermanEtAlPermutations(optionSets, MyGameOptionsGenerator.KlermanEtAl);
                    break;
                case OptionSetChoice.KlermanEtAl_MultipleStrengthPoints:
                    AddKlermanEtAlPermutations(optionSets, MyGameOptionsGenerator.KlermanEtAl_MultipleStrengthPoints);
                    break;
                case OptionSetChoice.KlermanEtAl_Options:
                    AddKlermanEtAlPermutations(optionSets, MyGameOptionsGenerator.KlermanEtAl_WithOptions);
                    break;
                case OptionSetChoice.KlermanEtAl_DamagesUncertainty:
                    AddKlermanEtAlPermutations(optionSets, MyGameOptionsGenerator.KlermanEtAl_WithDamagesUncertainty);
                    break;
            }

            optionSets = optionSets.OrderBy(x => x.optionSetName).ToList();

            bool simplify = false; // Enable for debugging purposes to speed up execution without going all the way to "fast" option
            if (simplify)
                foreach (var optionSet in optionSets)
                    optionSet.options.Simplify();

            return optionSets;
        }


        private void AddCustom2(List<(string optionSetName, GameOptions options)> optionSets)
        {

            // now, liability and damages only
            foreach (RiskAversion riskAverse in new RiskAversion[] { RiskAversion.DOnlyRiskAverse })
            {
                foreach ((string name, double costsMultiplier) in new (string name, double costsMultiplier)[] { ("basecosts", 1.0) })
                {
                    optionSets.Add(GetAndTransform("noshootout", name, MyGameOptionsGenerator.BothUncertain_2BR, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                    optionSets.Add(GetAndTransform("shootout", name, MyGameOptionsGenerator.Shootout, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                }
            }
        }

        private void AddWithVariedAlgorithms(List<(string optionSetName, GameOptions options)> optionSets)
        {
            void Helper(List<(string optionSetName, GameOptions options)> optionSets, string optionSetNamePrefix, Action<EvolutionSettings> modifyEvolutionSettings)
            {
                optionSets.Add(GetAndTransform(optionSetNamePrefix + "damages_unc", "basecosts", MyGameOptionsGenerator.DamagesUncertainty_1BR, x => { x.CostsMultiplier = 1.0; x.ModifyEvolutionSettings = modifyEvolutionSettings; }, RiskAversion.RiskNeutral));
                //optionSets.Add(GetAndTransform(optionSetNamePrefix + "damages_unc2BR", "basecosts", MyGameOptionsGenerator.DamagesUncertainty_2BR, x => { x.CostsMultiplier = 1.0; x.ModifyEvolutionSettings = modifyEvolutionSettings; }, false));
                optionSets.Add(GetAndTransform(optionSetNamePrefix + "liability_unc", "basecosts", MyGameOptionsGenerator.LiabilityUncertainty_1BR, x => { x.CostsMultiplier = 1.0; x.ModifyEvolutionSettings = modifyEvolutionSettings; }, RiskAversion.RiskNeutral));
                //optionSets.Add(GetAndTransform(optionSetNamePrefix + "liability_unc2BR", "basecosts", MyGameOptionsGenerator.LiabilityUncertainty_2BR, x => { x.CostsMultiplier = 1.0; x.ModifyEvolutionSettings = modifyEvolutionSettings; }, false));
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

        private void AddVariousUncertainties(List<(string optionSetName, GameOptions options)> optionSets)
        {

            optionSets.Add(GetAndTransform("damages_unc", "basecosts", MyGameOptionsGenerator.DamagesUncertainty_1BR, x => { x.CostsMultiplier = 1.0; }, RiskAversion.RiskNeutral));
            optionSets.Add(GetAndTransform("damages_unc2BR", "basecosts", MyGameOptionsGenerator.DamagesUncertainty_2BR, x => { x.CostsMultiplier = 1.0; }, RiskAversion.RiskNeutral));
            optionSets.Add(GetAndTransform("liability_unc", "basecosts", MyGameOptionsGenerator.LiabilityUncertainty_1BR, x => { x.CostsMultiplier = 1.0; }, RiskAversion.RiskNeutral));
            optionSets.Add(GetAndTransform("liability_unc2BR", "basecosts", MyGameOptionsGenerator.LiabilityUncertainty_2BR, x => { x.CostsMultiplier = 1.0; }, RiskAversion.RiskNeutral));
            optionSets.Add(GetAndTransform("both_unc", "basecosts", MyGameOptionsGenerator.BothUncertain_1BR, x => { x.CostsMultiplier = 1.0; }, RiskAversion.RiskNeutral));
            optionSets.Add(GetAndTransform("both_unc2BR", "basecosts", MyGameOptionsGenerator.BothUncertain_2BR, x => { x.CostsMultiplier = 1.0; }, RiskAversion.RiskNeutral));

        }

        private void AddFast(List<(string optionSetName, GameOptions options)> optionSets)
        {
            RiskAversion riskAverse = RiskAversion.RiskNeutral;
            foreach ((string name, double costsMultiplier) in new (string name, double costsMultiplier)[] { ("basecosts", 1.0), ("highcosts", 3.0) })
            {
                optionSets.Add(GetAndTransform("fast1", name, MyGameOptionsGenerator.SuperSimple, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
            }
        }

        private void AddShootoutMainPermutatio(List<(string optionSetName, GameOptions options)> optionSets)
        {
            RiskAversion riskAverse = RiskAversion.RiskNeutral;
            foreach ((string name, double costsMultiplier) in new (string name, double costsMultiplier)[] { ("basecosts", 1.0), ("highcosts", 3.0) })
            {
                optionSets.Add(GetAndTransform("noshootout", name, MyGameOptionsGenerator.BothUncertain_2BR, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                optionSets.Add(GetAndTransform("shootout", name, MyGameOptionsGenerator.Shootout, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                optionSets.Add(GetAndTransform("sotrip", name, MyGameOptionsGenerator.Shootout_Triple, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
            }
        }

        private void AddShootoutPermutations(List<(string optionSetName, GameOptions options)> optionSets)
        {
            foreach (RiskAversion riskAverse in new RiskAversion[] { RiskAversion.RiskNeutral, RiskAversion.RiskAverse })
            {
                foreach ((string name, double costsMultiplier) in new (string name, double costsMultiplier)[] { ("lowcosts", 1.0 / 3.0), ("basecosts", 1.0), ("highcosts", 3.0) })
                {
                    //optionSets.Add(GetAndTransform("liab", name, MyGameOptionsGenerator.LiabilityUncertainty_2BR, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                    //optionSets.Add(GetAndTransform("dam", name, MyGameOptionsGenerator.DamagesUncertainty_2BR, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                    optionSets.Add(GetAndTransform("noshootout", name, MyGameOptionsGenerator.BothUncertain_2BR, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                    optionSets.Add(GetAndTransform("shootout", name, MyGameOptionsGenerator.Shootout, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                    optionSets.Add(GetAndTransform("soallrounds", name, MyGameOptionsGenerator.Shootout_AllRounds, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                    optionSets.Add(GetAndTransform("soabandon", name, MyGameOptionsGenerator.Shootout_IncludingAbandoment, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                    optionSets.Add(GetAndTransform("soallraban", name, MyGameOptionsGenerator.Shootout_AllRoundsIncludingAbandoment, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                    optionSets.Add(GetAndTransform("sotrip", name, MyGameOptionsGenerator.Shootout_Triple, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                }
            }
        }

        private void AddKlermanEtAlPermutations(List<(string optionSetName, GameOptions options)> optionSets, Func<MyGameOptions> myGameOptionsFunc)
        {
            foreach (double exogProb in new double[] { 0.05, 0.1, 0.15, 0.2, 0.25, 0.3, 0.35, 0.4, 0.45, 0.5, 0.55, 0.6, 0.65, 0.7, 0.75, 0.8, 0.85, 0.9, 0.95 })
            {
                optionSets.Add(GetAndTransform("klerman", exogProb.ToString(), myGameOptionsFunc, x =>
                {
                    x.IncludeCourtSuccessReport = false;
                    x.IncludeSignalsReport = false;
                    x.FirstRowOnly = true;
                    ((MyGameExogenousDisputeGenerator)x.MyGameDisputeGenerator).ExogenousProbabilityTrulyLiable = exogProb;
                }, RiskAversion.RiskNeutral));
            }
        }

        private void AddShootoutGameVariations(List<(string optionSetName, GameOptions options)> optionSets)
        {
            foreach (RiskAversion riskAverse in new RiskAversion[] { RiskAversion.RiskNeutral })
            {
                // different levels of costs, same for both parties
                foreach ((string name, double costsMultiplier) in new (string name, double costsMultiplier)[] { ("basecosts", 1.0), ("lowcosts", 1.0 / 3.0), ("highcosts", 3) })
                {
                    optionSets.Add(GetAndTransform("noshootout", name, MyGameOptionsGenerator.BothUncertain_2BR, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                    optionSets.Add(GetAndTransform("shootout", name, MyGameOptionsGenerator.Shootout, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                }
                // try lowering all costs dramatically except trial costs
                foreach ((string name, double costsMultiplier) in new (string name, double costsMultiplier)[] { ("lowpretrialcosts", 1.0 / 10.0) })
                {
                    optionSets.Add(GetAndTransform("noshootout", name, MyGameOptionsGenerator.BothUncertain_2BR, x => { x.CostsMultiplier = costsMultiplier; x.PTrialCosts *= 1.0 / costsMultiplier; x.DTrialCosts *= 1.0 / costsMultiplier;  }, riskAverse));
                    optionSets.Add(GetAndTransform("shootout", name, MyGameOptionsGenerator.Shootout, x => { x.CostsMultiplier = costsMultiplier; x.PTrialCosts *= 1.0 / costsMultiplier; x.DTrialCosts *= 1.0 / costsMultiplier; }, riskAverse));
                }
                // Now, we'll do asymmetric trial costs. We assume that other costs are the same. This might make sense in the event that trial has a publicity cost for one party. 
                //foreach ((string name, double dCostsMultiplier) in new (string name, double dCostsMultiplier)[] { ("phighcost", 0.5), ("dhighcost", 2.0) })
                //{
                //    Action<MyGameOptions> transform = x =>
                //    {
                //        x.DTrialCosts *= dCostsMultiplier;
                //        x.PTrialCosts /= dCostsMultiplier;
                //    };
                //    optionSets.Add(GetAndTransform("noshootout", name, MyGameOptionsGenerator.Usual, transform, riskAverse));
                //    optionSets.Add(GetAndTransform("shootout", name, MyGameOptionsGenerator.Shootout, transform, riskAverse));
                //}
            }
            // now, add a set with regular and asymmetric risk aversion
            foreach (RiskAversion riskAverse in new RiskAversion[] { RiskAversion.RiskAverse, RiskAversion.POnlyRiskAverse, RiskAversion.DOnlyRiskAverse })
            {
                foreach ((string name, double costsMultiplier) in new (string name, double costsMultiplier)[] { ("basecosts", 1.0) })
                {
                    optionSets.Add(GetAndTransform("noshootout", name, MyGameOptionsGenerator.BothUncertain_2BR, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                    optionSets.Add(GetAndTransform("shootout", name, MyGameOptionsGenerator.Shootout, x => { x.CostsMultiplier = costsMultiplier; }, riskAverse));
                }
            }

            // now, vary strength of information
            foreach (RiskAversion riskAverse in new RiskAversion[] { RiskAversion.RiskNeutral })
            {
                foreach ((string name, double costsMultiplier) in new (string name, double costsMultiplier)[] { ("basecosts", 1.0) })
                {
                    foreach ((string infoName, double pInfoChange, double dInfoChange) in new (string infoName, double pInfoChange, double dInfoChange)[] { ("betterinfo", 0.5, 0.5), ("worseinfo", 2.0, 2.0), ("dbetterinfo", 2.0, 0.5) })
                    {
                        Action<MyGameOptions> transform = x =>
                        {
                            x.CostsMultiplier = costsMultiplier;
                            x.PDamagesNoiseStdev *= pInfoChange;
                            x.PLiabilityNoiseStdev *= pInfoChange;
                            x.DDamagesNoiseStdev *= dInfoChange;
                            x.DLiabilityNoiseStdev *= dInfoChange;
                        };
                        optionSets.Add(GetAndTransform("noshootout", infoName + "-" + name, MyGameOptionsGenerator.BothUncertain_2BR, transform, riskAverse));
                        optionSets.Add(GetAndTransform("shootout", infoName + "-" + name, MyGameOptionsGenerator.Shootout, transform, riskAverse));
                    }
                }
            }

            // now, liability and damages only
            foreach (RiskAversion riskAverse in new RiskAversion[] { RiskAversion.RiskNeutral })
            {
                foreach ((string name, double costsMultiplier) in new (string name, double costsMultiplier)[] { ("basecosts", 1.0) })
                {
                    Action<MyGameOptions> transform = x => { };
                    optionSets.Add(GetAndTransform("dam-noshootout", name, MyGameOptionsGenerator.DamagesUncertainty_2BR, transform, riskAverse));
                    optionSets.Add(GetAndTransform("dam-shootout", name, MyGameOptionsGenerator.DamagesShootout, transform, riskAverse)); 
                    optionSets.Add(GetAndTransform("liab-noshootout", name, MyGameOptionsGenerator.LiabilityUncertainty_2BR, transform, riskAverse));
                    optionSets.Add(GetAndTransform("liab-shootout", name, MyGameOptionsGenerator.LiabilityShootout, transform, riskAverse));
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

        (string optionSetName, MyGameOptions options) GetAndTransform(string baseName, string suffix, Func<MyGameOptions> baseOptionsFn, Action<MyGameOptions> transform, RiskAversion riskAversion)
        {
            MyGameOptions g = baseOptionsFn();
            transform(g);
            string suffix2 = suffix;
            if (riskAversion == RiskAversion.RiskAverse)
            {
                g.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.PInitialWealth, Alpha = 10 * 0.000001 };
                g.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.DInitialWealth, Alpha = 10 * 0.000001 };
                suffix2 += "-ra";
            }
            else if (riskAversion == RiskAversion.RiskNeutral)
            {
                g.PUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = g.PInitialWealth };
                g.DUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = g.DInitialWealth };
            }
            else if (riskAversion == RiskAversion.POnlyRiskAverse)
            {
                g.PUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.PInitialWealth, Alpha = 10 * 0.000001 };
                g.DUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = g.DInitialWealth };
                suffix2 += "-ara";
            }
            else if (riskAversion == RiskAversion.DOnlyRiskAverse)
            {
                g.PUtilityCalculator = new RiskNeutralUtilityCalculator() { InitialWealth = g.DInitialWealth };
                g.DUtilityCalculator = new CARARiskAverseUtilityCalculator() { InitialWealth = g.PInitialWealth, Alpha = 10 * 0.000001 };
                suffix2 += "-dara";
            }
            return (baseName + suffix2, g);
        }

        public void AddBluffingOptionsSets(List<(string optionSetName, GameOptions options)> optionSets)
        {
            MyGameOptions GetBluffingBase(bool hiddenOffers, bool onlyTwoRounds = false, bool liabilityAlsoUncertain = false, bool cfrPlus = false, double? costsMultiplier = null, bool pRiskAversion = false, bool dRiskAversion = false, double pNoiseMultiplier = 1.0, double dNoiseMultiplier = 1.0)
            {
                MyGameOptions options = (onlyTwoRounds, liabilityAlsoUncertain) switch
                {
                    (false, false) => MyGameOptionsGenerator.DamagesUncertainty_3BR(),
                    (false, true) => MyGameOptionsGenerator.BothUncertain_3BR(),
                    (true, false) => MyGameOptionsGenerator.DamagesUncertainty_2BR(),
                    (true, true) => MyGameOptionsGenerator.BothUncertain_2BR(),
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

            optionSets.Add(("baseline", GetBluffingBase(false)));
            optionSets.Add(("baseline-hidden", GetBluffingBase(true)));

            optionSets.Add(("twobr", GetBluffingBase(false, onlyTwoRounds: true)));
            optionSets.Add(("twobr-hidden", GetBluffingBase(true, onlyTwoRounds: true)));

            optionSets.Add(("bothunc", GetBluffingBase(false, liabilityAlsoUncertain: true)));
            optionSets.Add(("bothunc-hidden", GetBluffingBase(true, liabilityAlsoUncertain: true)));

            optionSets.Add(("cfrplus", GetBluffingBase(false, cfrPlus: true)));
            optionSets.Add(("cfrplus-hidden", GetBluffingBase(true, cfrPlus: true)));

            optionSets.Add(("locost", GetBluffingBase(false, costsMultiplier: 1.0 / 3.0)));
            optionSets.Add(("locost-hidden", GetBluffingBase(true, costsMultiplier: 1.0 / 3.0)));
            optionSets.Add(("hicost", GetBluffingBase(false, costsMultiplier: 3.0)));
            optionSets.Add(("hicost-hidden", GetBluffingBase(true, costsMultiplier: 3.0)));

            optionSets.Add(("pra", GetBluffingBase(false, pRiskAversion: true)));
            optionSets.Add(("pra-hidden", GetBluffingBase(true, pRiskAversion: true)));
            optionSets.Add(("dra", GetBluffingBase(false, dRiskAversion: true)));
            optionSets.Add(("dra-hidden", GetBluffingBase(true, dRiskAversion: true)));
            optionSets.Add(("bra", GetBluffingBase(false, pRiskAversion: true, dRiskAversion: true)));
            optionSets.Add(("bra-hidden", GetBluffingBase(true, pRiskAversion: true, dRiskAversion: true)));

            optionSets.Add(("goodinf", GetBluffingBase(false, pNoiseMultiplier: 0.5, dNoiseMultiplier: 0.5 )));
            optionSets.Add(("goodinf-hidden", GetBluffingBase(true, pNoiseMultiplier: 0.5, dNoiseMultiplier: 0.5))); 
            optionSets.Add(("badinf", GetBluffingBase(false, pNoiseMultiplier: 2.0, dNoiseMultiplier: 2.0)));
            optionSets.Add(("badinf-hidden", GetBluffingBase(true, pNoiseMultiplier: 2.0, dNoiseMultiplier: 2.0)));
            optionSets.Add(("pgdbinf", GetBluffingBase(false, pNoiseMultiplier: 0.5, dNoiseMultiplier: 2.0)));
            optionSets.Add(("pgdbinf-hidden", GetBluffingBase(true, pNoiseMultiplier: 0.5, dNoiseMultiplier: 2.0)));
            optionSets.Add(("pbdginf", GetBluffingBase(false, pNoiseMultiplier: 2.0, dNoiseMultiplier: 0.5)));
            optionSets.Add(("pbdginf-hidden", GetBluffingBase(true, pNoiseMultiplier: 2.0, dNoiseMultiplier: 0.5)));
        }

        public List<(string optionSetName, GameOptions options)> GetOptionsSets2()
        {
            List<(string optionSetName, GameOptions options)> optionSets = new List<(string optionSetName, GameOptions options)>();

            List<IMyGameDisputeGenerator> disputeGenerators;

            if (TestDisputeGeneratorVariations)
                disputeGenerators = new List<IMyGameDisputeGenerator>()
                {
                    new MyGameNegligenceDisputeGenerator(),
                    new MyGameAppropriationDisputeGenerator(),
                    new MyGameContractDisputeGenerator(),
                    new MyGameDiscriminationDisputeGenerator(),
                };
            else
                disputeGenerators = new List<IMyGameDisputeGenerator>()
                {
                    new MyGameExogenousDisputeGenerator()
                    {
                        ExogenousProbabilityTrulyLiable = 0.5,
                        StdevNoiseToProduceLiabilityStrength = 0.5
                    }
                };

            foreach (double costMultiplier in CostsMultipliers)
                foreach (IMyGameDisputeGenerator d in disputeGenerators)
                {
                    var options = MyGameOptionsGenerator.BaseOptions();
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
                    options.MyGameDisputeGenerator = d;
                    string generatorName = d.GetGeneratorName();
                    string fullName = generatorName;
                    if (costMultiplier != 1)
                        fullName += $" costs {costMultiplier}";
                    optionSets.AddRange(GetOptionsVariations(fullName, () => options));
                }
            return optionSets;
        }

        private List<(string optionSetName, GameOptions options)> GetOptionsVariations(string description, Func<MyGameOptions> initialOptionsFunc)
        {
            var list = new List<(string optionSetName, GameOptions options)>();
            MyGameOptions options;

            if (!LimitToAmerican)
            {
                options = initialOptionsFunc();
                options.MyGameRunningSideBets = new MyGameRunningSideBets()
                {
                    MaxChipsPerRound = 2,
                    ValueOfChip = 50000,
                    CountAllChipsInAbandoningRound = true,
                    TrialCostsMultiplierAsymptote = 3.0,
                    TrialCostsMultiplierWithDoubleStakes = 1.3,
                };
                list.Add((description + " RunSide", options));
            }

            if (IncludeRunningSideBetVariations)
            {
                options = initialOptionsFunc();
                options.MyGameRunningSideBets = new MyGameRunningSideBets()
                {
                    MaxChipsPerRound = 2,
                    ValueOfChip = 50000,
                    CountAllChipsInAbandoningRound = false,
                    TrialCostsMultiplierAsymptote = 3.0,
                    TrialCostsMultiplierWithDoubleStakes = 1.3,
                };
                list.Add((description + " RunSideEscap", options));

                options = initialOptionsFunc();
                options.MyGameRunningSideBets = new MyGameRunningSideBets()
                {
                    MaxChipsPerRound = 2,
                    ValueOfChip = 100000,
                    CountAllChipsInAbandoningRound = true,
                    TrialCostsMultiplierAsymptote = 3.0,
                    TrialCostsMultiplierWithDoubleStakes = 1.3,
                };
                list.Add((description + " RunSideLarge", options));

                options = initialOptionsFunc();
                options.MyGameRunningSideBets = new MyGameRunningSideBets()
                {
                    MaxChipsPerRound = 2,
                    ValueOfChip = 50000,
                    TrialCostsMultiplierAsymptote = 3.0,
                    TrialCostsMultiplierWithDoubleStakes = 2.0,
                };
                list.Add((description + " RunSideExp", options));
            }

            if (!LimitToAmerican)
            {
                options = initialOptionsFunc();
                options.LoserPays = true;
                options.LoserPaysMultiple = 1.0;
                options.LoserPaysAfterAbandonment = false;
                options.IncludeAgreementToBargainDecisions = true; // with the British rule, one might not want to make an offer at all
                list.Add((description + " British", options));
            }

            options = initialOptionsFunc();
            list.Add((description + " American", options));

            return list;
        }

        // The following is used by the test classes
        public static MyGameProgress PlayMyGameOnce(MyGameOptions options,
            Func<Decision, GameProgress, byte> actionsOverride)
        {
            MyGameDefinition gameDefinition = new MyGameDefinition();
            gameDefinition.Setup(options);
            List<Strategy> starterStrategies = Strategy.GetStarterStrategies(gameDefinition);

            if (GameProgressLogger.LoggingOn)
                gameDefinition.PrintOutOrderingInformation();

            bool useDirectGamePlayer = true; // DEBUG useful during testing, but slower
            MyGameProgress gameProgress = null;
            if (useDirectGamePlayer)
            {
                gameProgress = (MyGameProgress) new MyGameFactory().CreateNewGameProgress(new IterationID());
                DirectGamePlayer directGamePlayer = new DeepCFRDirectGamePlayer(DeepCFRMultiModelMode.DecisionSpecific, gameDefinition, gameProgress, true, default, null);
                gameProgress = (MyGameProgress) directGamePlayer.PlayWithActionsOverride(actionsOverride);
            }
            else
            {
                GamePlayer gamePlayer = new GamePlayer(starterStrategies, false, gameDefinition);
                gameProgress = (MyGameProgress)gamePlayer.PlayUsingActionOverride(actionsOverride);
            }

            return gameProgress;
        }
    }
}
