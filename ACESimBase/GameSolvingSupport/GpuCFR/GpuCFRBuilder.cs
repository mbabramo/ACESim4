// ACESimBase/GameSolvingSupport/GpuCFR/GpuCFRBuilder.cs
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Reflection;
using ILGPU;
using ILGPU.Runtime;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Settings;
using Microsoft.FSharp.Core;
using ACESim;
using ACESimBase.Util.Debugging;

namespace ACESimBase.GameSolvingSupport.GpuCFR
{
    public sealed class GpuCFRBuilder : IDisposable
    {
        #region Public surface

        public sealed class RootNode
        {
            private readonly GpuCFRBuilder _owner;
            internal RootNode(GpuCFRBuilder owner) => _owner = owner;

            public NodeResult Go(ref IterationContext ctx)
            {
                _owner._pending.Clear();

                var pi = ArrayPool<double>.Shared.Rent(_owner.NumNonChancePlayers);
                var avgPi = ArrayPool<double>.Shared.Rent(_owner.NumNonChancePlayers);
                try
                {
                    for (byte p = 0; p < _owner.NumNonChancePlayers; p++)
                    {
                        pi[p] = 1.0;
                        avgPi[p] = 1.0;
                    }

                    var hp = _owner._rootFactory();
                    var u = _owner.Visit(in hp, ref ctx, pi, avgPi);
                    return new NodeResult(u);
                }
                finally
                {
                    ArrayPool<double>.Shared.Return(pi, clearArray: true);
                    ArrayPool<double>.Shared.Return(avgPi, clearArray: true);
                }
            }
        }

        public readonly struct NodeResult
        {
            public NodeResult(double[] utilities) => Utilities = utilities;
            public double[] Utilities { get; }
        }

        public struct IterationContext
        {
            public int IterationNumber;
            public byte OptimizedPlayerIndex;
            public int ScenarioIndex;
            public double ReachSelf;
            public double ReachOpp;
            public double ReachChance;
            public double SamplingCorrection;
            public bool SuppressMath;
        }

        public sealed class GpuCFRBuilderOptions
        {
            public bool UseDynamicChanceProbabilities { get; set; } = true;
            public byte NumNonChancePlayers { get; set; } = 2;
        }

        public RootNode Root { get; }
        public bool IsAvailable { get; private set; }
        public byte NumNonChancePlayers { get; }

        public GpuCFRBuilder(
            HistoryNavigationInfo navigation,
            Func<HistoryPoint> rootFactory,
            bool useFloat,
            GpuCFRBuilderOptions options)
        {
            _navigation = navigation;
            _rootFactory = rootFactory ?? throw new ArgumentNullException(nameof(rootFactory));
            _useFloatPreference = useFloat;
            _options = options ?? new GpuCFRBuilderOptions();
            NumNonChancePlayers = _options.NumNonChancePlayers;

            _context = Context.Create(builder => builder.Default().EnableAlgorithms());

            Accelerator found = null;
            foreach (var device in _context)
            {
                if (device.AcceleratorType == AcceleratorType.Cuda ||
                    device.AcceleratorType == AcceleratorType.OpenCL)
                {
                    found = device.CreateAccelerator(_context);
                    break;
                }
            }

            if (found == null)
            {
                throw new InvalidOperationException("No compatible GPU accelerator (CUDA or OpenCL) was found. GPU mode cannot be used on this system.");
            }

            _accelerator = found;
            _useDeviceFloat = _useFloatPreference;

            LoadKernels();

            Root = new RootNode(this);
            IsAvailable = true;
        }

        public IterationContext InitializeIteration(byte optimizedPlayerIndex, int scenarioIndex, Func<int, double> rand01ForDecision)
        {
            return new IterationContext
            {
                IterationNumber = 0,
                OptimizedPlayerIndex = optimizedPlayerIndex,
                ScenarioIndex = scenarioIndex,
                ReachSelf = 1.0,
                ReachOpp = 1.0,
                ReachChance = 1.0,
                SamplingCorrection = 1.0,
                SuppressMath = false
            };
        }

