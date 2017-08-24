using ACESim.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    /// <summary>
    /// A dummy Game subclass to show a basic Game implementation.
    /// </summary>
    public class MyGame : Game
    {
        public MyGameDefinition MyDefinition => (MyGameDefinition)GameDefinition;
        public MyGameProgress MyProgress => (MyGameProgress)Progress;

        public override bool DecisionIsNeeded(Decision currentDecision)
        {
            if (currentDecision.DecisionByteCode == (byte) MyGameDecisions.MutualGiveUp)
                return MyProgress.PTriesAbandon && MyProgress.DTriesDefault;
            if (currentDecision.DecisionByteCode == (byte)MyGameDecisions.CourtDecision)
                return !MyProgress.CaseSettles;
            return true;
        }

        public override void UpdateGameProgressFollowingAction(byte currentDecisionByteCode, byte action)
        {
            switch (currentDecisionByteCode)
            {
                case (byte)MyGameDecisions.LitigationQuality:
                    MyProgress.PInitialWealth = MyDefinition.Options.PInitialWealth;
                    MyProgress.DInitialWealth = MyDefinition.Options.DInitialWealth;
                    MyProgress.DamagesAlleged = MyDefinition.Options.DamagesAlleged;
                    MyProgress.LitigationQualityDiscrete = action;
                    MyProgress.LitigationQualityUniform = ConvertActionToUniformDistributionDraw(action);
                    // If one or both parties have perfect information, then they can get their information about litigation quality now, since they don't need a signal. Note that we also specify in the game definition that the litigation quality should become part of their information set.
                    if (MyDefinition.Options.PNoiseStdev == 0)
                        MyProgress.PSignalUniform = MyProgress.LitigationQualityUniform;
                    if (MyDefinition.Options.DNoiseStdev == 0)
                        MyProgress.DSignalUniform = MyProgress.LitigationQualityUniform;
                break;
                case (byte)MyGameDecisions.PSignal:
                    if (MyDefinition.Options.UseRawSignals)
                        MyDefinition.GetDiscreteSignal(MyProgress.LitigationQualityDiscrete, action, true,
                            out MyProgress.PSignalDiscrete, out MyProgress.PSignalUniform);
                    else
                        ConvertNoiseActionToDiscreteAndUniformSignal(action, MyDefinition.Options.UseRawSignals,
                            MyProgress.LitigationQualityUniform, MyDefinition.Options.NumNoiseValues,
                            MyDefinition.Options.PNoiseStdev, MyDefinition.Options.NumSignals,
                            out MyProgress.PSignalDiscrete, out MyProgress.PSignalUniform);
                    //System.Diagnostics.Debug.WriteLine($"P: Quality {MyProgress.LitigationQualityUniform} Noise action {action} => signal {MyProgress.PSignalDiscrete} ({MyProgress.PSignalUniform})");
                break;
                case (byte)MyGameDecisions.DSignal:
                    if (MyDefinition.Options.UseRawSignals)
                        MyDefinition.GetDiscreteSignal(MyProgress.LitigationQualityDiscrete, action, false,
                            out MyProgress.DSignalDiscrete, out MyProgress.DSignalUniform);
                    else
                        ConvertNoiseActionToDiscreteAndUniformSignal(action, MyDefinition.Options.UseRawSignals,
                            MyProgress.LitigationQualityUniform, MyDefinition.Options.NumNoiseValues,
                            MyDefinition.Options.DNoiseStdev, MyDefinition.Options.NumSignals,
                            out MyProgress.DSignalDiscrete, out MyProgress.DSignalUniform);
                    //System.Diagnostics.Debug.WriteLine($"D: Quality {MyProgress.LitigationQualityUniform} Noise action {action} => signal {MyProgress.DSignalDiscrete} ({MyProgress.DSignalUniform})");
                    break;
                case (byte)MyGameDecisions.PFile:
                    MyProgress.PFiles = action == 1;
                    if (!MyProgress.PFiles)
                        MyProgress.GameComplete = true;
                    break;
                case (byte)MyGameDecisions.DAnswer:
                    MyProgress.DAnswers = action == 1;
                    if (!MyProgress.DAnswers)
                        MyProgress.GameComplete = true;
                    break;
                case (byte)MyGameDecisions.POffer:
                    double offer = GetOfferBasedOnAction(action, true);
                    MyProgress.AddOffer(true, offer);
                    MyProgress.UpdateProgress(MyDefinition);
                break;
                case (byte)MyGameDecisions.DOffer:
                    offer = GetOfferBasedOnAction(action, false);
                    MyProgress.AddOffer(false, offer);
                    MyProgress.UpdateProgress(MyDefinition);
                break;
                case (byte)MyGameDecisions.PResponse:
                    MyProgress.AddResponse(true, action == 1); // 1 == accept, 2 == reject
                    MyProgress.UpdateProgress(MyDefinition);
                break;
                case (byte)MyGameDecisions.DResponse:
                    MyProgress.AddResponse(false, action == 1); // 1 == accept, 2 == reject
                    MyProgress.UpdateProgress(MyDefinition);
                break;
                case (byte)MyGameDecisions.PAbandon:
                    MyProgress.PTriesAbandon = action == 1;
                    break;
                case (byte)MyGameDecisions.DDefault:
                    MyProgress.DTriesDefault = action == 1;
                    if (MyProgress.PTriesAbandon ^ MyProgress.DTriesDefault)
                    {
                        // exactly one party gives up
                        MyProgress.PAbandons = MyProgress.PTriesAbandon;
                        MyProgress.DDefaults = MyProgress.DTriesDefault;
                        MyProgress.TrialOccurs = false;
                        MyProgress.GameComplete = true;
                    }
                    break;
                case (byte)MyGameDecisions.MutualGiveUp:
                    // both trying to give up simultaneously! revise with a coin flip
                    MyProgress.PAbandons = action == 1;
                    MyProgress.DDefaults = !MyProgress.PAbandons;
                    MyProgress.GameComplete = true;
                    break;
                case (byte)MyGameDecisions.CourtDecision:
                    MyProgress.TrialOccurs = true;
                    if (MyDefinition.Options.UseRawSignals)
                    {
                        double courtNoiseUniformDistribution =
                            EquallySpaced.GetLocationOfEquallySpacedPoint(action - 1 /* make it zero-based */,
                                MyDefinition.Options.NumSignals);
                        double courtNoiseNormalDraw = InvNormal.Calculate(courtNoiseUniformDistribution) *
                                                        MyDefinition.Options.CourtNoiseStdev;
                        double courtSignal = MyProgress.LitigationQualityUniform + courtNoiseNormalDraw;
                        MyProgress.PWinsAtTrial =
                            courtSignal >
                            0.5; // we'll assume that P has burden of proof in case courtSignal is exactly equal to 0.5.
                        //System.Diagnostics.Debug.WriteLine($"Quality {MyProgress.LitigationQualityUniform} Court noise action {action} => {courtNoiseNormalDraw} => signal {courtSignal} PWins {MyProgress.PWinsAtTrial}");
                    }
                    else // with processed signals, the probability of P winning is defined in MyGameDefinition; action 2 always means a plaintiff victory
                        MyProgress.PWinsAtTrial = action == 2;
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        public static void ConvertNoiseActionToDiscreteAndUniformSignal(byte action, bool useRawSignals, double trueValue, byte numNoiseValues, double noiseStdev, byte numSignals, out byte discreteSignal, out double uniformSignal)
        {
            if (useRawSignals)
            {
                // This is an equal probabilities decision. 
                discreteSignal = DiscreteValueSignal.GetRawSignal(trueValue, action, numNoiseValues, noiseStdev, numSignals);
                if (discreteSignal == 1)
                    uniformSignal = -1.0; // just a sign indicating that the signal is negative
                else if (discreteSignal == numSignals)
                    uniformSignal = 2.0; // again, just a sign that it's out of range
                else
                    uniformSignal = EquallySpaced.GetLocationOfEquallySpacedPoint(
                        discreteSignal -
                        2 /* make it zero-based, but also account for the fact that we have a signal for values less than 0 */,
                        numSignals - 2);
            }
            else
            {
                // Note: This is an unequal probabilities chance decision. The action IS the discrete signal. The game definition must then calculates the probability that we would get this signal, given the uniform distribution draw. In other words, this is like a weighted die, where the die is heavily weighted toward signal values that are close to the litigation quality values.
                discreteSignal = action;
                uniformSignal =
                    EquallySpaced.GetLocationOfEquallySpacedPoint(
                        discreteSignal - 1 /* make it zero-based */, numSignals);
            }
        }

        private double GetOfferBasedOnAction(byte action, bool plaintiffOffer)
        {
            double offer;
            if (MyProgress.BargainingRoundsComplete == 0 || !MyDefinition.Options.DeltaOffersOptions.SubsequentOffersAreDeltas)
                offer = ConvertActionToUniformDistributionDraw(action);
            else
            {
                double? previousOffer = plaintiffOffer ? MyProgress.PLastOffer : MyProgress.DLastOffer;
                if (previousOffer == null)
                    offer = ConvertActionToUniformDistributionDraw(action);
                else
                    offer = MyDefinition.Options.DeltaOffersCalculation.GetOfferValue((double) previousOffer, action);
            }
            return offer;
        }

        private byte GetDiscreteSignal(int action, double noiseStdev, DiscreteValueSignalParameters dvsp)
        {
            var noise = ConvertActionToNormalDistributionDraw(action, noiseStdev);
            var valuePlusNoise = MyProgress.LitigationQualityUniform + noise;
            byte discreteSignal = (byte)DiscreteValueSignal.GetDiscreteSignal(valuePlusNoise, dvsp); // note that this is a 1-based signal
            return discreteSignal;
        }

        public override void FinalProcessing()
        {
            if (!MyProgress.PFiles || MyProgress.PAbandons)
            {
                MyProgress.PChangeWealth = MyProgress.DChangeWealth = 0;
                MyProgress.TrialOccurs = false;
            }
            else if (!MyProgress.DAnswers || MyProgress.DDefaults)
            { // defendant pays full damages (but no trial costs)
                MyProgress.PChangeWealth += MyDefinition.Options.DamagesAlleged;
                MyProgress.DChangeWealth -= MyDefinition.Options.DamagesAlleged;
                MyProgress.TrialOccurs = false;
            }
            else if (MyProgress.CaseSettles)
            {
                MyProgress.PChangeWealth = (double)MyProgress.SettlementValue;
                MyProgress.DChangeWealth = 0 - (double)MyProgress.SettlementValue;
                MyProgress.TrialOccurs = false;
            }
            else
            {
                MyProgress.TrialOccurs = true;
                MyProgress.PChangeWealth = (MyProgress.PWinsAtTrial ? MyProgress.DamagesAlleged : 0) - MyDefinition.Options.PTrialCosts;
                MyProgress.DChangeWealth = (MyProgress.PWinsAtTrial ? -MyProgress.DamagesAlleged : 0) - MyDefinition.Options.DTrialCosts;
            }
            double perPartyBargainingCosts = MyDefinition.Options.PerPartyBargainingRoundCosts * MyProgress.BargainingRoundsComplete;
            MyProgress.PChangeWealth -= perPartyBargainingCosts;
            MyProgress.DChangeWealth -= perPartyBargainingCosts;
            MyProgress.PFinalWealth = MyProgress.PInitialWealth + MyProgress.PChangeWealth;
            MyProgress.DFinalWealth = MyProgress.DInitialWealth + MyProgress.DChangeWealth;
            MyProgress.PWelfare =
                MyDefinition.Options.PUtilityCalculator.GetSubjectiveUtilityForWealthLevel(MyProgress.PFinalWealth);
            MyProgress.DWelfare =
                MyDefinition.Options.DUtilityCalculator.GetSubjectiveUtilityForWealthLevel(MyProgress.DFinalWealth);
            base.FinalProcessing();
        }
    }
}
