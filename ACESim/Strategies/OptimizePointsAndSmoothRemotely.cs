using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SeparateAppDomain;
using System.Threading;
using System.Diagnostics;
using System.IO;

namespace ACESim
{
    [Serializable]
    public class OptimizePointsAndSmoothRemotelyInfo : ISerializationPrep
    {
        public string OptimizerHash;
        [NonSerialized]
        public OptimizePointsAndSmooth Optimizer;
        [NonSerialized]
        public OptimizePointsAndSmooth OptimizerTemp;
        public Strategy.StrategyState StrategyState;
        public StrategySerializationInfo StrategySerializationInfo; // when this is non-null, StrategyContext will be set to null, and then can be recreated from the StrategySerializationInfo
        public int ChunkSize;
        public int DecisionNumber;
        public bool Find;
        public bool Optimize;

        public void SerializeStrategyContextToAzure()
        {
            StrategySerializationInfo = StrategyStateSerialization.SerializeStrategyStateToAzure(StrategyState, DecisionNumber, "inputblobs");
            StrategyState = null; // we'll load this back below
        }

        public void DeserializeStrategyContextFromAzure(Dictionary<string, Strategy> alreadyDeserializedStrategies, ref Tuple<string, OptimizePointsAndSmooth> lastOptimizePointsAndSmooth)
        {
            if (OptimizerHash == lastOptimizePointsAndSmooth.Item1)
                Optimizer = lastOptimizePointsAndSmooth.Item2;
            else
            {
                Optimizer = (OptimizePointsAndSmooth) AzureBlob.Download("inputblobs", "OPS" + OptimizerHash);
                UndoPreSerialize();
                lastOptimizePointsAndSmooth = new Tuple<string, OptimizePointsAndSmooth>(OptimizerHash, Optimizer);
            }
            StrategyState = StrategyStateSerialization.DeserializeStrategyStateFromAzure("inputblobs", StrategySerializationInfo, alreadyDeserializedStrategies, StrategyState);
            if (StrategyState.UnserializedStrategies[DecisionNumber] == null)
                StrategyState.UnserializedStrategies[DecisionNumber] = (Strategy)BinarySerialization.GetObjectFromByteArray(StrategyState.SerializedStrategies[DecisionNumber]);
            Optimizer.OverallStrategy = StrategyState.UnserializedStrategies[DecisionNumber];
        }

        public void PreSerialize()
        {
            if (Optimizer != null)
            {
                Optimizer.PreSerializeTemporarilyLimitingSize();
                OptimizerHash = StrategyStateSerialization.ComputeHashOfOptimizePointsAndSmooth(Optimizer);
                AzureBlob.UploadSerializableObject(Optimizer, "inputblobs", "OPS" + OptimizerHash, false);
                OptimizerTemp = Optimizer;
                Optimizer = null;
            }
        }

        public void UndoPreSerialize()
        {
            if (Optimizer == null && OptimizerTemp != null)
                Optimizer = OptimizerTemp;
            if (Optimizer != null)
                Optimizer.UndoPreSerializeTemporarilyLimitingSize();
        }

        public void RecoverState(ref Dictionary<string, Strategy> alreadyDeserializedStrategies, ref Tuple<string, OptimizePointsAndSmooth> lastOptimizePointsAndSmooth)
        {
            if (StrategySerializationInfo != null)
                DeserializeStrategyContextFromAzure(alreadyDeserializedStrategies, ref lastOptimizePointsAndSmooth);
            Optimizer.OverallStrategy.RecallStrategyState(StrategyState);
            if (StrategySerializationInfo != null)
                alreadyDeserializedStrategies = StrategyState.GetAlreadyDeserializedStrategies(StrategySerializationInfo);
        }
    }

    public static class OptimizePointsAndSmoothRemotely
    {
        public static void FindAndOrOptimize(object input, int taskSet, int index, CancellationToken ct, ref Dictionary<string, Strategy> alreadyDeserializedStrategies, ref Tuple<string, OptimizePointsAndSmooth> lastOptimizePointsAndSmooth, out object output)
        {
            TabbedText.EnableOutput = false;
            Parallelizer.DisableParallel = true;
            OptimizePointsAndSmoothRemotelyInfo info = (OptimizePointsAndSmoothRemotelyInfo)input;
            info.RecoverState(ref alreadyDeserializedStrategies, ref lastOptimizePointsAndSmooth);
            if (info.Find && info.Optimize)
            {
                OptimizePointsAndSmooth.OptimalValueResults[] chunkResult = info.Optimizer.IdentifyClosestPointsAndOptimizeChunk(((long)index) * ((long)info.ChunkSize), (long)info.ChunkSize, false, ct);
                output = chunkResult;
            }
            else if (info.Find && !info.Optimize)
            {
                int chunkResult = info.Optimizer.IdentifyClosestPointsAndSaveToAzure(taskSet, index, ((long)index) * ((long)info.ChunkSize), (long)info.ChunkSize, ct).Result;
                output = chunkResult;
            }
            else if (info.Optimize)
            {
                OptimizePointsAndSmooth.OptimalValueResults[] chunkResult = info.Optimizer.OptimizeSomeSmoothingPointsBasedOnIterationsSavedPreviouslyInAzure(taskSet, index, ct).Result;
                output = chunkResult;
            }
            else
                throw new Exception("Should not reach this point.");
        }
    }

