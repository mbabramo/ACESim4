using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Diagnostics;
using ACESim.Util;
using ACESim;
using JetBrains.Annotations;
using ACESimBase.Util.Mathematics;

namespace ACESimBase.Util.Statistical
{

    public class LemkeHowson
    {
        bool Trace = false;

        int NumRowStrategies;
        int NumColStrategies;

        public double[,] RowPlayerTableau;
        public double[,] ColPlayerTableau;
        public double[][,] Tableaux => new double[2][,] { RowPlayerTableau, ColPlayerTableau };
        double[,] RowPlayerUtilities_A;
        double[,] ColPlayerUtilities_B;
        bool RowPlayerNext;
        int InitialLabelToDrop;
        int NextLabel;
        public LemkeHowson()
        {

        }

        public LemkeHowson(double[,] rowPlayerUtilities_A, double[,] colPlayerUtilities_B)
        {
            NumRowStrategies = rowPlayerUtilities_A.GetLength(0);
            NumColStrategies = rowPlayerUtilities_A.GetLength(1);
            RowPlayerUtilities_A = (double[,])rowPlayerUtilities_A.Clone();
            RowPlayerUtilities_A.MakePositive(false); // ignore zeros is false because the 0's (in contrast to Lemke's algorithm) do not correspond to impossible combinations of sequences
            ColPlayerUtilities_B = (double[,])colPlayerUtilities_B.Clone();
            ColPlayerUtilities_B.MakePositive(false);
            FillTableauxInitially();
        }

        public LemkeHowson DeepCopy()
        {
            return new LemkeHowson()
            {
                NumRowStrategies = NumRowStrategies,
                NumColStrategies = NumColStrategies,
                RowPlayerTableau = (double[,])RowPlayerTableau.Clone(),
                ColPlayerTableau = (double[,])ColPlayerTableau.Clone(),
                RowPlayerUtilities_A = (double[,])RowPlayerUtilities_A.Clone(),
                ColPlayerUtilities_B = (double[,])ColPlayerUtilities_B.Clone(),
                RowPlayerNext = RowPlayerNext,
                InitialLabelToDrop = InitialLabelToDrop,
                NextLabel = NextLabel,
            };
        }

        public LemkeHowson DeepCopy_SpecifyingStartingPoint(int labelToDrop)
        {
            var result = DeepCopy();
            result.InitialLabelToDrop = labelToDrop;
            result.NextLabel = labelToDrop;
            result.RowPlayerNext = labelToDrop < NumRowStrategies;
            return result;
        }

        private IEnumerable<LemkeHowson> GenerateAllStartingPoints(int firstPossibilityToCheck, int maxPossibilitiesToCheck)
        {
            int lastPossibilityToCheck = Math.Min(NumRowStrategies + NumColStrategies, firstPossibilityToCheck + maxPossibilitiesToCheck);
            for (int i = firstPossibilityToCheck; i < lastPossibilityToCheck; i++)
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
        }

        /// <summary>
        /// Try LemkeHowson for different labels to drop, one step at a time for each label. 
        /// </summary>
        /// <returns>The first result obtained, or null if all labels fail to return a valid strategy.</returns>
        public double[][] DoLemkeHowsonStartingAtAllPossibilities(int maxPossibilitiesToCheckEachTime, int maxPossibilitiesToCheckAltogether, bool errorIfFailure = false)
        {
            int firstPossibilityToCheck = 0;
            double[][] result = null;
            while (firstPossibilityToCheck < maxPossibilitiesToCheckAltogether)
            {
                List<LemkeHowson> tableauxGroups = GenerateAllStartingPoints(firstPossibilityToCheck, maxPossibilitiesToCheckEachTime).ToList();

                if (Trace)
                {
                    foreach (var tableaux in tableauxGroups)
                        tableaux.PrintTableaux();
                }

                int? completedIndex = null;
                bool[] groupDone = new bool[tableauxGroups.Count];
                bool doneOverall = false;
                do
                {
                    for (int i = 0; i < tableauxGroups.Count(); i++)
                    {
                        if (!groupDone[i])
                        {
                            groupDone[i] = tableauxGroups[i].LemkeHowsonStep();
                            if (groupDone[i])
                            {
                                result = tableauxGroups[i].CompleteLemkeHowson();
                                doneOverall = result != null;
                                if (!doneOverall && errorIfFailure)
                                    throw new Exception($"Failed to find equilibrium for possibility {firstPossibilityToCheck + i}.");
                            }
                            if (doneOverall)
                            {
                                completedIndex = i;
                                break;
                            }
                        }
                    }
                }
                while (completedIndex == null && !groupDone.All(x => x == true));
                if (result != null)
                    break;
                firstPossibilityToCheck += maxPossibilitiesToCheckEachTime;
            };
            return result;
        }