        public void CopyTalliesIntoBackingNodes()
        {
            int n = _pending.Count;
            if (n == 0)
            {
                _pending.Clear();
                return;
            }

            // Always-on parity summary of pending updates before apply
            {
                int p0 = 0, p1 = 0, pOther = 0;
                double sumRTimesInvPi = 0.0, sumInvPi = 0.0, sumCumInc = 0.0;
                var unique = new HashSet<InformationSetNode>();

                for (int i = 0; i < n; i++)
                {
                    var rec = _pending[i];

                    if (rec.Node.PlayerIndex == 0) p0++;
                    else if (rec.Node.PlayerIndex == 1) p1++;
                    else pOther++;

                    unique.Add(rec.Node);

                    double r = rec.Q - rec.V;
                    sumRTimesInvPi += r * rec.InversePi;
                    sumInvPi += rec.InversePi;
                    sumCumInc += rec.PiSelf * rec.PAction;
                }

                if (GeneralizedVanilla.EnableParityLogging)
                    TabbedText.WriteLine($"[PARITY] GPU pending pre-apply n={n} p0={p0} p1={p1} other={pOther} uniqueNodes={unique.Count} sumR×InvPi={sumRTimesInvPi:G17} sumInvPi={sumInvPi:G17} sumAvgStrInc={sumCumInc:G17}");
            }

            if (_accelerator is null)
            {
                CpuApplyPending();
                _pending.Clear();
                return;
            }

            if (_useDeviceFloat)
                RunKernelFloat(n);
            else
                RunKernelDouble(n);

            _pending.Clear();
        }



        #endregion

        #region Private state

        private readonly HistoryNavigationInfo _navigation;
        private readonly Func<HistoryPoint> _rootFactory;
        private readonly bool _useFloatPreference;
        private readonly GpuCFRBuilderOptions _options;

        private readonly Context _context;
        private readonly Accelerator _accelerator;
        private bool _useDeviceFloat;

        private Action<Index1D,
            ArrayView<double>, ArrayView<double>, ArrayView<double>, ArrayView<double>, ArrayView<double>,
            ArrayView<double>, ArrayView<double>, ArrayView<double>> _kernelDouble;

        private Action<Index1D,
            ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<float> ,
            ArrayView<float>, ArrayView<float>, ArrayView<float>> _kernelFloat;

        private struct PendingUpdate
        {
            public InformationSetNode Node;
            public byte ActionOneBased;
            public double Q;
            public double V;
            public double InversePi;
            public double PiSelf;
            public double PAction;
        }

        private readonly List<PendingUpdate> _pending = new List<PendingUpdate>(capacity: 1 << 14);

        private DoubleDeviceBuffers _dbl;
        private FloatDeviceBuffers _flt;

        // Cache for scenario-aware terminal utilities
        private readonly Dictionary<FinalUtilitiesNode, double[][]> _finalUtilitiesCache = new Dictionary<FinalUtilitiesNode, double[][]>();

        #endregion

        #region Traversal (CPU)

        private double[] Visit(in HistoryPoint hp, ref IterationContext ctx, double[] pi, double[] avgPi)
        {
            var state = hp.GetGameStatePrerecorded(_navigation) ?? _navigation.GetGameState(in hp);

            if (state is FinalUtilitiesNode fu)
            {
                // Respect ScenarioIndex just like the Fast/Regular paths.
                var utilsByScenario = GetFinalUtilitiesByScenario(fu);
                int s = (uint)ctx.ScenarioIndex < (uint)utilsByScenario.Length ? ctx.ScenarioIndex : 0;
                var src = utilsByScenario[s];

                var u = new double[NumNonChancePlayers];
                int copy = Math.Min(NumNonChancePlayers, src.Length);
                for (int i = 0; i < copy; i++)
                    u[i] = src[i];
                return u;
            }
            else if (state is ChanceNode cn)
            {
                return VisitChance(in hp, cn, ref ctx, pi, avgPi);
            }
            else
            {
                return VisitDecision(in hp, (InformationSetNode)state, ref ctx, pi, avgPi);
            }
        }

