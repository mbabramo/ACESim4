using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading;

namespace ACESim
{
    public class ProgressStep
    {
        public Action<double> ReportPercentComplete;
        public ProgressStep Parent;
        public List<ProgressStep> Children;
        public double StepSize;
        public double ProportionComplete;
        public bool IsComplete;
        public string StepType;
        public int LevelNumber;
        bool StoppedProcess;
        public bool DisableProgressTracking = false;

        bool TraceProgressStep = false; 

        public void Trace()
        {
            if (TraceProgressStep)
            {
                Debug.WriteLine("<-----------------------------");
                Debug.WriteLine(Environment.StackTrace);
                Debug.WriteLine(ToString());
                Debug.WriteLine("----------------------------->");
            }
        }

        public void ReportStoppedProcess()
        {
            ProgressStep top = Parent;
            while (top != null)
                top = top.Parent;
            ReportStoppedProcessHelper();
        }

        private void ReportStoppedProcessHelper()
        {
            StoppedProcess = true;
            if (Children != null && Children.Any())
                Children.ForEach(x => x.ReportStoppedProcessHelper());
        }

        public void AddChildSteps(int number, string stepType)
        {
            if (!StoppedProcess)
            {
                double[] substepWeights = new double[number];
                for (int i = 0; i < number; i++)
                    substepWeights[i] = 1;
                AddChildSteps(substepWeights, stepType);
            }
        }

        public void AddChildSteps(double[] weights, string stepType)
        {
            if (DisableProgressTracking)
                return;
            if (!StoppedProcess)
            {
                double[] adjustedWeights = new double[weights.Length];
                if (Children == null)
                    Children = new List<ProgressStep>();
                else
                    throw new Exception("Internal error: Attempt with ProgressStep " + StepType + " to add children of type " + stepType + " when children were already defined. One possibility is that you did not indicate that a previous instance of type " + StepType + " was complete. Another possibility is that you failed to add some step that should be layered in between " + StepType + " and " + stepType + ".");
                for (int i = 0; i < weights.Length; i++)
                {
                    adjustedWeights[i] = weights[i] / weights.Sum();
                    Children.Add(new ProgressStep { Parent = this, StepSize = adjustedWeights[i], ProportionComplete = 0, StepType = stepType, LevelNumber = this.LevelNumber + 1 });
                }
                Trace();
            }
        }

        public ProgressStep GetCurrentStep()
        {
            if (Children == null || Children.All(x => x.IsComplete))
                return this;
            return Children.First(x => !x.IsComplete).GetCurrentStep();
        }

        public void SetSeveralStepsComplete(int numSteps, string stepType)
        {
            if (numSteps > 0)
            { 
                SetProportionOfStepComplete(1, true, stepType);
                Parent.GetCurrentStep().SetSeveralStepsComplete(numSteps - 1, stepType);
            }
        }

        public void SetProportionOfStepComplete(double proportion, bool markAsComplete, string stepType)
        {
            if (DisableProgressTracking)
                return;
            if (!StoppedProcess)
            {
                if (StepType != stepType)
                {
                    if (Children != null && Children.Any() && Children.First().StepType == stepType)
                        throw new Exception("Internal error: All child steps of type " + stepType + " were already complete, and ProgressStep was expecting an update on " + StepType + ". Perhaps you specified too few child steps? (Alternatively, you may have failed to get the latest version of the progress step before calling this function, and thus called Complete on the parent step instead of on the child step.");
                    if (Parent != null && Parent.StepType == stepType)
                        throw new Exception("Internal error: You tried to mark complete " + stepType + " when you had not marked complete a child " + StepType + ". Perhaps you specified too many child steps?");
                    throw new Exception("Internal error: Incorrect step type.");
                }
                SetProportionOfStepCompleteHelper(proportion, markAsComplete);
                //Debug.WriteLine("Step complete: " + stepType);
                //Debug.WriteLine(this);
                if (markAsComplete)
                    Trace();
            }
        }

        internal void SetProportionOfStepCompleteHelper(double proportion, bool markAsComplete)
        {
            ProportionComplete = proportion;
            if (markAsComplete)
            {
                ProportionComplete = 1;
                IsComplete = true;
            }
            if (Parent != null)
                Parent.SetProportionOfStepCompleteHelper(Parent.Children.Sum(x => x.StepSize * x.ProportionComplete), false); // will recursively propogate upwards
            else if (ReportPercentComplete != null)
                ReportPercentComplete(ProportionComplete);
        }

        public ProgressStep GetTopLevelStep()
        {
            if (Parent == null)
                return this;
            else
                return Parent.GetTopLevelStep();
        }

        long NumIterationsOfSubstepComplete = 0;

        public void PrepareToAutomaticallyUpdateProgress()
        {
            NumIterationsOfSubstepComplete = 0;
        }

        public void PerformActionAndAutomaticallyUpdatePartialProgress(string name, long iteration, long totalNumberIterations, long  reportEveryNIterations, Action<long> action)
        {
            action(iteration);
            Interlocked.Increment(ref NumIterationsOfSubstepComplete);
            if (NumIterationsOfSubstepComplete % reportEveryNIterations == 0)
                SetProportionOfStepComplete(((double)NumIterationsOfSubstepComplete) / (double)totalNumberIterations, false, name);
        }


        public bool PerformActionAndAutomaticallyUpdatePartialProgress(string name, long successNumber, long iteration, long totalNumberIterations, long reportEveryNIterations, Func<long, long, bool> actionReturningSuccessIfCompleted)
        {
            bool success = actionReturningSuccessIfCompleted(successNumber, iteration);
            if (success)
            {
                Interlocked.Increment(ref NumIterationsOfSubstepComplete);
                if (NumIterationsOfSubstepComplete % reportEveryNIterations == 0)
                    SetProportionOfStepComplete(((double)NumIterationsOfSubstepComplete) / (double)totalNumberIterations, false, name);
            }
            return success;
        }

        bool isLevelForWhichStringIsRequested;
        public override string ToString()
        {
            isLevelForWhichStringIsRequested = true;
            string returnString = GetTopLevelStep().ToStringHelper();
            isLevelForWhichStringIsRequested = false;
            return returnString;
        }

        public string ToStringHelper()
        {
            string spaces = "";
            for (int i = 0; i < LevelNumber; i++)
                spaces += "     ";
            return String.Format("{0}: {1} {2}% Complete:{3} {4} \n {5}", spaces + LevelNumber, StepType, ProportionComplete * 100, IsComplete, isLevelForWhichStringIsRequested ? "*" : " ", Children == null ? "" : String.Join("\n", Children.Select(x => x.ToStringHelper()).ToArray()));
        }
    }

}
