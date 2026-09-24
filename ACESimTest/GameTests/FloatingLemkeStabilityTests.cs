using ACESimBase.GameSolvingAlgorithms.ECTAAlgorithm;
using ACESimBase.GameSolvingSupport.ExactValues;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace ACESimTest.GameTests;
[TestClass,DoNotParallelize]
public class FloatingLemkeStabilityTests
{
    private static ECTALemke<T> Solve<T>(double[,] matrix,double[] q) where T:IMaybeExact<T>,new()
    {
        int n=q.Length;var l=new ECTALemke<T>(n);
        for(int i=0;i<n;i++) {l.rhsq[i]=IMaybeExact<T>.FromRational((Rationals.Rational)q[i]);l.coveringVectorD[i]=IMaybeExact<T>.One();for(int j=0;j<n;j++)l.lcpM[i][j]=IMaybeExact<T>.FromRational((Rationals.Rational)matrix[i,j]);}
        l.RunLemke(new ECTALemkeOptions{maxPivotSteps=100});return l;
    }
    [TestMethod]
    public void DegenerateStartReconstructsKnownSolutionAndKeepsExactBranch()
    {
        var matrix=new double[,]{{2,-1},{-1,2}};var q=new[]{-1.0,-1.0};
        var floating=Solve<InexactValue>(matrix,q);var exact=Solve<ExactValue>(matrix,q);
        for(int i=0;i<2;i++){Assert.AreEqual(1, floating.solz[i].AsDouble,1e-10);Assert.AreEqual(exact.solz[i].AsDouble,floating.solz[i].AsDouble,1e-10);}
        Assert.IsNotNull(floating.FloatingDiagnostics);Assert.IsNull(exact.FloatingDiagnostics);
        Assert.IsTrue(floating.FloatingDiagnostics.MaximumAcceptedResidual<=1e-9);
    }
    [TestMethod]
    public void SmallButResolvedCoefficientDoesNotGetErased()
    {
        var l=Solve<InexactValue>(new double[,]{{1e-8}},new[]{-1.0});
        Assert.AreEqual(1e8,l.solz[0].AsDouble,1e-3);
    }
    [TestMethod]
    public void SingularRayEndsWithoutPublishingASolution()
    {
        Assert.ThrowsException<ECTAException>(()=>Solve<InexactValue>(new double[,]{{0}},new[]{-1.0}));
    }
}