        public double[][] DoLemkeHowsonStartingAtLabel0()
        {
            int labelToDrop = 0;
            return DoLemkeHowsonStartingAtLabel(labelToDrop);
        }

        public double[][] DoLemkeHowsonStartingAtLabel(int initialLabelToDrop)
        {
            InitialLabelToDrop = initialLabelToDrop;
            NextLabel = initialLabelToDrop;
            RowPlayerNext = initialLabelToDrop < NumRowStrategies;
            if (Trace)
            {
                Debug.WriteLine($"Initial tableaux");
                PrintTableaux();
            }
            int iteration = 0;
            bool done;
            do
            {
                if (Trace)
                    Debug.WriteLine($"Lemke-Howson iteration {iteration}");
                done = LemkeHowsonStep();
                iteration++;
            }
            while (!done);
            return CompleteLemkeHowson();
        }

        private bool LemkeHowsonStep()
        {
            bool stopPrematurely = DoPivotIteration(RowPlayerNext);
            if (stopPrematurely)
                return true;
            PrintTableaux();
            RowPlayerNext = !RowPlayerNext;
            return NextLabel == InitialLabelToDrop;
        }

        private double[] TableauToStrategy(double[,] tableau, List<int> basicLabels, List<int> strategyLabels)
        {
            List<double> unnormalized = new List<double>();
            foreach (var columnIndex in strategyLabels)
            {
                if (basicLabels.Contains(columnIndex))
                {
                    var column = tableau.GetColumn(columnIndex);
                    for (int i = 0; i < column.Length; i++)
                    {
                        double value = column[i];
                        if (value != 0)
                            unnormalized.Add(tableau[i, tableau.GetLength(1) - 1] / value);
                    }
                }
                else
                    unnormalized.Add(0);
            }
            if (unnormalized.Any(x => x < 0 || double.IsNaN(x)))
            {
                return null;
            }
            var normalized = Normalize(unnormalized.ToArray());
            return normalized;
        }

        private double[][] CompleteLemkeHowson()
        {
            if (NextLabel != InitialLabelToDrop)
                return null; // problem occurred
            var rowStrategy = TableauToStrategy(RowPlayerTableau, NonBasicVariables(ColPlayerTableau), Enumerable.Range(0, NumRowStrategies).ToList());
            var colStrategy = TableauToStrategy(ColPlayerTableau, NonBasicVariables(RowPlayerTableau), Enumerable.Range(NumRowStrategies, NumColStrategies).ToList());
            if (rowStrategy == null || colStrategy == null)
                return null; // degenerate matrix or other problem

            return new double[2][] { rowStrategy, colStrategy };
        }

        private double[] Normalize(double[] values)
        {
            double sum = values.Sum();
            return values.Select(x => x / sum).ToArray();
        }

        List<int> NonBasicVariables(double[,] tableau)
        {
            List<int> labels = new List<int>();
            int columns = tableau.GetLength(1) - 1; // ignore the last column
            for (int c = 0; c < columns; c++)
                if (!tableau.ColumnIsBasic(c))
                    labels.Add(c);
            return labels;
        }
        private bool DoPivotIteration(bool pivotOnRowPlayerTableau)
        {
            int pivotColumn = NextLabel;
            var tableau = pivotOnRowPlayerTableau ? RowPlayerTableau : ColPlayerTableau;
            var originalLabels = NonBasicVariables(tableau);
            if (Trace)
            {
                Debug.WriteLine($"Pivoting {(pivotOnRowPlayerTableau ? "row" : "column")} player tableau on column {pivotColumn}");
                Debug.WriteLine("Original tableau");
                Debug.WriteLine(tableau.ToString(4, 10));
                Debug.WriteLine($"Original labels: {string.Join(',', originalLabels)}");
            }
            tableau.Pivot(pivotColumn);
            if (Trace)
            {
                Debug.WriteLine("Revised tableau");
                Debug.WriteLine(tableau.ToString(4, 10));
            }
            var revisedLabels = NonBasicVariables(tableau);
            var difference = revisedLabels.Where(x => !originalLabels.Contains(x)).ToList();
            if (!difference.Any())
                return true;
            NextLabel = difference.First();
            if (Trace)
            {
                Debug.WriteLine($"Revised labels: {string.Join(',', revisedLabels)}");
                Debug.WriteLine($"Next label: {NextLabel}");
                Debug.WriteLine("");
            }
            return false;
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
            if (Trace)
            {
                Debug.WriteLine($"Row player:");
                Debug.WriteLine(ToString(true));
                Debug.WriteLine($"Col player:");
                Debug.WriteLine(ToString(false));
                Debug.WriteLine($"Next label to drop {NextLabel}");
                Debug.WriteLine($"---------------------------------------------------------------------");
            }
        }
    }
}
