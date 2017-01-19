using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "ShootoutAdjustmentsModule")]
    [Serializable]
    public class ShootoutAdjustmentsModule : AdjustmentsModule, ICodeBasedSettingGenerator
    {

        public LitigationGame LitigationGame { get { return (LitigationGame) Game; } }
        public ShootoutAdjustmentsModuleInputs ShootoutAdjustmentsModuleInputs { get { return (ShootoutAdjustmentsModuleInputs)GameModuleInputs; } }
        public ShootoutAdjustmentsModuleProgress ShootoutProgress { get { return (ShootoutAdjustmentsModuleProgress)GameModuleProgress; } }

        private void AddShootoutsToProgress()
        {
            BargainingModuleProgress bmp = LitigationGame.BargainingModule.BargainingProgress;

            ShootoutProgress.Shootouts = new List<LitigationShootout>();
            if (bmp.POfferList != null)
            {
                int offerCount = bmp.POfferList.Count;
                for (int bargainingRound = 0; bargainingRound < offerCount; bargainingRound++)
                {
                    if (bmp.SettlementBlockedThisBargainingRoundBecauseOfEvolution[bargainingRound] == false)
                    {
                        ShootoutProgress.Shootouts.Add(new LitigationShootout() { DiscountFactor = ShootoutAdjustmentsModuleInputs.DiscountFactorForShootouts, MultiplierOfDamagesForShootout = ShootoutAdjustmentsModuleInputs.MultiplierForShootouts, SettlementOfferIsPlaintiffs = true, SettlementOffer = (double)bmp.POfferList[bargainingRound] * LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim });
                        ShootoutProgress.Shootouts.Add(new LitigationShootout() { DiscountFactor = ShootoutAdjustmentsModuleInputs.DiscountFactorForShootouts, MultiplierOfDamagesForShootout = ShootoutAdjustmentsModuleInputs.MultiplierForShootouts, SettlementOfferIsPlaintiffs = false, SettlementOffer = (double)bmp.DOfferList[bargainingRound] * LitigationGame.DisputeGeneratorModule.DGProgress.DamagesClaim });
                    }
                }
            }
        }

        public override void ActionBeforeTrial()
        {
            AddShootoutsToProgress();
        }

        public override void AdjustDamagesAmounts(ref double ultimateDamagesIfPWins, ref double paymentFromPToDIfDWins)
        {
            // NOTE: This feature not currently enabled (to enforce when case dropped immediately afterward) because this only gets called when there is a trial.
            if (ShootoutAdjustmentsModuleInputs.EnforceWhenCaseDroppedImmediatelyAfterward && (ShootoutProgress.Shootouts == null || !ShootoutProgress.Shootouts.Any()))
                AddShootoutsToProgress(); // will occur if this is based on a drop rather than on a trial and we are enforcing the shootout when the case is dropped immediately afterward

            if (!ShootoutProgress.Shootouts.Any())
                return;

            double netPaymentFromDWhenPWins = 0, netPaymentFromDWhenDWins = 0;
            double ultimateDamagesIPWinsCopy = ultimateDamagesIfPWins; // to use in anonymous method
            double paymentFromPToDIfDWinsCopy = paymentFromPToDIfDWins;
            netPaymentFromDWhenPWins = ShootoutProgress.Shootouts.Sum(x => x.NetPaymentFromDToP(true, ultimateDamagesIPWinsCopy));
            netPaymentFromDWhenDWins = ShootoutProgress.Shootouts.Sum(x => x.NetPaymentFromDToP(false, 0 - paymentFromPToDIfDWinsCopy));
            ultimateDamagesIfPWins += netPaymentFromDWhenPWins;
            paymentFromPToDIfDWins -= netPaymentFromDWhenDWins;
        }

        //public override void ActionWhenApplyingLitigationCosts()
        //{
        //    BargainingModuleProgress bmp = LitigationGame.BargainingModule.BargainingProgress;

        //    if (LitigationGame.LGP.TrialOccurs ||
        //        (LitigationGame.LGP.DropInfo != null && ShootoutAdjustmentsModuleInputs.EnforceWhenCaseDroppedImmediatelyAfterward)  // to do: when extending to multiple rounds and adding dropping after each round, we need to test here when the case was dropped
        //        )
        //    {
        //        if (ShootoutProgress.Shootouts == null)
        //            AddShootoutsToProgress(); // will occur if this is based on a drop rather than on a trial

        //        if (!ShootoutProgress.Shootouts.Any())
        //            return;

        //        double netPaymentFromD = 0;
        //        if (LitigationGame.LGP.ScoreCalculatedProportionatelyRelativeToProbabilityOfVictory)
        //        {
        //            double probPWins = (double)LitigationGame.LGP.UltimateProbabilityOfPVictory;
        //            double netPaymentFromDWhenPWins = 0, netPaymentFromDWhenDWins = 0;
        //            netPaymentFromDWhenPWins = ShootoutProgress.Shootouts.Sum(x => x.NetPaymentFromDToP(true, (double)LitigationGame.LGP.UltimateDamagesIfPWins));
        //            netPaymentFromDWhenDWins = ShootoutProgress.Shootouts.Sum(x => x.NetPaymentFromDToP(false, 0));
        //            netPaymentFromD = probPWins * netPaymentFromDWhenPWins + (1.0 - probPWins) * netPaymentFromDWhenDWins;
        //        }
        //        else
        //        {
        //            bool pWins;
        //            if (LitigationGame.LGP.TrialOccurs)
        //                pWins = (bool)LitigationGame.LGP.PWins;
        //            else
        //                pWins = !LitigationGame.LGP.DropInfo.DroppedByPlaintiff;
        //            netPaymentFromD = ShootoutProgress.Shootouts.Sum(x => x.NetPaymentFromDToP(pWins, pWins ? (double)LitigationGame.LGP.DamagesPaymentFromDToP : 0));
        //        }
        //        LitigationGame.LGP.PFinalWealth += netPaymentFromD;
        //        LitigationGame.LGP.DFinalWealth -= netPaymentFromD;
        //    }
        //}


        public override void CreateInstanceAndInitializeProgress(Game theGame, List<Strategy> theStrategies, object gameModuleSettings, out GameModule theGameModule, out GameModuleProgress theGameModuleProgress)
        {
            ShootoutAdjustmentsModule copy = new ShootoutAdjustmentsModule();
            copy.SetGameAndStrategies(theGame, theStrategies, GameModuleName, GameModuleNamesThisModuleReliesOn, GameModuleNumbersThisModuleReliesOn, GameModuleSettings);
            SetGameModuleFields(copy);
            theGameModuleProgress = ShootoutAdjustmentsModuleProgress.GetRecycledOrAllocate();
            theGameModule = copy;
        }


        public virtual object GenerateSetting(string options)
        {
            List<Decision> decisions = new List<Decision>();

            int adjustmentsModuleNumber = GetIntCodeGeneratorOption(options, "AdjustModNumber");

            return new ShootoutAdjustmentsModule()
            {
                DecisionsCore = decisions,
                GameModuleName = "AdjustmentsModule" + adjustmentsModuleNumber,
                GameModuleNamesThisModuleReliesOn = new List<string>() { },
                Tags = new List<string> { "Adjustment" }
            };
        }

    }
}
