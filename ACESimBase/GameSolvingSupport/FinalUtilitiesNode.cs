﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class FinalUtilitiesNode : IGameState
    {
        public int CurrentInitializedScenarioIndex = 0;
        public double WeightOnOpponentsUtility = 0;
        public double[] Utilities => GetUtilities_ConsideringScenarioAndWeight();
        private double[] GetUtilities_ConsideringScenarioAndWeight()
        {
            if (WeightOnOpponentsUtility == 0)
                return AllScenarioUtilities[CurrentInitializedScenarioIndex];
            double[] weightedUtilities = AllScenarioUtilities[CurrentInitializedScenarioIndex].ToArray();
            if (weightedUtilities.Length != 2)
                throw new Exception("Weighted utilities only supported for two-player games.");
            weightedUtilities[0] = Utilities[0] + WeightOnOpponentsUtility * Utilities[1];
            weightedUtilities[1] = Utilities[1] + WeightOnOpponentsUtility * Utilities[0];
            return weightedUtilities;
        }
        public List<double[]> AllScenarioUtilities;
        public FloatSet CustomResult => AllScenarioCustomResult[CurrentInitializedScenarioIndex];
        public List<FloatSet> AllScenarioCustomResult;
        public int FinalUtilitiesNodeNumber;
        public int GetNodeNumber() => FinalUtilitiesNodeNumber;

        public FinalUtilitiesNode(List<double[]> allScenarioUtilities, List<FloatSet> customResults, int finalUtilitiesNodeNumber)
        {
            AllScenarioUtilities = allScenarioUtilities;
            AllScenarioCustomResult = customResults;
            FinalUtilitiesNodeNumber = finalUtilitiesNodeNumber;
        }

        public FinalUtilitiesNode(double[] utilities, FloatSet customResult, int finalUtilitiesNodeNumber)
        {
            AllScenarioUtilities = new List<double[]>() { utilities };
            AllScenarioCustomResult = new List<FloatSet>() { customResult };
            FinalUtilitiesNodeNumber = finalUtilitiesNodeNumber;
        }

        public override string ToString()
        {
            return $"Utilities {FinalUtilitiesNodeNumber}: {String.Join("; ", Utilities.Select(x => $"{x.ToSignificantFigures(6)}"))}";
        }

        public GameStateTypeEnum GetGameStateType()
        {
            return GameStateTypeEnum.FinalUtilities;
        }
    }
}
