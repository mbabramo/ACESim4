using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Diagnostics;
using ACESim.Util;
using ACESim;

namespace ACESimBase.Util
{

    public struct VariableInEquation
    {
        public bool IsMain;
        public bool IsSlack => !IsMain;
        public int Index;

        public VariableInEquation(bool isMain, int index)
        {
            IsMain = isMain;
            Index = index;
        }

        public override bool Equals(object obj) =>
            obj is VariableInEquation mys
            && mys.IsMain == this.IsMain
            && mys.Index == this.Index;

        public override int GetHashCode()
        {
            return (IsMain, Index).GetHashCode();
        }

        public string ToString(double coefficient, char mainVarName)
        {
            StringBuilder s = new StringBuilder();
            string coefficientString = "";
            if (coefficient != 1)
                coefficientString = coefficient.ToSignificantFigures(4);
            for (int i = coefficientString.Length; i < 7; i++)
                s.Append(" ");
            s.Append(coefficientString);
            if (IsMain)
                s.Append(mainVarName);
            else
                s.Append('s');
            s.Append(Index.ToString());
            return s.ToString();
        }

        public static bool operator ==(VariableInEquation left, VariableInEquation right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(VariableInEquation left, VariableInEquation right)
        {
            return !(left == right);
        }
    }

    public class EquationInSet
    {
        public VariableInEquation LHS;
        public double RHS_Constant;
        public (double coefficient, VariableInEquation variable)[] RHS_Vars;
        public char MainVarName;
        public int MinSlackIndex;
        public int MinMainIndex;
        public int NumMainVariables;
        public int NumSlackVariables;
        public int GetRHSIndex(VariableInEquation variable) => GetRHSIndex(variable.IsMain, variable.Index);
        public int GetRHSIndex(bool main, int index) => main ? index - MinMainIndex : NumMainVariables + index - MinSlackIndex;
        public double GetCoefficient(bool main, int index) => RHS_Vars[GetRHSIndex(main, index)].coefficient;
        public void SetCoefficient(bool main, int index, double coefficient)
        {
            RHS_Vars[GetRHSIndex(main, index)].coefficient = coefficient;
        }

        public EquationInSet(int indexAmongSlackVariables, int minSlackIndex, int minMainIndex, int numSlackVariables, double rhsConstant, double[] rhsCoefficients, char mainVarName)
        {
            MainVarName = mainVarName;
            MinSlackIndex = minSlackIndex;
            MinMainIndex = minMainIndex;
            NumMainVariables = rhsCoefficients.Length;
            NumSlackVariables = numSlackVariables;
            LHS = new VariableInEquation(false, indexAmongSlackVariables + minSlackIndex);
            RHS_Constant = rhsConstant;
            int totalRHSVariables = NumMainVariables + numSlackVariables;
            RHS_Vars = new (double coefficient, VariableInEquation variable)[totalRHSVariables];
            for (int i = 0; i < NumMainVariables; i++)
                RHS_Vars[i] = (rhsCoefficients[i], new VariableInEquation(true, minMainIndex + i));
            for (int i = 0; i < numSlackVariables; i++)
                RHS_Vars[i + NumMainVariables] = (0, new VariableInEquation(false, minSlackIndex + i));
        }

        public override string ToString()
        {
            StringBuilder s = new StringBuilder();
            s.Append(LHS.ToString(1.0, MainVarName));
            s.Append(" = ");
            s.Append(RHS_Constant);
            for (int i = 0; i < RHS_Vars.Length; i++)
                s.Append($" + {RHS_Vars[i].variable.ToString(RHS_Vars[i].coefficient, MainVarName)}");
            return s.ToString();
        }

        public void MoveVariableToLHS(VariableInEquation variableToMove)
        {
            int indexInRHSOfVariableToMove = GetRHSIndex(variableToMove);
               (double coefficient, VariableInEquation variableToMoveFromRHSToLHS) = RHS_Vars[indexInRHSOfVariableToMove];
            VariableInEquation lhsVar = LHS;
            int rhsIndexToMoveTo = GetRHSIndex(lhsVar);
            for (int i = 0; i < RHS_Vars.Length; i++)
            {
                if (i == rhsIndexToMoveTo)
                {
                    RHS_Vars[i].coefficient = 1.0 / coefficient;
                }
                else
                {
                    RHS_Vars[i].coefficient *= -1.0 / coefficient;
                }
            }
            LHS = variableToMoveFromRHSToLHS;
        }