        private double[] VisitChance(in HistoryPoint hp, ChanceNode cn, ref IterationContext ctx, double[] pi, double[] avgPi)
        {
            var decision = _navigation.GameDefinition.DecisionsExecutionOrder[cn.DecisionIndex];
            byte numOutcomes = decision.NumPossibleActions;

            var expected = new double[NumNonChancePlayers];

            for (byte a = 1; a <= numOutcomes; a++)
            {
                double p = cn.GetActionProbability(a);
                if (p == 0.0)
                    continue;

                var nextPi = ArrayPool<double>.Shared.Rent(NumNonChancePlayers);
                var nextAvgPi = ArrayPool<double>.Shared.Rent(NumNonChancePlayers);
                try
                {
                    GetNextPiValues(pi,    ctx.OptimizedPlayerIndex, p, changeOtherPlayers: true,  dest: nextPi);
                    GetNextPiValues(avgPi, ctx.OptimizedPlayerIndex, p, changeOtherPlayers: true,  dest: nextAvgPi);

                    HistoryPoint nextHp;
                    if (_navigation.LookupApproach != InformationSetLookupApproach.PlayGameDirectly && cn.Decision.IsReversible)
                        nextHp = hp.SwitchToBranch(_navigation, a, cn.Decision, cn.DecisionIndex);
                    else
                        nextHp = hp.GetBranch(_navigation, a, cn.Decision, cn.DecisionIndex);

                    var childU = Visit(in nextHp, ref ctx, nextPi, nextAvgPi);
                    for (int i = 0; i < NumNonChancePlayers; i++)
                        expected[i] += p * childU[i];

                    if (_navigation.LookupApproach != InformationSetLookupApproach.PlayGameDirectly && cn.Decision.IsReversible)
                        _navigation.GameDefinition.ReverseSwitchToBranchEffects(cn.Decision, in nextHp);
                }
                finally
                {
                    ArrayPool<double>.Shared.Return(nextPi, clearArray: true);
                    ArrayPool<double>.Shared.Return(nextAvgPi, clearArray: true);
                }
            }

            return expected;
        }
        private double[] VisitDecision(in HistoryPoint hp, InformationSetNode isn, ref IterationContext ctx, double[] pi, double[] avgPi)
        {
            byte decisionIndex = isn.DecisionIndex;
            var decision = _navigation.GameDefinition.DecisionsExecutionOrder[decisionIndex];
            byte numActions = decision.NumPossibleActions;
            byte playerAtNode = isn.PlayerIndex;

            const int StackThreshold = 64;

            Span<double> actionBuf = numActions <= StackThreshold ? stackalloc double[numActions] : default;
            double[] rentedActionBuf = null;
            Span<double> pAction = numActions <= StackThreshold ? actionBuf : (rentedActionBuf = ArrayPool<double>.Shared.Rent(numActions));

            if (decision.AlwaysDoAction is byte always)
            {
                SetAlwaysAction(numActions, pAction, always);
            }
            else
            {
                bool opponentProbabilities = playerAtNode != ctx.OptimizedPlayerIndex;
                isn.GetCurrentProbabilities(pAction, opponentProbabilities);
            }

            double[] expected = new double[NumNonChancePlayers];

            double inversePi = GetInversePiValue(pi, ctx.OptimizedPlayerIndex);

            double[] rentedEoA = null;
            Span<double> expectedOptimizedForAction =
                numActions <= StackThreshold ? stackalloc double[numActions] : (rentedEoA = ArrayPool<double>.Shared.Rent(numActions));

            double expectedOptimized = 0.0;

            for (byte a = 1; a <= numActions; a++)
            {
                double prob = pAction[a - 1];
                if (prob <= double.Epsilon && playerAtNode != ctx.OptimizedPlayerIndex)
                    continue;

                double avgProb = isn.GetAverageStrategy(a);

                var nextPi = ArrayPool<double>.Shared.Rent(NumNonChancePlayers);
                var nextAvgPi = ArrayPool<double>.Shared.Rent(NumNonChancePlayers);
                try
                {
                    GetNextPiValues(pi, playerAtNode, prob, changeOtherPlayers: false, dest: nextPi);
                    GetNextPiValues(avgPi, playerAtNode, avgProb, changeOtherPlayers: false, dest: nextAvgPi);

                    HistoryPoint nextHp;
                    if (_navigation.LookupApproach != InformationSetLookupApproach.PlayGameDirectly && isn.Decision.IsReversible)
                        nextHp = hp.SwitchToBranch(_navigation, a, isn.Decision, isn.DecisionIndex);
                    else
                        nextHp = hp.GetBranch(_navigation, a, isn.Decision, isn.DecisionIndex);

                    var childU = Visit(in nextHp, ref ctx, nextPi, nextAvgPi);

                    for (int i = 0; i < NumNonChancePlayers; i++)
                        expected[i] += prob * childU[i];

                    expectedOptimizedForAction[a - 1] = childU[ctx.OptimizedPlayerIndex];

                    if (_navigation.LookupApproach != InformationSetLookupApproach.PlayGameDirectly && isn.Decision.IsReversible)
                        _navigation.GameDefinition.ReverseSwitchToBranchEffects(isn.Decision, in nextHp);
                }
                finally
                {
                    ArrayPool<double>.Shared.Return(nextPi, clearArray: true);
                    ArrayPool<double>.Shared.Return(nextAvgPi, clearArray: true);
                }
            }

            if (playerAtNode == ctx.OptimizedPlayerIndex)
            {
                for (byte a = 1; a <= numActions; a++)
                    expectedOptimized += pAction[a - 1] * expectedOptimizedForAction[a - 1];

                // Match Regular flavor: clamp reach used for average‑strategy contribution.
                double piSelf = pi[ctx.OptimizedPlayerIndex];
                if (piSelf < InformationSetNode.SmallestProbabilityRepresented)
                    piSelf = InformationSetNode.SmallestProbabilityRepresented;

                for (byte a = 1; a <= numActions; a++)
                {
                    _pending.Add(new PendingUpdate
                    {
                        Node = isn,
                        ActionOneBased = a,
                        Q = expectedOptimizedForAction[a - 1],
                        V = expectedOptimized,
                        InversePi = inversePi,
                        PiSelf = piSelf,
                        PAction = pAction[a - 1]
                    });
                }
            }

            if (rentedActionBuf != null) ArrayPool<double>.Shared.Return(rentedActionBuf, clearArray: true);
            if (rentedEoA != null) ArrayPool<double>.Shared.Return(rentedEoA, clearArray: true);

            return expected;
        }


