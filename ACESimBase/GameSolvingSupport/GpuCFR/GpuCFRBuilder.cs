// ACESimBase/GameSolvingSupport/GpuCFR/GpuCFRBuilder.cs
using System;
using System.Collections.Generic;
using ILGPU;
using ILGPU.Runtime;
using ACESimBase.GameSolvingSupport.GameTree;
using ACESimBase.GameSolvingSupport.Settings;

namespace ACESimBase.GameSolvingSupport.GpuCFR
{
    /// <summary>
    /// CFR builder that collects per-action updates during traversal and computes
    /// regret/avg-strategy increments on the GPU via ILGPU. If a CUDA GPU is not
    /// available, ILGPU falls back to the CPU accelerator automatically.
    /// </summary>
    public sealed class GpuCFRBuilder : IDisposable
    {
        #region Public surface (unchanged)

        public sealed class RootNode
        {
            private readonly GpuCFRBuilder _owner;
            internal RootNode(GpuCFRBuilder owner) => _owner = owner;

            public NodeResult Go(ref IterationContext ctx)
            {
                // Reset per-sweep buffers.
                _owner._pending.Clear();

                // Initialize reach vectors (current-policy and average-policy streams).
                var pi = new double[_owner.NumNonChancePlayers];
                var avgPi = new double[_owner.NumNonChancePlayers];
                for (byte p = 0; p < _owner.NumNonChancePlayers; p++)
                {
                    pi[p] = 1.0;
                    avgPi[p] = 1.0;
                }

                var hp = _owner._rootFactory();
                var u = _owner.Visit(in hp, ref ctx, pi, avgPi);
                return new NodeResult(u);
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
        public bool IsAvailable { get; }
        public byte NumNonChancePlayers { get; }

        public GpuCFRBuilder(
            HistoryNavigationInfo navigation,
            Func<HistoryPoint> rootFactory,
            bool useFloat,
            GpuCFRBuilderOptions options)
        {
            _navigation = navigation; // may be non-null in your env
            _rootFactory = rootFactory ?? throw new ArgumentNullException(nameof(rootFactory));
            _useFloatPreference = useFloat;
            _options = options ?? new GpuCFRBuilderOptions();
            NumNonChancePlayers = _options.NumNonChancePlayers;

            // Build a context with default devices and Algorithms enabled.
            // This wires up ILGPU.Algorithms properly.
            _context = Context.Create(builder => builder.Default().EnableAlgorithms());

            // Prefer a real GPU; fall back to FastCFR (IsAvailable=false) if none.
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
                // No CUDA/OpenCL device detected -> let outer code fall back to Fast flavor.
                IsAvailable = false;
                Root = new RootNode(this);
                throw new Exception("CUDA not found");
                //return;
            }

            _accelerator = found;

            // Precision choice: keep it simple; honor user preference.
            _useDeviceFloat = _useFloatPreference;

            // Compile kernels once.
            LoadKernels();

            Root = new RootNode(this);
            IsAvailable = true;
        }


