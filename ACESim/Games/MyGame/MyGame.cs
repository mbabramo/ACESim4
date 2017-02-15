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
            
            Progress.GameComplete = true;
        }
        
    }
}