        #endregion

        #region ILGPU kernels + launchers

        private static void ComputeContribsDouble(
            Index1D i,
            ArrayView<double> q,
            ArrayView<double> v,
            ArrayView<double> invPi,
            ArrayView<double> piSelf,
            ArrayView<double> pAction,
            ArrayView<double> outRegretTimesInvPi,
            ArrayView<double> outInvPi,
            ArrayView<double> outCumStratInc)
        {
            double r = q[i] - v[i];
            outRegretTimesInvPi[i] = r * invPi[i];
            outInvPi[i] = invPi[i];
            outCumStratInc[i] = piSelf[i] * pAction[i];
        }

        private static void ComputeContribsFloat(
            Index1D i,
            ArrayView<float> q,
            ArrayView<float> v,
            ArrayView<float> invPi,
            ArrayView<float> piSelf,
            ArrayView<float> pAction,
            ArrayView<float> outRegretTimesInvPi,
            ArrayView<float> outInvPi,
            ArrayView<float> outCumStratInc)
        {
            float r = q[i] - v[i];
            outRegretTimesInvPi[i] = r * invPi[i];
            outInvPi[i] = invPi[i];
            outCumStratInc[i] = piSelf[i] * pAction[i];
        }

        private void LoadKernels()
        {
            if (_accelerator is null)
                return;

            if (_useDeviceFloat)
            {
                _kernelFloat = _accelerator.LoadAutoGroupedStreamKernel<
                    Index1D,
                    ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<float>,
                    ArrayView<float>, ArrayView<float>, ArrayView<float>>(ComputeContribsFloat);
            }
            else
            {
                _kernelDouble = _accelerator.LoadAutoGroupedStreamKernel<
                    Index1D,
                    ArrayView<double>, ArrayView<double>, ArrayView<double>, ArrayView<double>, ArrayView<double>,
                    ArrayView<double>, ArrayView<double>, ArrayView<double>>(ComputeContribsDouble);
            }
        }

