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
        int NumRowStrategies;
        int NumColStrategies;

        int NumRowsInTableau_RowPlayer => NumColStrategies;
        int NumRowsInTableau_ColPlayer => NumRowStrategies;
        int NumColsInTableau => NumColStrategies + NumRowStrategies + 1;

        public double[,] RowPlayerTableau;
        public double[,] ColPlayerTableau; 
        double[,] RowPlayerUtilities_A;
        double[,] ColPlayerUtilities_B;

        public LH_Tableaux(double[,] rowPlayerUtilities_A, double[,] colPlayerUtilities_B)
        {
            NumRowStrategies = rowPlayerUtilities_A.GetLength(0);
            NumColStrategies = rowPlayerUtilities_A.GetLength(1);
            if (colPlayerUtilities_B.GetLength(0) != NumColStrategies || colPlayerUtilities_B.GetLength(1) != NumRowStrategies)
                throw new ArgumentException();
            RowPlayerUtilities_A = rowPlayerUtilities_A;
            ColPlayerUtilities_B = colPlayerUtilities_B;
            FillTableauxInitially();
        }

        public void FillTableauxInitially()
        {
            // Row player tableau is Btranspose I 1.
            RowPlayerTableau = new double[NumRowsInTableau_RowPlayer, NumColsInTableau];
            for (int rowInTableau = 0; rowInTableau < NumRowsInTableau_RowPlayer; rowInTableau++)
            {
                for (int colInTableau = 0; colInTableau < NumColsInTableau; colInTableau++)
                {
                    if (colInTableau < NumRowStrategies)
                    {
                        RowPlayerTableau[rowInTableau, colInTableau] = ColPlayerUtilities_B[colInTableau, rowInTableau];
                    }
                    else if (colInTableau < NumColStrategies + NumRowStrategies)
                    {
                        int colInIdentityMatrix = colInTableau - NumRowStrategies;
                        double valueFromIdentityMatrix;
                        if (rowInTableau == colInIdentityMatrix)
                            valueFromIdentityMatrix = 1.0;
                        else
                            valueFromIdentityMatrix = 0; 
                        RowPlayerTableau[rowInTableau, colInTableau] = valueFromIdentityMatrix;
                    }
                    else
                    {
                        RowPlayerTableau[rowInTableau, colInTableau] = 1;
                    }
                }
            }
            // Col player tableau is I A 1.
            ColPlayerTableau = new double[NumRowsInTableau_ColPlayer, NumColsInTableau];
            for (int rowInTableau = 0; rowInTableau < NumRowsInTableau_ColPlayer; rowInTableau++)
            {
                for (int colInTableau = 0; colInTableau < NumColsInTableau; colInTableau++)
                {
                    if (colInTableau < NumRowStrategies)
                    {
                        double valueFromIdentityMatrix;
                        if (rowInTableau == colInTableau)
                            valueFromIdentityMatrix = 1.0;
                        else
                            valueFromIdentityMatrix = 0;
                        RowPlayerTableau[rowInTableau, colInTableau] = valueFromIdentityMatrix;
                    }
                    else if (colInTableau < NumRowStrategies + NumColStrategies)
                    {
                        int colInUtilityMatrix = colInTableau - NumRowStrategies;
                        RowPlayerTableau[rowInTableau, colInUtilityMatrix] = RowPlayerUtilities_A[rowInTableau, colInUtilityMatrix];
                    }
                    else
                    {
                        RowPlayerTableau[rowInTableau, colInTableau] = 1;
                    }
                }
            }
        }

        public string ToString(bool rowPlayer) => rowPlayer ? RowPlayerTableau.ToString(4, 10) : ColPlayerTableau.ToString(4, 10);

        public void PrintTableaux()
        {
            TabbedText.WriteLine($"Row player:");
            TabbedText.WriteLine(ToString(true));
            TabbedText.WriteLine($"Col player:");
            TabbedText.WriteLine(ToString(false));
        }
    }

    public class tableau_old
    {
        List<List<double>> matrix = new List<List<double>>();
        List<string> xVars = new List<string>(), sVars = new List<string>(); //x1,x2,x3,... //s1,s2,s3,...

        public tableau_old()
        {

        }

        public string to_string(int i) => i.ToString();

        public tableau_old(List<List<int>> payoffMatrix, bool isPlayer1)
        {
            int rows = payoffMatrix.Count();
            int columns = payoffMatrix[0].Count();

            Debug.WriteLine("rows: " + rows );
            Debug.WriteLine("columns: " + columns );

            if (isPlayer1)
            {
                matrix.Resize(columns);

                for (int i = 1; i <= rows; i++)
                {
                    xVars.Add("x" + to_string(i));
                }

                for (int i = rows + 1; i < columns + rows + 1; i++)
                {
                    sVars.Add("s" + to_string(i));
                }

                for (int i = rows; i < matrix.Count() + rows; i++)
                {
                    matrix[i - rows].Add(-(i + 1));
                    matrix[i - rows].Add(1);

                    for (int j = 0; j < columns; j++)
                    {
                        matrix[i - rows].Add(0);
                    }

                    for (int j = 0; j < rows; j++)
                    {
                        matrix[i - rows].Add(-payoffMatrix[j][i - rows]);
                    }
                }
            }
            else
            {
                matrix.Resize(rows);

                for (int i = 1; i <= rows; i++)
                {
                    sVars.Add("s" + to_string(i));
                }

                for (int i = rows + 1; i < columns + rows + 1; i++)
                {
                    xVars.Add("x" + to_string(i));
                }


                for (int i = 0; i < matrix.Count(); i++)
                {
                    matrix[i].Add(-(i + 1));
                    matrix[i].Add(1);

                    for (int j = 0; j < rows; j++)
                    {
                        matrix[i].Add(0);
                    }

                    for (int j = 0; j < columns; j++)
                    {
                        matrix[i].Add(-payoffMatrix[i][j]);
                    }
                }
            }
        }
        
        /// <summary>
        /// Finds a string in a list of strings and returns the index where there is a match.
        /// If no match is found, returns false and sets index to -1.
        /// </summary>
        public bool checkVar(string var, List<string> v, ref int index)
        {
            for (int i = 0; i < v.Count(); i++)
            {
                if (var == v[i])
                {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }

        public bool checkVar(string var, List<string> v)
        {
            int useless = 0;
            return checkVar(var, v, ref useless);
        }

        public bool isMember(string var)
        {
            return checkVar(var, xVars) || checkVar(var, sVars);
        }

        public int getColumn(string var)
        {
            int index = 0;

            if (checkVar(var, xVars, ref index))
            {
                return index + sVars.Count() + 2;
            }
            else if (checkVar(var, sVars, ref index))
            {
                return index + 2;
            }

            //this should not happen...
            throw new Exception("var unknown");
        }


        public List<ValueTuple<string, double>> getEquilibrium()
        {
            List<ValueTuple<string, double>> result = new List<(string, double)>();
            double sum = 0.0;

            Debug.WriteLine("equilibrium: \n");

            for (int i = 0; i < matrix.Count(); i++)
            {
                if (matrix[i][0] > 0)
                {
                    result.Add(("x" + to_string((int)matrix[i][0]), matrix[i][1]));
                    sum += matrix[i][1];
                }
            }

            for (int i = 0; i < result.Count(); i++)
            {
                Debug.WriteLine(result[i].Item1 + ": " + (result[i].Item2) / sum + ' ' );
            }
            Debug.WriteLine("");

            return result;
        }

        public void printTableau()
        {
            Debug.Write("\t");
            for (int i = 0; i < sVars.Count(); i++)
            {
                Debug.Write('\t' + sVars[i]);
            }

            for (int i = 0; i < xVars.Count(); i++)
            {
                Debug.Write('\t' + xVars[i]) ;
            }

            Debug.WriteLine("");

            for (int i = 0; i < matrix.Count(); i++)
            {
                for (int j = 0; j < matrix[i].Count(); j++)
                {
                    Debug.Write(matrix[i][j] + "\t");
                }
                Debug.WriteLine("");
            }

            Debug.WriteLine("");

        }

        public int rows()
        {
            return matrix.Count();
        }

        public int columns()
        {
            return columns(0);
        }

        public int columns(int index)
        {
            return matrix[index].Count();
        }

        public double getCoefficient(int row, int column)
        {
            return matrix[row][column];
        }

        public void setCoefficient(int row, int column, double value)
        {
            matrix[row][column] = value;
        }

        public string getVariable(int row)
        {
            // Debug.WriteLine(row + ' ' + matrix[row][0] );
            string result;
            if ((matrix[row][0]) < 0)
            {
                result = "s" + to_string(Math.Abs((int)matrix[row][0]));

            }
            else if ((matrix[row][0]) > 0)
            {
                result = "x" + to_string((int)(matrix[row][0]));

            }
            else
            {
                throw new Exception("Unknown index." );
            }

            return result;
        }

    };

    public class lemke_howson
    {
        public int runAlgorithm(int maxIterations, int[,] v1, int[,] v2) => runAlgorithm(maxIterations, v1.ConvertToListOfLists(), v2.ConvertToListOfLists());

        public int runAlgorithm(int maxIterations, List<List<int>> v1, List<List<int>> v2)
        {
            long iterations = 0;

            tableau_old t1 = new tableau_old(v1, true);
            tableau_old t2 = new tableau_old(v2, false);

            Debug.WriteLine("t1");
            t1.printTableau();
            Debug.WriteLine("");
            Debug.WriteLine("t2");
            t2.printTableau();
            Debug.WriteLine("");


            List<string> varPool = new List<string>();
            for (int i = 0; i < v1.Count() + v1[0].Count(); i++)
            {
                varPool.Add("x" + (i + 1).ToString());
            }

            int seed = 0; // could replace with random seed
            RandomSubset.Shuffle(varPool, seed);

            // Debug.WriteLine(varPool.Count() );

            int index = -1;
            bool foundEquilibrium = false;
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            long num_iter = 0;
            int num_index = 0;

            while (++index < varPool.Count() && !foundEquilibrium)
            {
                iterations = 0;

                num_index += 1;

                lemke_howson_helper(t1, t2, varPool[index], maxIterations, ref iterations, out foundEquilibrium);

                num_iter += iterations;

                if (foundEquilibrium)
                    break;

                // Debug.WriteLine(iterations + " " + elapsed.count()/10000000.0 + " " + varPool[index] );
            }

            if (!foundEquilibrium)
            {
                iterations = 0;
                lemke_howson_helper(t1, t2, varPool[index - 1], 0x3f3f3f3f, ref iterations, out foundEquilibrium);

                num_index += 1;
            }

            stopwatch.Stop();
            var elapsed = stopwatch.ElapsedMilliseconds;

            Debug.WriteLine(num_iter + ", " + num_index + ", " + elapsed.ToString() + ", " + foundEquilibrium);


            return 0;
        }

        public void lemke_howson_helper(tableau_old t1, tableau_old t2, string startPivot, long maxIterations, ref long iterations, out bool result)
        {
            result = false;
            string pivot = startPivot;

            while (true)
            {

                if (iterations + 1 > maxIterations)
                {
                    result = false;
                    return;
                }

                iterations++;

                // Debug.WriteLine("var_in: " + pivot );

                tableau_old cur_tab = null;

                if (t1.isMember(pivot))
                {
                    cur_tab = t1;
                    Debug.WriteLine("cur_tab = t1\n");
                    t1.printTableau();
                }
                else
                {
                    cur_tab = t2;
                    Debug.WriteLine("cur_tab = t2\n");
                    t2.printTableau();
                }

                int col_i = cur_tab.getColumn(pivot);
                int row_i = 0;

                double minimum_ratio = 0x3f3f3f3f;
                for (int i = 0; i < cur_tab.rows(); i++)
                {

                    if (cur_tab.getCoefficient(i, col_i) < 0)
                    {
                        double ratio = -cur_tab.getCoefficient(i, 1) / cur_tab.getCoefficient(i, col_i);
                        Debug.WriteLine("ratio: " + ratio );
                        if (ratio < minimum_ratio)
                        {
                            Debug.WriteLine("minimum_ratio "  + ratio + " " + cur_tab.getCoefficient(i,0) );
                            minimum_ratio = ratio;
                            row_i = i;
                        }
                    }
                }

                string var_out = cur_tab.getVariable(row_i);
                int col_i_out = cur_tab.getColumn(var_out);

                cur_tab.printTableau();

                Debug.WriteLine(pivot + " [row_i] = " + row_i + " --- " + Convert.ToInt32(pivot.Substring(1,pivot.Count()-1)) );
                Debug.WriteLine("var_out: " + var_out);
                Debug.WriteLine("");

                cur_tab.setCoefficient(row_i, col_i_out, -1);
                cur_tab.setCoefficient(row_i, 0,
                  (pivot[0] == 'x' ? 1 : -1) * Convert.ToInt32(pivot.Substring(1, pivot.Count() - 1)));

                for (int j = 1; j < cur_tab.columns(row_i); j++)
                {
                    if (j != col_i)
                    {
                        cur_tab.setCoefficient(row_i, j, cur_tab.getCoefficient(row_i, j) / -cur_tab.getCoefficient(row_i, col_i));
                    }
                }

                cur_tab.setCoefficient(row_i, col_i, 0);

                for (int i = 0; i < cur_tab.rows(); i++)
                {
                    if (cur_tab.getCoefficient(i, col_i) != 0)
                    {

                        for (int j = 1; j < cur_tab.columns(i); j++)
                        {
                            cur_tab.setCoefficient(i, j, cur_tab.getCoefficient(i, j) + cur_tab.getCoefficient(i, col_i) * cur_tab.getCoefficient(row_i, j));
                        }

                        cur_tab.setCoefficient(i, col_i, 0);
                    }
                }

                cur_tab.printTableau();

                pivot = relatedVar(var_out);
                if (pivot.Substring(1, pivot.Count() - 1) == startPivot.Substring(1, startPivot.Count() - 1))
                {
                    break;
                }
            }

            // Debug.WriteLine("=======\n";
            // t1.printTableau();
            // t1.getEquilibrium();
            // t2.printTableau();
            // t2.getEquilibrium();


            result = true;
        }

        public string relatedVar(string v)
        {
            if (v[0] == 's')
            {
                StringBuilder sb = new StringBuilder(v);
                sb[0] = 'x';
                v = sb.ToString();
            }
            else if (v[0] == 'x')
            {
                StringBuilder sb = new StringBuilder(v);
                sb[0] = 's';
                v = sb.ToString();
            }

            return v;
        }
    }
}