        public void ApplySubstitution(EquationInSet otherEquation)
        {
            int rhsIndex = GetRHSIndex(otherEquation.LHS.IsMain, otherEquation.LHS.Index);
            double coefficientOnVariableBeingSubstitutedFor = RHS_Vars[rhsIndex].coefficient;
            for (int i = 0; i < RHS_Vars.Length; i++)
            {
                if (i == rhsIndex)
                    RHS_Vars[i].coefficient = 0;
                else
                    RHS_Vars[i].coefficient += coefficientOnVariableBeingSubstitutedFor * otherEquation.RHS_Vars[i].coefficient;
            }
        }
    }

    public class EquationSet
    {
        public int NumMainVars;
        public int NumSlackVars;
        public int MinMainVarIndex;
        public int MinSlackVarIndex;
        public EquationInSet[] Equations;
        public VariableInEquation SlackVar(int index) => new VariableInEquation(false, index);
        public VariableInEquation MainVar(int index) => new VariableInEquation(true, index);
        
        public EquationSet(double[,] matrix, char mainVarName, int minSlackVarIndex, int minMainVarIndex)
        {
            NumSlackVars = matrix.GetLength(0);
            NumMainVars = matrix.GetLength(1);
            MinSlackVarIndex = minSlackVarIndex;
            MinMainVarIndex = minMainVarIndex;
            Equations = new EquationInSet[NumSlackVars];
            for (int matrixRowIndex = 0; matrixRowIndex < NumSlackVars; matrixRowIndex++)
            {
                Equations[matrixRowIndex] = new EquationInSet(matrixRowIndex, minSlackVarIndex, minMainVarIndex, NumSlackVars, 1.0, Enumerable.Range(0, NumMainVars).Select(matrixColumnIndex => (0 - matrix[matrixRowIndex, matrixColumnIndex])).ToArray(), mainVarName);
            }
        }

        public void ChangeOfBasis(VariableInEquation variableEnteringBasis, out VariableInEquation variableLeavingBasis)
        {
            int equationToModifyInitiallyIndex = 0;
            double lowestValue = 0;
            for (int e = 0; e < Equations.Length; e++)
            {
                double coefficient = Equations[e].GetCoefficient(variableEnteringBasis.IsMain, variableEnteringBasis.Index);
                if (e == 0 || coefficient < lowestValue)
                {
                    equationToModifyInitiallyIndex = e;
                    lowestValue = coefficient;
                }
            }
            EquationInSet equationToModifyInitially = Equations[equationToModifyInitiallyIndex];
            variableLeavingBasis = equationToModifyInitially.LHS;
            equationToModifyInitially.MoveVariableToLHS(variableEnteringBasis);
            for (int e = 0; e < Equations.Length; e++)
                if (e != equationToModifyInitiallyIndex)
                    Equations[e].ApplySubstitution(equationToModifyInitially);
        }

        public override string ToString()
        {
            StringBuilder s = new StringBuilder();
            for (int i = 0; i < Equations.Length; i++)
                s.AppendLine(Equations[i].ToString());
            return s.ToString();
        }
    }

    

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

        public EquationSet[] EquationTableaux;

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

            EquationTableaux = new EquationSet[2];
            EquationTableaux[0] = new EquationSet(bTranspose, 'x', NumRowStrategies, 0);
            EquationTableaux[1] = new EquationSet(RowPlayerUtilities_A, 'y', 0, NumRowStrategies + 0);
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
                Debug.WriteLine(EquationTableaux[0].ToString());
                Debug.WriteLine(EquationTableaux[1].ToString());
                //DEBUG
                //Debug.WriteLine($"Row player:");
                //Debug.WriteLine(ToString(true));
                //Debug.WriteLine($"Col player:");
                //Debug.WriteLine(ToString(false));
                //Debug.WriteLine($"Next {(RowPlayerNext ? "Row" : "Column")} Label to drop {LabelToDrop}");
                Debug.WriteLine($"---------------------------------------------------------------------");
            }
        }
    }
}