        private void RunKernelDouble(int n)
        {
            EnsureDoubleCapacity(n);

            var q = new double[n];
            var v = new double[n];
            var invPi = new double[n];
            var piSelf = new double[n];
            var pAction = new double[n];

            for (int i = 0; i < n; i++)
            {
                var rec = _pending[i];
                q[i] = rec.Q;
                v[i] = rec.V;
                invPi[i] = rec.InversePi;
                piSelf[i] = rec.PiSelf;
                pAction[i] = rec.PAction;
            }

            var viewQ = _dbl.Q.View.SubView(0, n);
            var viewV = _dbl.V.View.SubView(0, n);
            var viewInv = _dbl.Inv.View.SubView(0, n);
            var viewPiSelf = _dbl.PiSelf.View.SubView(0, n);
            var viewPAct = _dbl.PAct.View.SubView(0, n);

            viewQ.CopyFromCPU(q);
            viewV.CopyFromCPU(v);
            viewInv.CopyFromCPU(invPi);
            viewPiSelf.CopyFromCPU(piSelf);
            viewPAct.CopyFromCPU(pAction);

            _kernelDouble(n, _dbl.Q.View, _dbl.V.View, _dbl.Inv.View, _dbl.PiSelf.View, _dbl.PAct.View,
                          _dbl.OutRInv.View, _dbl.OutInv.View, _dbl.OutCum.View);
            _accelerator.Synchronize();

            var hRInvPi = new double[n];
            var hInv = new double[n];
            var hCum = new double[n];

            _dbl.OutRInv.View.SubView(0, n).CopyToCPU(hRInvPi);
            _dbl.OutInv.View.SubView(0, n).CopyToCPU(hInv);
            _dbl.OutCum.View.SubView(0, n).CopyToCPU(hCum);

            for (int i = 0; i < n; i++)
            {
                var rec = _pending[i];
                rec.Node.IncrementLastRegret(rec.ActionOneBased, hRInvPi[i], hInv[i]);
                rec.Node.IncrementLastCumulativeStrategyIncrements(rec.ActionOneBased, hCum[i]);
            }
        }

