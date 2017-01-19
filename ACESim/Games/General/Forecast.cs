using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    /// <summary>
    /// This is a helper class to simplify programming in games where one or more strategies to be evolved
    /// are simply forecasting some number, where the correct number is known to the game being evolved. 
    /// It also contains support for simultaneously evolving a forecast of a number and an assessment of 
    /// the accuracy of the forecast.
    /// </summary>
    public static class Forecast
    {
        /// <summary>
        /// Makes a decision based on the strategy, with the inputs specified, and returns the 
        /// squared difference between this decision and the correct answer. 
        /// </summary>
        /// <param name="strategy"></param>
        /// <param name="inputs"></param>
        /// <param name="correctAnswer"></param>
        public static double Score(Strategy strategy, List<double> inputs, double correctAnswer)
        {
            double decision = strategy.Calculate(inputs: inputs);
            double difference = decision - correctAnswer;
            return difference * difference;
        }

        /// <summary>
        /// Same as Score(Strategy, List<double/>, double), but also sets the decision parameter to the decision 
        /// returned by the strategy.
        /// </summary>
        /// <param name="strategy"></param>
        /// <param name="inputs"></param>
        /// <param name="correctAnswer"></param>
        /// <param name="decision"></param>
        /// <returns></returns>
        public static double Score(Strategy strategy, List<double> inputs, double correctAnswer, out double decision)
        {
            decision = strategy.Calculate(inputs: inputs);
            double difference = decision - correctAnswer;
            return difference * difference;
        }

        /// <summary>
        /// This is used to score two strategies forecasting some number; one represents the underlying prediction, 
        /// and the other represents the average absolute error. The underlyingPredictionStrategy and 
        /// errorPredictionStrategy are from two different populations – one of which is supposed to be making a 
        /// forecast of the correctAnswer based on the inputs, and the other of which is forecasting the average 
        /// absolute error of the first. If scoreUnderlyingPrediction, this returns 
        /// Score(underlyingPredictionStrategy, inputs, correctAnswer). Otherwise, it calls 
        /// Score(underlyingPredictionStrategy, inputs, correctAnswer, out decision) and then returns 
        /// Score(errorPredictionStrategy, inputs, abs(decision – correctAnswer)).
        /// </summary>
        /// <param name="underlyingPredictionStrategy"></param>
        /// <param name="errorPredictionStrategy"></param>
        /// <param name="scoreUnderlyingPrediction"></param>
        /// <param name="inputs"></param>
        /// <param name="correctAnswer"></param>
        /// <returns></returns>
        public static double Score(Strategy underlyingPredictionStrategy, Strategy errorPredictionStrategy,
            bool scoreUnderlyingPrediction, List<double> inputs, double correctAnswer, out double decision)
        {
            if (scoreUnderlyingPrediction)
            {
                return Score(underlyingPredictionStrategy, inputs, correctAnswer, out decision);
            }
            else
            {
                double previousDecision;
                Score(underlyingPredictionStrategy, inputs, correctAnswer, out previousDecision); // don't care about the score, just trying to get the decision
                return Score(errorPredictionStrategy, inputs, Math.Abs(previousDecision - correctAnswer), out decision);
            }
        }
    }
}
