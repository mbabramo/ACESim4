using System;
using System.Collections.Generic;
using System.Linq;
using ACESimBase.GameSolvingSupport.ExactValues;
using MathNet.Numerics.LinearAlgebra.Double;

namespace ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm;

public sealed class FloatingLemkeDiagnostics
{
    public int Refactorizations { get; set; }
    public int ResidualRepairs { get; set; }
    public int RepeatedBases { get; set; }
    public int Pivots { get; set; }
    public double MaximumAcceptedResidual { get; set; }
    public double MaximumObservedResidual { get; set; }
    public string Termination { get; set; }
}

public partial class ECTALemke<T> where T : IMaybeExact<T>, new()
{
    // This branch is entered ONLY for InexactValue. Exact arithmetic, scaling,
    // fraction-free pivots and exact lexicographic comparisons are untouched.
    public FloatingLemkeDiagnostics FloatingDiagnostics { get; private set; }
    private double[][] floatingTableau;
    private double[][] originalMatrix;
    private double[] originalQ, originalD;
    private const double FloatingPivotTolerance = 1e-12;
    private const double FloatingRatioTolerance = 1e-11;
    private const double FloatingResidualTolerance = 1e-9;

    private double FloatingCoefficient(int row, int variable) => variable == 0 ? -originalD[row] :
        variable <= n ? -originalMatrix[row][variable-1] : (variable-n-1 == row ? 1 : 0);

    private void RebuildFloatingBasis()
    {
        // Managed LU, explicitly single-threaded; no native provider or row parallelism.
        MathNet.Numerics.Control.MaxDegreeOfParallelism = 1;
        var basis = new DenseMatrix(n,n);
        var right = new DenseMatrix(n,n+2);
        for(int i=0;i<n;i++)
        {
            double scale=Math.Max(1,Math.Max(Math.Abs(originalD[i]),originalMatrix[i].Max(Math.Abs)));
            for(int j=0;j<n;j++) basis[i,j]=FloatingCoefficient(i,basicCobasicIndexToVariable[j])/scale;
            for(int j=0;j<=n;j++) right[i,j]=FloatingCoefficient(i,basicCobasicIndexToVariable[n+j])/scale;
            right[i,n+1]=originalQ[i]/scale;
        }
        var factor=basis.LU();
        var solution=factor.Solve(right);
        // One refinement using the original basis, not accumulated tableau updates.
        var correction=factor.Solve(right-basis*solution);
        solution+=correction;
        for(int i=0;i<n;i++) for(int j=0;j<n+2;j++)
        {
            double value=solution[i,j];
            if(!double.IsFinite(value)) throw new ECTAException("Floating basis factorization is singular/nonfinite.");
            floatingTableau[i][j]=value;
        }
        FloatingDiagnostics.Refactorizations++;
    }

    private double CheckFloatingEquations()
    {
        double[] values=new double[2*n+1];
        for(int i=0;i<n;i++) values[basicCobasicIndexToVariable[i]]=floatingTableau[i][n+1];
        double worst=0;
        for(int i=0;i<n;i++)
        {
            double sum=originalQ[i]+originalD[i]*values[0];
            double scale=1+Math.Abs(sum)+Math.Abs(values[n+i+1]);
            for(int j=0;j<n;j++) {double term=originalMatrix[i][j]*values[j+1];sum+=term;scale+=Math.Abs(term);}
            worst=Math.Max(worst,Math.Abs(sum-values[n+i+1])/scale);
        }
        foreach(double value in values)
        {
            if(!double.IsFinite(value)) return double.PositiveInfinity;
            worst=Math.Max(worst,Math.Max(0,-value)/(1+Math.Abs(value)));
        }
        return worst;
    }

    private void ValidateFloatingBasis()
    {
        double error=CheckFloatingEquations();
        FloatingDiagnostics.MaximumObservedResidual=Math.Max(FloatingDiagnostics.MaximumObservedResidual,error);
        if(!double.IsFinite(error) || error>FloatingResidualTolerance)
        {
            FloatingDiagnostics.ResidualRepairs++;
            RebuildFloatingBasis();
            error=CheckFloatingEquations();
            if(!double.IsFinite(error) || error>FloatingResidualTolerance)
                throw new ECTAException($"Floating basis failed original-equation/feasibility check: {error:R}");
        }
        FloatingDiagnostics.MaximumAcceptedResidual=Math.Max(FloatingDiagnostics.MaximumAcceptedResidual,error);
    }

