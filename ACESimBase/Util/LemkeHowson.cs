using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Diagnostics;
using ACESim.Util;
using ACESim;

namespace ACESimBase.Util
{
    public class LH_Tableaux
    {
        bool trace = true; // DEBUG

        int NumRowStrategies;
        int NumColStrategies;

        public double[,] RowPlayerTableau;
        public double[,] ColPlayerTableau;
        public double[][,] Tableaux => new double[2][,] { RowPlayerTableau, ColPlayerTableau };
        double[,] RowPlayerUtilities_A;
        double[,] ColPlayerUtilities_B;
        bool[] RowPlayerLabels;
        bool[] ColPlayerLabels;
        bool RowPlayerNext;
        int? LabelToDrop;
        bool FullyLabeled => LabelToDrop == null;

        public LH_Tableaux()
        {

        }

        public LH_Tableaux(double[,] rowPlayerUtilities_A, double[,] colPlayerUtilities_B)
        {
            NumRowStrategies = rowPlayerUtilities_A.GetLength(0);
            NumColStrategies = rowPlayerUtilities_A.GetLength(1);
            if (colPlayerUtilities_B.GetLength(0) != NumColStrategies || colPlayerUtilities_B.GetLength(1) != NumRowStrategies)
                throw new ArgumentException();
            RowPlayerUtilities_A = (double[,]) rowPlayerUtilities_A.Clone();
            RowPlayerUtilities_A.MakePositive();
            ColPlayerUtilities_B = (double[,]) colPlayerUtilities_B.Clone();
            ColPlayerUtilities_B.MakePositive();
            FillTableauxInitially();
        }

        public LH_Tableaux DeepCopy()
        {
            return new LH_Tableaux()
            {
                NumRowStrategies = NumRowStrategies,
                NumColStrategies = NumColStrategies,
                RowPlayerTableau = (double[,])RowPlayerTableau.Clone(),
                ColPlayerTableau = (double[,])ColPlayerTableau.Clone(),
                RowPlayerUtilities_A = (double[,])RowPlayerUtilities_A.Clone(),
                ColPlayerUtilities_B = (double[,])ColPlayerUtilities_B.Clone(),
                RowPlayerLabels = (bool[])RowPlayerLabels.Clone(),
                ColPlayerLabels = (bool[])ColPlayerLabels.Clone(),
                RowPlayerNext = RowPlayerNext,
                LabelToDrop = LabelToDrop
            };
        }

        public LH_Tableaux DeepCopy_SpecifyingStartingPoint(int labelToDrop)
        {
            var result = DeepCopy();
            result.RowPlayerNext = RowPlayerLabels[labelToDrop];
            result.LabelToDrop = labelToDrop;
            return result;
        }

        public IEnumerable<LH_Tableaux> GenerateAllStartingPoints()
        {
            for (int i = 0; i < NumColStrategies; i++)
                yield return DeepCopy_SpecifyingStartingPoint(NumColStrategies + i);
            for (int i = 0; i< NumRowStrategies; i++)
                yield return DeepCopy_SpecifyingStartingPoint(i);
        }

        public void FillTableauxInitially()
        {
            // Row player tableau is Btranspose I 1.
            var bTranspose = ColPlayerUtilities_B.Transpose();
            var identityForColStrategies = Matrix.Identity(NumColStrategies);
            var onesForColStrategies = Matrix.One(NumColStrategies);
            RowPlayerTableau = bTranspose.Append(identityForColStrategies).Append(onesForColStrategies);
            // Col player tableau is I A 1.
            var identityForRowStrategies = Matrix.Identity(NumRowStrategies);
            var onesForRowStrategies = Matrix.One(NumRowStrategies);
            ColPlayerTableau = identityForRowStrategies.Append(RowPlayerUtilities_A).Append(onesForRowStrategies);
            // Create space for labels.
            RowPlayerLabels = new bool[NumRowStrategies + NumColStrategies];
            ColPlayerLabels = new bool[NumRowStrategies + NumColStrategies];
            SetLabels(true, true);
        }

        public double[][] DoLemkeHowsonStartingAtAllPossibilities()
        {
            List<LH_Tableaux> tableauxGroups = GenerateAllStartingPoints().ToList();

            if (trace)
            {
                foreach (var tableaux in tableauxGroups)
                    tableaux.PrintTableaux();
            }
           
            int? completedIndex = null;
            do
            {
                for (int i = 0; i < tableauxGroups.Count(); i++)
                {
                    tableauxGroups[i].LemkeHowsonStep();
                    if (tableauxGroups[i].FullyLabeled)
                    {
                        completedIndex = i;
                        break;
                    }
                }
            }
            while (completedIndex == null);
            return tableauxGroups[(int) completedIndex].CompleteLemkeHowson();
        }

