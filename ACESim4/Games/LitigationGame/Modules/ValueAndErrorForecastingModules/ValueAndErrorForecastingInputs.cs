using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class ValueAndErrorForecastingInputs : GameModuleInputs
    {
        public string ForecastingType;
        public double BiasAffectingEntireLegalSystem;
        public double BiasAffectingPlaintiff;
        public double BiasAffectingDefendant;
        public double BiasAffectingJudge;
        [SwapInputSeeds("RandomSeed00")]
        [FlipInputSeed]
        public double PRandomSeed00;
        [SwapInputSeeds("RandomSeed01")]
        [FlipInputSeed]
        public double PRandomSeed01;
        [SwapInputSeeds("RandomSeed02")]
        [FlipInputSeed]
        public double PRandomSeed02;
        [SwapInputSeeds("RandomSeed03")]
        [FlipInputSeed]
        public double PRandomSeed03;
        [SwapInputSeeds("RandomSeed04")]
        [FlipInputSeed]
        public double PRandomSeed04;
        [SwapInputSeeds("RandomSeed05")]
        [FlipInputSeed]
        public double PRandomSeed05;
        [SwapInputSeeds("RandomSeed06")]
        [FlipInputSeed]
        public double PRandomSeed06;
        [SwapInputSeeds("RandomSeed07")]
        [FlipInputSeed]
        public double PRandomSeed07;
        [SwapInputSeeds("RandomSeed08")]
        [FlipInputSeed]
        public double PRandomSeed08;
        [SwapInputSeeds("RandomSeed09")]
        [FlipInputSeed]
        public double PRandomSeed09;
        [SwapInputSeeds("RandomSeed10")]
        [FlipInputSeed]
        public double PRandomSeed10;
        [SwapInputSeeds("RandomSeed00")]
        [FlipInputSeed]
        public double DRandomSeed00;
        [SwapInputSeeds("RandomSeed01")]
        [FlipInputSeed]
        public double DRandomSeed01;
        [SwapInputSeeds("RandomSeed02")]
        [FlipInputSeed]
        public double DRandomSeed02;
        [SwapInputSeeds("RandomSeed03")]
        [FlipInputSeed]
        public double DRandomSeed03;
        [SwapInputSeeds("RandomSeed04")]
        [FlipInputSeed]
        public double DRandomSeed04;
        [SwapInputSeeds("RandomSeed05")]
        [FlipInputSeed]
        public double DRandomSeed05;
        [SwapInputSeeds("RandomSeed06")]
        [FlipInputSeed]
        public double DRandomSeed06;
        [SwapInputSeeds("RandomSeed07")]
        [FlipInputSeed]
        public double DRandomSeed07;
        [SwapInputSeeds("RandomSeed08")]
        [FlipInputSeed]
        public double DRandomSeed08;
        [SwapInputSeeds("RandomSeed09")]
        [FlipInputSeed]
        public double DRandomSeed09;
        [SwapInputSeeds("RandomSeed10")]
        [FlipInputSeed]
        public double DRandomSeed10;

    }
}