        public IterationContext InitializeIteration(byte optimizedPlayerIndex, int scenarioIndex, Func<int, double> rand01ForDecision)
        {
            // Nothing to freeze here; policies are read directly from nodes during traversal.
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

        /// <summary>
        /// Launches the ILGPU kernel over all pending per-action updates and applies
        /// the results to backing information-set nodes. Clears the pending buffer.
        /// </summary>
        public void CopyTalliesIntoBackingNodes()
        {
            int n = _pending.Count;
            if (n == 0)
                return;

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

        // ILGPU
        private readonly Context _context;
        private readonly Accelerator _accelerator;
        private bool _useDeviceFloat;

        private Action<Index1D,
            ArrayView<double>, ArrayView<double>, ArrayView<double>, ArrayView<double>, ArrayView<double>,
            ArrayView<double>, ArrayView<double>, ArrayView<double>> _kernelDouble;

        private Action<Index1D,
            ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<float>,
            ArrayView<float>, ArrayView<float>, ArrayView<float>> _kernelFloat;

        private struct PendingUpdate
        {
            public InformationSetNode Node;
            public byte ActionOneBased;
            public double Q;         // action value for optimized player
            public double V;         // state value for optimized player under current policy
            public double InversePi; // product of opponent & chance reach
            public double PiSelf;    // reach of optimized player (clipped)
            public double PAction;   // current policy prob for the action
        }

        private readonly List<PendingUpdate> _pending = new List<PendingUpdate>(capacity: 1 << 14);

        #endregion

        #region Traversal (CPU) — collects per-action updates for GPU

        private double[] Visit(in HistoryPoint hp, ref IterationContext ctx, double[] pi, double[] avgPi)
        {
            var state = hp.GetGameStatePrerecorded(_navigation) ?? _navigation.GetGameState(in hp);

            if (state is FinalUtilitiesNode fu)
            {
                var u = new double[NumNonChancePlayers];
                for (int i = 0; i < NumNonChancePlayers; i++)
                    u[i] = fu.Utilities[i];
                return u;
            }
            else if (state is ChanceNode cn)
            {
                return VisitChance(in hp, cn, ref ctx, pi, avgPi);
            }
            else // InformationSetNode
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

                var nextPi = new double[NumNonChancePlayers];
                var nextAvgPi = new double[NumNonChancePlayers];
                GetNextPiValues(pi,     ctx.OptimizedPlayerIndex, p, changeOtherPlayers: true,  dest: nextPi);
                GetNextPiValues(avgPi,  ctx.OptimizedPlayerIndex, p, changeOtherPlayers: true,  dest: nextAvgPi);

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

            return expected;
        }

        private double[] VisitDecision(in HistoryPoint hp, InformationSetNode isn, ref IterationContext ctx, double[] pi, double[] avgPi)
        {
            byte decisionIndex = isn.DecisionIndex;
            var decision = _navigation.GameDefinition.DecisionsExecutionOrder[decisionIndex];
            byte numActions = decision.NumPossibleActions;
            byte playerAtNode = isn.PlayerIndex;

            var pAction = new double[numActions];
            if (decision.AlwaysDoAction is byte always)
            {
                SetAlwaysAction(numActions, pAction, always);
            }
            else
            {
                bool opponentProbabilities = playerAtNode != ctx.OptimizedPlayerIndex;
                isn.GetCurrentProbabilities(pAction, opponentProbabilities);
            }

            var expectedOptimizedForAction = new double[numActions];
            double expectedOptimized = 0.0;
            var expected = new double[NumNonChancePlayers];

            double inversePi = GetInversePiValue(pi, ctx.OptimizedPlayerIndex);

            for (byte a = 1; a <= numActions; a++)
            {
                double prob = pAction[a - 1];
                if (prob <= 0.0 && playerAtNode != ctx.OptimizedPlayerIndex)
                    continue; // skip zero-prob opponent branches

                double avgProb = isn.GetAverageStrategy(a);

                var nextPi = new double[NumNonChancePlayers];
                var nextAvgPi = new double[NumNonChancePlayers];
                GetNextPiValues(pi,     playerAtNode, prob,    changeOtherPlayers: false, dest: nextPi);
                GetNextPiValues(avgPi,  playerAtNode, avgProb, changeOtherPlayers: false, dest: nextAvgPi);

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

            if (playerAtNode == ctx.OptimizedPlayerIndex)
            {
                for (byte a = 1; a <= numActions; a++)
                    expectedOptimized += pAction[a - 1] * expectedOptimizedForAction[a - 1];

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
            // Pack host arrays from pending list
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

            using var dq = _accelerator.Allocate1D(q);
            using var dv = _accelerator.Allocate1D(v);
            using var dinv = _accelerator.Allocate1D(invPi);
            using var dpiSelf = _accelerator.Allocate1D(piSelf);
            using var dpAct = _accelerator.Allocate1D(pAction);

            using var doutRInvPi = _accelerator.Allocate1D<double>(n);
            using var doutInvPi = _accelerator.Allocate1D<double>(n);
            using var doutCumInc = _accelerator.Allocate1D<double>(n);

            _kernelDouble(n, dq.View, dv.View, dinv.View, dpiSelf.View, dpAct.View,
                          doutRInvPi.View, doutInvPi.View, doutCumInc.View);
            _accelerator.Synchronize();

            var hRInvPi = doutRInvPi.GetAsArray1D();
            var hInvPi = doutInvPi.GetAsArray1D();
            var hCumInc = doutCumInc.GetAsArray1D();

            // Apply to backing nodes
            for (int i = 0; i < n; i++)
            {
                var rec = _pending[i];
                rec.Node.IncrementLastRegret(rec.ActionOneBased, hRInvPi[i], hInvPi[i]);
                rec.Node.IncrementLastCumulativeStrategyIncrements(rec.ActionOneBased, hCumInc[i]);
            }
        }

        private void RunKernelFloat(int n)
        {
            // Pack host arrays from pending list (cast to float)
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

            using var dq = _accelerator.Allocate1D(q);
            using var dv = _accelerator.Allocate1D(v);
            using var dinv = _accelerator.Allocate1D(invPi);
            using var dpiSelf = _accelerator.Allocate1D(piSelf);
            using var dpAct = _accelerator.Allocate1D(pAction);

            using var doutRInvPi = _accelerator.Allocate1D<float>(n);
            using var doutInvPi = _accelerator.Allocate1D<float>(n);
            using var doutCumInc = _accelerator.Allocate1D<float>(n);

            _kernelFloat(n, dq.View, dv.View, dinv.View, dpiSelf.View, dpAct.View,
                         doutRInvPi.View, doutInvPi.View, doutCumInc.View);
            _accelerator.Synchronize();

            var hRInvPi = doutRInvPi.GetAsArray1D();
            var hInvPi = doutInvPi.GetAsArray1D();
            var hCumInc = doutCumInc.GetAsArray1D();

            // Apply to backing nodes
            for (int i = 0; i < n; i++)
            {
                var rec = _pending[i];
                rec.Node.IncrementLastRegret(rec.ActionOneBased, hRInvPi[i], hInvPi[i]);
                rec.Node.IncrementLastCumulativeStrategyIncrements(rec.ActionOneBased, hCumInc[i]);
            }
        }

        #endregion

        #region Helpers

        private static void SetAlwaysAction(byte numActions, double[] dest, byte actionOneBased)
        {
            for (int i = 0; i < numActions; i++) dest[i] = 0.0;
            dest[actionOneBased - 1] = 1.0;
        }

        private static double GetInversePiValue(double[] pi, byte playerIndex)
        {
            double product = 1.0;
            for (byte p = 0; p < pi.Length; p++)
                if (p != playerIndex)
                    product *= pi[p];
            return product;
        }

        private static void GetNextPiValues(double[] current, byte playerIndex, double prob, bool changeOtherPlayers, double[] dest)
        {
            for (byte p = 0; p < current.Length; p++)
            {
                double cur = current[p];
                if (p == playerIndex)
                    dest[p] = changeOtherPlayers ? cur : cur * prob;
                else
                    dest[p] = changeOtherPlayers ? cur * prob : cur;
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            try
            {
                _accelerator?.Dispose();
            }
            catch { /* ignore */ }
            try
            {
                _context?.Dispose();
            }
            catch { /* ignore */ }
        }

        #endregion
    }
}