    public class OptimizePointsAndSmoothInSeparateAppDomain : ProcessInSeparateAppDomainActionBase
    {
        static Tuple<string, OptimizePointsAndSmooth> lastOPSUsed = new Tuple<string, OptimizePointsAndSmooth>("N/A", null);
        public override void DoProcessing(object input, int index, CancellationToken ct, out object output)
        {
            Dictionary<string, Strategy> d = new Dictionary<string, Strategy>();
            OptimizePointsAndSmoothRemotely.FindAndOrOptimize(input, 0, index, ct, ref d, ref lastOPSUsed, out output);
        }
    }


    // This is a somewhat roundabout alternative to passing a Func, where we want to be able to pass an object with a function instead (because we are passing the object to worker roles)
    [Serializable]
    public class OptimizePointsAndSmoothRemoteCutoffExecutor : RemoteCutoffExecutorBase, ISerializationPrep
    {
        public string OptimizerHash;
        [NonSerialized]
        OptimizePointsAndSmooth Optimizer;
        [NonSerialized]
        public OptimizePointsAndSmooth OptimizerTemp;
        public Strategy.StrategyState StrategyState;
        public StrategySerializationInfo StrategySerializationInfo; // when this is non-null, StrategyContext will be set to null, and then can be recreated from the StrategySerializationInfo
        public int DecisionNumber;

        [NonSerialized]
        private Strategy OverallStrategyTemp;
        [NonSerialized]
        private OptimizePointsAndSmooth OriginalStateTemp;

        public OptimizePointsAndSmoothRemoteCutoffExecutor(OptimizePointsAndSmooth ops)
        {
            Optimizer = ops;
            DecisionNumber = ops.OverallStrategy.DecisionNumber;
            StrategyState = ops.OverallStrategy.RememberStrategyState();
        }

        public override StochasticCutoffFinderOutputs PlaySingleIterationIfNearEnoughCutoff(StochasticCutoffFinderInputs scfi, long iter)
        {
            return Optimizer.PlaySingleIterationIfNearEnoughCutoff(scfi, iter);
        }


        public override void RecoverState(ref Dictionary<string, Strategy> alreadyDeserializedStrategies, ref Tuple<string, OptimizePointsAndSmooth> lastOptimizePointsAndSmooth)
        {
            if (StrategySerializationInfo != null)
                DeserializeStrategyContextFromAzure(alreadyDeserializedStrategies, ref lastOptimizePointsAndSmooth);
            Optimizer.OverallStrategy.RecallStrategyState(StrategyState);
            if (StrategySerializationInfo != null)
                alreadyDeserializedStrategies = StrategyState.GetAlreadyDeserializedStrategies(StrategySerializationInfo);
        }

        public void PreSerialize()
        {
            if (Optimizer != null)
            {
                Optimizer.PreSerializeTemporarilyLimitingSize();
                OptimizerHash = StrategyStateSerialization.ComputeHashOfOptimizePointsAndSmooth(Optimizer);
                AzureBlob.UploadSerializableObject(Optimizer, "inputblobs", "OPS" + OptimizerHash, false);
                OptimizerTemp = Optimizer;
                Optimizer = null;
            }
        }

        public void UndoPreSerialize()
        {
            if (Optimizer == null && OptimizerTemp != null)
                Optimizer = OptimizerTemp;
            if (Optimizer != null)
                Optimizer.UndoPreSerializeTemporarilyLimitingSize();
        }

        public void SerializeStrategyContextToAzure()
        {
            StrategySerializationInfo = StrategyStateSerialization.SerializeStrategyStateToAzure(StrategyState, DecisionNumber, "inputblobs");
            StrategyState = null; // we'll load this back below
        }

        public void DeserializeStrategyContextFromAzure(Dictionary<string, Strategy> alreadyDeserializedStrategies, ref Tuple<string, OptimizePointsAndSmooth> lastOptimizePointsAndSmooth)
        {
            if (OptimizerHash == lastOptimizePointsAndSmooth.Item1)
                Optimizer = lastOptimizePointsAndSmooth.Item2;
            else
            {
                Optimizer = (OptimizePointsAndSmooth)AzureBlob.Download("inputblobs", "OPS" + OptimizerHash);
                UndoPreSerialize();
                lastOptimizePointsAndSmooth = new Tuple<string, OptimizePointsAndSmooth>(OptimizerHash, Optimizer);
            }
            StrategyState = StrategyStateSerialization.DeserializeStrategyStateFromAzure("inputblobs", StrategySerializationInfo, alreadyDeserializedStrategies, StrategyState);
            if (StrategyState.UnserializedStrategies[DecisionNumber] == null)
                StrategyState.UnserializedStrategies[DecisionNumber] = (Strategy)BinarySerialization.GetObjectFromByteArray(StrategyState.SerializedStrategies[DecisionNumber]);
            Optimizer.OverallStrategy = StrategyState.UnserializedStrategies[DecisionNumber];
        }
    }



}
