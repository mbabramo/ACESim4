using ACESimBase.GameSolvingSupport.Evaluation;
using ACESimBase.GameSolvingSupport.Policies;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using N=ACESimBase.GameSolvingSupport.Evaluation.PolicyGameTree.Node;

namespace ACESimTest.StrategiesTests;

[TestClass]
public class JointReplayObserverTests
{
    private static PolicyGameTree Game()=>new(N.Decision(0,N.Decision(1,N.Terminal(1,10),N.Terminal(0,0)),
        N.Decision(1,N.Terminal(0,0),N.Terminal(1,20))),[
        new InformationSetPolicy("p",0,0,["0","1"],[.5,.5]),new InformationSetPolicy("d",1,1,["0","1"],[.5,.5])]);
    private static PlayerPolicy Pure(PolicyGameTree g,byte p,int action)=>g.MakePolicy(p,_=>action==0?[1.0,0.0]:[0.0,1.0]);

    [TestMethod]
    public void ObserverRetainsJointCorrelationInsteadOfProductMarginals()
    {
        var g=Game();var ps=new[]{Pure(g,0,0),Pure(g,0,1)};var ds=new[]{Pure(g,1,0),Pure(g,1,1)};
        double[] v=new double[2];int leaves=0;
        double mass=g.VisitJointTerminals(ps,ds,[.5,0,0,.5],(_,w)=>leaves++,(u,w)=>{v[0]+=w*u[0];v[1]+=w*u[1];});
        Assert.AreEqual(1,mass);Assert.AreEqual(2,leaves);Assert.AreEqual(1,v[0]);Assert.AreEqual(15,v[1]);
        double product=g.Evaluate(g.Mix(ps,[.5,.5]),g.Mix(ds,[.5,.5]))[0];
        Assert.AreEqual(.5,product);Assert.AreNotEqual(v[0],product);
    }

    [TestMethod]
    public void AddingObserverLeavesOriginalTerminalReplayBitwiseUnchanged()
    {
        var g=Game();var ps=new[]{Pure(g,0,0),Pure(g,0,1)};var ds=new[]{Pure(g,1,0),Pure(g,1,1)};
        var old=new List<string>();var observed=new List<string>();
        var a=g.VisitJointTerminals(ps,ds,[.1,.2,.3,.4],(path,w)=>old.Add(string.Join(",",path)+":"+BitConverter.DoubleToInt64Bits(w)));
        var b=g.VisitJointTerminals(ps,ds,[.1,.2,.3,.4],(path,w)=>observed.Add(string.Join(",",path)+":"+BitConverter.DoubleToInt64Bits(w)),(_,_)=>{});
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(a),BitConverter.DoubleToInt64Bits(b));CollectionAssert.AreEqual(old,observed);
    }

    [TestMethod]
    public void ObserverStillRejectsMalformedWeights()
    {
        var g=Game();Assert.ThrowsException<ArgumentException>(()=>g.VisitJointTerminals([Pure(g,0,0)],[Pure(g,1,0)],[.5],(_,_)=>{},(_,_)=>{}));
    }
}
