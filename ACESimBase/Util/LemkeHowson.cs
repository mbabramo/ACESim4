using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Diagnostics;
using ACESim.Util;
using ACESim;

namespace ACESimBase.Util
{

    public class LH_Tableaux_NoEQ
    {
        bool trace = false; // DEBUG

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
        public LH_Tableaux_NoEQ()
        {

        }

        public LH_Tableaux_NoEQ(double[,] rowPlayerUtilities_A, double[,] colPlayerUtilities_B)
        {
            NumRowStrategies = rowPlayerUtilities_A.GetLength(0);
            NumColStrategies = rowPlayerUtilities_A.GetLength(1);
            RowPlayerUtilities_A = (double[,]) rowPlayerUtilities_A.Clone();
            RowPlayerUtilities_A.MakePositive();
            ColPlayerUtilities_B = (double[,]) colPlayerUtilities_B.Clone();
            ColPlayerUtilities_B.MakePositive();
            FillTableauxInitially();
        }

        public LH_Tableaux_NoEQ DeepCopy()
        {
            return new LH_Tableaux_NoEQ()
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

        public LH_Tableaux_NoEQ DeepCopy_SpecifyingStartingPoint(int labelToDrop)
        {
            var result = DeepCopy();
            result.InitialLabelToDrop = labelToDrop;
            result.NextLabel = labelToDrop;
            result.RowPlayerNext = labelToDrop < NumRowStrategies;
            return result;
        }

        public IEnumerable<LH_Tableaux_NoEQ> GenerateAllStartingPoints()
        {
            for (int i = 0; i < NumRowStrategies + NumColStrategies; i++)
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

        public double[][] DoLemkeHowsonStartingAtAllPossibilities()
        {
            List<LH_Tableaux_NoEQ> tableauxGroups = GenerateAllStartingPoints().ToList();

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
                    bool done = tableauxGroups[i].LemkeHowsonStep();
                    if (done)
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

        public double[][] DoLemkeHowsonStartingAtLabel(int initialLabelToDrop)
        {
            InitialLabelToDrop = initialLabelToDrop;
            NextLabel = initialLabelToDrop;
            RowPlayerNext = initialLabelToDrop < NumRowStrategies;
            if (trace)
            {
                Debug.WriteLine($"Initial tableaux");
                PrintTableaux();
            }
            int iteration = 0;
            bool done;
            do
            {
                if (trace)
                    Debug.WriteLine($"Lemke-Howson iteration {iteration}");
                done = LemkeHowsonStep();
                iteration++;
            }
            while (!done);
            return CompleteLemkeHowson();
        }

        private bool LemkeHowsonStep()
        {
            DoPivotIteration(RowPlayerNext);
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
                throw new Exception("Invalid results");
            }
            var normalized = Normalize(unnormalized.ToArray());
            return normalized;
        }

        private double[][] CompleteLemkeHowson()
        {
            var rowStrategy = TableauToStrategy(RowPlayerTableau, NonBasicVariables(ColPlayerTableau), Enumerable.Range(0, NumRowStrategies).ToList());
            var colStrategy = TableauToStrategy(ColPlayerTableau, NonBasicVariables(RowPlayerTableau), Enumerable.Range(NumRowStrategies, NumColStrategies).ToList());
            
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
        private void DoPivotIteration(bool pivotOnRowPlayerTableau)
        {
            // DEBUG
            int pivotColumn = NextLabel;
            var tableau = pivotOnRowPlayerTableau ? RowPlayerTableau : ColPlayerTableau;
            var originalLabels = NonBasicVariables(tableau);
            if (trace)
            {
                Debug.WriteLine($"Pivoting {(pivotOnRowPlayerTableau ? "row" : "column")} player tableau on column {pivotColumn}");
                Debug.WriteLine("Original tableau");
                Debug.WriteLine(tableau.ToString(4, 10));
                Debug.WriteLine($"Original labels: {String.Join(',', originalLabels)}");
            }
            tableau.Pivot(pivotColumn);
            if (trace)
            {
                Debug.WriteLine("Revised tableau");
                Debug.WriteLine(tableau.ToString(4, 10));
            }
            var revisedLabels = NonBasicVariables(tableau);
            var difference = revisedLabels.Where(x => !originalLabels.Contains(x)).ToList();
            NextLabel = difference.First(); 
            if (trace)
            {
                Debug.WriteLine($"Revised labels: {String.Join(',', revisedLabels)}");
                Debug.WriteLine($"Next label: {NextLabel}");
                Debug.WriteLine("");
            }
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
                //DEBUG
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
