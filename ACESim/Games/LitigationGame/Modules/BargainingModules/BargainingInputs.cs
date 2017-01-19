using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class BargainingInputs : GameModuleInputs
    {
        public bool PartiesTakeIntoAccountPreviousOffer;
        public bool PartiesConsiderAccuracyOfOwnEstimates; /* This affects the inputs to bargaining and indirectly drop/default decisions. If the game is being played with a range of possible noise levels for a player, or if decisions in the game might lead to different levels of accuracy, this should be true. Otherwise, it should be left at false to improve the accuracy of the neural networks */
        public bool ConsiderTasteForFairness;
        public bool TasteForFairnessOnlySelfRegarding; /* If true, a high taste for fairness means strong resistance to a settlement that is unfair for the player, but not one that is unfair for the other player */
        [SwapInputSeeds("TasteForFairness")]
        public double PTasteForFairness;
        [SwapInputSeeds("TasteForFairness")]
        public double DTasteForFairness;
        [SwapInputSeeds("TasteForSettlement")]
        public double PTasteForSettlement;
        [SwapInputSeeds("TasteForSettlement")]
        public double DTasteForSettlement;
        [SwapInputSeeds("RegretAversion")]
        public double PRegretAversion;
        [SwapInputSeeds("RegretAversion")]
        public double DRegretAversion;
    }
}
