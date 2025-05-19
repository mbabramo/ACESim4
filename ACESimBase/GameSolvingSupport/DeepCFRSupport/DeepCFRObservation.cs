using System;
using System.Text;

namespace ACESimBase.GameSolvingSupport.DeepCFRSupport
{
    public class DeepCFRObservation
    {
        /// <summary>
        /// The dependent variable in the regression. This is calculated at an information set by performing a probe starting with an action (not necessarily chosen on policy) with a separate probe starting with an action chosen on policy.
        /// </summary>
        public double SampledRegret;
        /// <summary>
        /// The independent variables in the regression.
        /// </summary>
        public DeepCFRIndependentVariables IndependentVariables;
        /// <summary>
        /// The weight of this observation in the regression.
        /// </summary>
        public double Weight;

        public DeepCFRObservation DeepCopy() => new DeepCFRObservation() { SampledRegret = SampledRegret, IndependentVariables = IndependentVariables.DeepCopy(), Weight = Weight };
    }
}