        public double[][] DoLemkeHowsonStartingAtLabel0()
        {
            int labelToDrop = 0;
            return DoLemkeHowsonStartingAtLabel(labelToDrop);
        }

        public double[][] DoLemkeHowsonStartingAtLabel(int labelToDrop)
        {
            if (trace)
                PrintTableaux();
            LabelToDrop = labelToDrop;
            RowPlayerNext = RowPlayerLabels[labelToDrop];
            int iteration = 0;
            do
            {
                if (trace)
                    Debug.WriteLine($"Lemke-Howson iteration {iteration}");
                LemkeHowsonStep();
                iteration++;
            }
            while (!FullyLabeled);
            return CompleteLemkeHowson();
        }

        private void LemkeHowsonStep()
        {
            DoPivotIteration(RowPlayerNext, (int)LabelToDrop);
            PrintTableaux();
            RowPlayerNext = !RowPlayerNext;
        }

        private double[][] CompleteLemkeHowson()
        {
            List<double[,]> linearSystems = Tableaux.Select(x => x.RemoveLabeledColumns()).ToList();
            double[][] unnormalized = linearSystems.Select(x => x.SolveLinearSystem()).ToArray();
            double[][] normalized = unnormalized.Select(x => Normalize(x)).ToArray();
            if (trace)
            {
                Debug.WriteLine("");
                Debug.WriteLine($"COMPLETING LEMKE-HOWSON");
                Debug.WriteLine($"Linear system:");
                foreach (var linearSystem in linearSystems)
                    Debug.WriteLine(linearSystem.ToString(4, 10));
                Debug.WriteLine($"Unnormalized results:");
                Debug.WriteLine(unnormalized.FromNested().ToString(4, 10));
                Debug.WriteLine($"Normalized results:");
                Debug.WriteLine(normalized.FromNested().ToString(4, 10));
                Debug.WriteLine("");
            }
            return normalized;
        }

        private double[] Normalize(double[] values)
        {
            double sum = values.Sum();
            return values.Select(x => x / sum).ToArray();
        }

        private void SetLabels(bool updateRowPlayer, bool updateColPlayer)
        {
            LabelToDrop = null;
            for (int i = 0; i < NumRowStrategies + NumColStrategies; i++)
            {
                if (updateRowPlayer)
                    RowPlayerLabels[i] = !RowPlayerTableau.ColumnIsBasic(i);
                if (updateColPlayer)
                    ColPlayerLabels[i] = !ColPlayerTableau.ColumnIsBasic(i);
                if (RowPlayerLabels[i] && ColPlayerLabels[i])
                    LabelToDrop = i;
            }
        }

        private void DoPivotIteration(bool pivotOnRowPlayerTableau, int pivotColumn)
        {
            (var tableau, var labels) = pivotOnRowPlayerTableau ? (RowPlayerTableau, RowPlayerLabels) : (ColPlayerTableau, ColPlayerLabels); 
            if (!labels[pivotColumn])
                throw new ArgumentException("Cannot pivot on unlabeled column");
            if (trace)
                Debug.WriteLine($"Pivoting {(pivotOnRowPlayerTableau ? "row" : "column")} player tableau on column {pivotColumn}");
            tableau.Pivot(pivotColumn);
            SetLabels(pivotOnRowPlayerTableau, !pivotOnRowPlayerTableau);
        }

        public string ToString(bool rowPlayer)
        {
            var tableau = rowPlayer ? RowPlayerTableau : ColPlayerTableau;
            string columnLabels = tableau.GetColumnLabelsString(10) + "\r\n";
            string main = tableau.ToString(4, 10);
            return columnLabels + main;
        }

        public void PrintTableaux()
        {
            if (trace)
            {
                Debug.WriteLine($"Row player:");
                Debug.WriteLine(ToString(true));
                Debug.WriteLine($"Col player:");
                Debug.WriteLine(ToString(false));
                Debug.WriteLine($"Next {(RowPlayerNext ? "Row" : "Column")} Label to drop {LabelToDrop}");
                Debug.WriteLine($"---------------------------------------------------------------------");
            }
        }
    }
}