    private int FloatingLeaving(int enter, bool initial)
    {
        int col=TableauColumn(enter);
        double columnScale=Math.Max(1,floatingTableau.Max(row=>Math.Abs(row[col])));
        var candidates=Enumerable.Range(0,n).Where(i=>
            (initial ? -floatingTableau[i][col] : floatingTableau[i][col])>FloatingPivotTolerance*columnScale).ToList();
        if(candidates.Count==0) throw new ECTAException("No numerically safe floating leaving variable.");
        double Den(int i)=>initial ? -floatingTableau[i][col] : floatingTableau[i][col];
        for(int j=0;j<=n && candidates.Count>1;j++)
        {
            int testcol=j==0 ? n+1 : TableauColumn(W(j));
            if(testcol==col) continue;
            if(testcol<0)
            {
                candidates.Remove(variableIndexToBasicCobasicIndex[W(j)]);
                continue;
            }
            double min=candidates.Min(i=>floatingTableau[i][testcol]/Den(i));
            candidates=candidates.Where(i=>{
                double value=floatingTableau[i][testcol]/Den(i);
                return Math.Abs(value-min)<=FloatingRatioTolerance*Math.Max(1,Math.Max(Math.Abs(value),Math.Abs(min)));
            }).ToList();
        }
        if(candidates.Count==0) throw new ECTAException("Floating lexicographic selection lost all candidates.");
        // Deterministic last resort for numerically indistinguishable rows.
        return basicCobasicIndexToVariable[candidates.OrderBy(i=>basicCobasicIndexToVariable[i]).First()];
    }

    private void FloatingPivot(int leave,int enter)
    {
        int row=TableauRow(leave),col=TableauColumn(enter);
        double pivot=floatingTableau[row][col];
        if(!double.IsFinite(pivot) || pivot==0) throw new ECTAException("Invalid floating pivot.");
        var normalized=new double[n+2];
        for(int j=0;j<n+2;j++) normalized[j]=j==col ? 1/pivot : floatingTableau[row][j]/pivot;
        for(int i=0;i<n;i++) if(i!=row)
        {
            double coefficient=floatingTableau[i][col];
            for(int j=0;j<n+2;j++) if(j!=col)
                floatingTableau[i][j]-=coefficient*normalized[j];
            floatingTableau[i][col]=-coefficient*normalized[col];
        }
        floatingTableau[row]=normalized;
        variableIndexToBasicCobasicIndex[leave]=n+col;basicCobasicIndexToVariable[n+col]=leave;
        variableIndexToBasicCobasicIndex[enter]=row;basicCobasicIndexToVariable[row]=enter;
    }

    private void RunFloatingLemke(ECTALemkeOptions flags)
    {
        FloatingDiagnostics=new();
        originalMatrix=lcpM.Select(row=>row.Select(v=>v.AsDouble).ToArray()).ToArray();
        originalQ=rhsq.Select(v=>v.AsDouble).ToArray();originalD=coveringVectorD.Select(v=>v.AsDouble).ToArray();
        ConfirmCoveringVectorOK();InitializeTableauVariables();InitMinRatioTestStatistics();
        determinant=IMaybeExact<T>.One();
        for(int j=0;j<scaleFactors.Length;j++) scaleFactors[j]=IMaybeExact<T>.One();
        floatingTableau=Enumerable.Range(0,n).Select(i=>Enumerable.Range(0,n+2).Select(j=>j==n+1 ? originalQ[i] : FloatingCoefficient(i,j)).ToArray()).ToArray();
        var visited=new HashSet<string>();
        int enter=0;bool initial=true;
        try
        {
            for(pivotcount=1;;pivotcount++)
            {
                int leave;
                try {leave=FloatingLeaving(enter,initial);}
                catch(ECTAException) when(!initial) {RebuildFloatingBasis();ValidateFloatingBasis();leave=FloatingLeaving(enter,false);}
                FloatingPivot(leave,enter);initial=false;
                if(pivotcount%100==0) RebuildFloatingBasis();
                ValidateFloatingBasis();
                string basis=string.Join(",",basicCobasicIndexToVariable.Take(n).OrderBy(v=>v));
                if(!visited.Add(basis))
                {
                    FloatingDiagnostics.RepeatedBases++;
                    RebuildFloatingBasis();ValidateFloatingBasis();
                    throw new ECTAException("Floating basis repeated after numerical reconstruction; ending this start.");
                }
                FloatingDiagnostics.Pivots=pivotcount;
                bool final=leave==0;
                PivotObserver?.Invoke(CapturePivot(leave,enter,final));
                if(final)
                {
                    for(int j=0;j<n;j++) {int row=TableauRow(j+1);solz[j]=IMaybeExact<T>.FromDouble(row<n ? floatingTableau[row][n+1] : 0);}
                    FloatingDiagnostics.Termination="LCP completed";
                    return;
                }
                if(flags.maxPivotSteps>0 && pivotcount>=flags.maxPivotSteps) throw new ECTAException("Floating pivot cap reached.");
                enter=ComplementOfVariable(leave);
            }
        }
        catch(Exception ex) {FloatingDiagnostics.Termination=ex.Message;throw;}
    }
}
