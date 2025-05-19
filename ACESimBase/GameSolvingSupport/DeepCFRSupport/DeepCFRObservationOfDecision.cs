using ACESim;
using System;
using System.Collections.Generic;

namespace ACESimBase.GameSolvingSupport.DeepCFRSupport
{
    public struct DeepCFRObservationOfDecision
    {
        public Decision decision;
        public byte decisionIndex;
        public DeepCFRObservation observation;

        public DeepCFRObservationOfDecision(Decision decision, byte decisionIndex, DeepCFRObservation observation)
        {
            this.decision = decision;
            this.decisionIndex = decisionIndex;
            this.observation = observation;
        }

        public override bool Equals(object obj)
        {
            return obj is DeepCFRObservationOfDecision other &&
                   EqualityComparer<Decision>.Default.Equals(decision, other.decision) &&
                   decisionIndex == other.decisionIndex &&
                   EqualityComparer<DeepCFRObservation>.Default.Equals(observation, other.observation);
        }

        public static bool operator ==(DeepCFRObservationOfDecision lhs, DeepCFRObservationOfDecision rhs)
        {
            return lhs.Equals(rhs);
        }

        public static bool operator !=(DeepCFRObservationOfDecision lhs, DeepCFRObservationOfDecision rhs)
        {
            return !(lhs == rhs);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(decision, decisionIndex, observation);
        }

        public void Deconstruct(out Decision decision, out byte decisionIndex, out DeepCFRObservation observation)
        {
            decision = this.decision;
            decisionIndex = this.decisionIndex;
            observation = this.observation;
        }

        public static implicit operator (Decision decision, byte decisionIndex, DeepCFRObservation observation)(DeepCFRObservationOfDecision value)
        {
            return (value.decision, value.decisionIndex, value.observation);
        }

        public static implicit operator DeepCFRObservationOfDecision((Decision decision, byte decisionIndex, DeepCFRObservation observation) value)
        {
            return new DeepCFRObservationOfDecision(value.decision, value.decisionIndex, value.observation);
        }
    }
}