        private void RunKernelFloat(int n)
        {
            EnsureFloatCapacity(n);

            var q = new float[n];
            var v = new float[n];
            var invPi = new float[n];
            var piSelf = new float[n];
            var pAction = new float[n];

            for (int i = 0; i < n; i++)
            {
                var rec = _pending[i];
                q[i] = (float)rec.Q;
                v[i] = (float)rec.V;
                invPi[i] = (float)rec.InversePi;
                piSelf[i] = (float)rec.PiSelf;
                pAction[i] = (float)rec.PAction;
            }

            var viewQ = _flt.Q.View.SubView(0, n);
            var viewV = _flt.V.View.SubView(0, n);
            var viewInv = _flt.Inv.View.SubView(0, n);
            var viewPiSelf = _flt.PiSelf.View.SubView(0, n);
            var viewPAct = _flt.PAct.View.SubView(0, n);

            viewQ.CopyFromCPU(q);
            viewV.CopyFromCPU(v);
            viewInv.CopyFromCPU(invPi);
            viewPiSelf.CopyFromCPU(piSelf);
            viewPAct.CopyFromCPU(pAction);

            _kernelFloat(n, _flt.Q.View, _flt.V.View, _flt.Inv.View, _flt.PiSelf.View, _flt.PAct.View,
                         _flt.OutRInv.View, _flt.OutInv.View, _flt.OutCum.View);
            _accelerator.Synchronize();

            var hRInvPi = new float[n];
            var hInv = new float[n];
            var hCum = new float[n];

            _flt.OutRInv.View.SubView(0, n).CopyToCPU(hRInvPi);
            _flt.OutInv.View.SubView(0, n).CopyToCPU(hInv);
            _flt.OutCum.View.SubView(0, n).CopyToCPU(hCum);

            for (int i = 0; i < n; i++)
            {
                var rec = _pending[i];
                rec.Node.IncrementLastRegret(rec.ActionOneBased, hRInvPi[i], hInv[i]);
                rec.Node.IncrementLastCumulativeStrategyIncrements(rec.ActionOneBased, hCum[i]);
            }
        }

        #endregion

        #region Helpers

        private void CpuApplyPending()
        {
            // CPU fallback identical to GPU kernel math, used when no accelerator is available.
            for (int i = 0; i < _pending.Count; i++)
            {
                var rec = _pending[i];
                double r = rec.Q - rec.V;
                double rTimesInvPi = r * rec.InversePi;
                rec.Node.IncrementLastRegret(rec.ActionOneBased, rTimesInvPi, rec.InversePi);
                rec.Node.IncrementLastCumulativeStrategyIncrements(rec.ActionOneBased, rec.PiSelf * rec.PAction);
            }
        }

        private static void SetAlwaysAction(byte numActions, Span<double> dest, byte actionOneBased)
        {
            for (int i = 0; i < numActions; i++) dest[i] = 0.0;
            dest[actionOneBased - 1] = 1.0;
        }

        private double GetInversePiValue(double[] pi, byte playerIndex)
        {
            double product = 1.0;
            // Only multiply the logical player slots. ArrayPool may return longer buffers.
            for (byte p = 0; p < NumNonChancePlayers; p++)
                if (p != playerIndex)
                    product *= pi[p];
            return product;
        }

        private void GetNextPiValues(double[] current, byte playerIndex, double prob, bool changeOtherPlayers, double[] dest)
        {
            // Only touch the logical player slots. Ignore any extra capacity from ArrayPool.
            for (byte p = 0; p < NumNonChancePlayers; p++)
            {
                double cur = current[p];
                if (p == playerIndex)
                    dest[p] = changeOtherPlayers ? cur : cur * prob;
                else
                    dest[p] = changeOtherPlayers ? cur * prob : cur;
            }
        }


