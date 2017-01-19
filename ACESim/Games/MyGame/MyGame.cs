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

        public override void PlaySetup(
            List<Strategy> strategies,
            GameProgress progress,
            GameInputs gameInputs,
            StatCollectorArray recordedInputs,
            GameDefinition gameDefinition,
            bool recordReportInfo,
            double weightOfObservation)
        {
            base.PlaySetup(strategies, progress, gameInputs, recordedInputs, gameDefinition, recordReportInfo, weightOfObservation);
        }

        /// <summary>
        /// If game play is completed, then gameSettings.gameComplete should be set to true. 
        /// </summary>
        public override void PrepareForOrMakeCurrentDecision()
        {
            if (Progress.GameComplete)
                return;

            Score(CurrentDecisionIndex.Value, 0.0);
            Progress.GameComplete = true;
        }


        protected override List<double> GetDecisionInputs()
        {
            double[] inputs = null; // set these in a real game
            int decisionNumber = (int)CurrentDecisionIndex;
            switch (decisionNumber)
            {
                case 0:
                break;

                default: throw new Exception();
            }
            // be sure to keep in following code so that we can keep statistics on 
            if (CurrentDecisionIndex == RecordInputsForDecisionNumber && !PreparationPhase)
                RecordedInputs.Add(inputs);
            return null; // change this
        }
    }
}