        private double[][] GetFinalUtilitiesByScenario(FinalUtilitiesNode node)
        {
            if (_finalUtilitiesCache.TryGetValue(node, out var arr))
                return arr;

            var t = node.GetType();

            // Try UtilitiesByScenario first (multi-scenario games)
            var utilsByScProp = t.GetProperty("UtilitiesByScenario", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (utilsByScProp != null && utilsByScProp.GetValue(node) is System.Collections.IEnumerable seq)
            {
                var list = new List<double[]>();
                foreach (var item in seq)
                    list.Add((double[])item);
                arr = list.ToArray();
                _finalUtilitiesCache[node] = arr;
                return arr;
            }

            // Fallback to single Utilities array
            var utilsProp = t.GetProperty("Utilities", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            double[] single = utilsProp != null ? (double[])utilsProp.GetValue(node) : new double[NumNonChancePlayers];
            arr = new[] { single };
            _finalUtilitiesCache[node] = arr;
            return arr;
        }

        private void EnsureDoubleCapacity(int needed)
        {
            if (_dbl == null) _dbl = new DoubleDeviceBuffers();
            if (_dbl.Capacity >= needed) return;

            _dbl.Dispose();
            _dbl.Q      = _accelerator.Allocate1D<double>(needed);
            _dbl.V      = _accelerator.Allocate1D<double>(needed);
            _dbl.Inv    = _accelerator.Allocate1D<double>(needed);
            _dbl.PiSelf = _accelerator.Allocate1D<double>(needed);
            _dbl.PAct   = _accelerator.Allocate1D<double>(needed);
            _dbl.OutRInv = _accelerator.Allocate1D<double>(needed);
            _dbl.OutInv  = _accelerator.Allocate1D<double>(needed);
            _dbl.OutCum  = _accelerator.Allocate1D<double>(needed);
            _dbl.Capacity = needed;
        }

        private void EnsureFloatCapacity(int needed)
        {
            if (_flt == null) _flt = new FloatDeviceBuffers();
            if (_flt.Capacity >= needed) return;

            _flt.Dispose();
            _flt.Q      = _accelerator.Allocate1D<float>(needed);
            _flt.V      = _accelerator.Allocate1D<float>(needed);
            _flt.Inv    = _accelerator.Allocate1D<float>(needed);
            _flt.PiSelf = _accelerator.Allocate1D<float>(needed);
            _flt.PAct   = _accelerator.Allocate1D<float>(needed);
            _flt.OutRInv = _accelerator.Allocate1D<float>(needed);
            _flt.OutInv  = _accelerator.Allocate1D<float>(needed);
            _flt.OutCum  = _accelerator.Allocate1D<float>(needed);
            _flt.Capacity = needed;
        }

        private sealed class DoubleDeviceBuffers : IDisposable
        {
            public int Capacity;
            public MemoryBuffer1D<double, Stride1D.Dense> Q;
            public MemoryBuffer1D<double, Stride1D.Dense> V;
            public MemoryBuffer1D<double, Stride1D.Dense> Inv;
            public MemoryBuffer1D<double, Stride1D.Dense> PiSelf;
            public MemoryBuffer1D<double, Stride1D.Dense> PAct;
            public MemoryBuffer1D<double, Stride1D.Dense> OutRInv;
            public MemoryBuffer1D<double, Stride1D.Dense> OutInv;
            public MemoryBuffer1D<double, Stride1D.Dense> OutCum;

            public void Dispose()
            {
                Q?.Dispose(); V?.Dispose(); Inv?.Dispose(); PiSelf?.Dispose(); PAct?.Dispose();
                OutRInv?.Dispose(); OutInv?.Dispose(); OutCum?.Dispose();
                Q = V = Inv = PiSelf = PAct = OutRInv = OutInv = OutCum = null;
                Capacity = 0;
            }
        }

        private sealed class FloatDeviceBuffers : IDisposable
        {
            public int Capacity;
            public MemoryBuffer1D<float, Stride1D.Dense> Q;
            public MemoryBuffer1D<float, Stride1D.Dense> V;
            public MemoryBuffer1D<float, Stride1D.Dense> Inv;
            public MemoryBuffer1D<float, Stride1D.Dense> PiSelf;
            public MemoryBuffer1D<float, Stride1D.Dense> PAct;
            public MemoryBuffer1D<float, Stride1D.Dense> OutRInv;
            public MemoryBuffer1D<float, Stride1D.Dense> OutInv;
            public MemoryBuffer1D<float, Stride1D.Dense> OutCum;

            public void Dispose()
            {
                Q?.Dispose(); V?.Dispose(); Inv?.Dispose(); PiSelf?.Dispose(); PAct?.Dispose();
                OutRInv?.Dispose(); OutInv?.Dispose(); OutCum?.Dispose();
                Q = V = Inv = PiSelf = PAct = OutRInv = OutInv = OutCum = null;
                Capacity = 0;
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            try { _dbl?.Dispose(); } catch { }
            try { _flt?.Dispose(); } catch { }
            try { _accelerator?.Dispose(); } catch { }
            try { _context?.Dispose(); } catch { }
        }

        #endregion
    }
}
