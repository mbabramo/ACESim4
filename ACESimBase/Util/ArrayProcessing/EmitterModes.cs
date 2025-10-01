// EmitterModes.cs
using System;
using System.Runtime.CompilerServices;

namespace ACESimBase.Util.ArrayProcessing
{
    public interface IEmissionMode
    {
        int  EmitCopyToNew(int sourceIndex, bool fromOriginalSources, CommandRecorder r, OrderedIoRecorder io);
        void EmitCopyToExisting(int index, int sourceIndex, bool isCheckpoint, CommandRecorder r, OrderedIoRecorder io);
        void EmitIncrement(int idx, bool targetOriginal, int incIdx, CommandRecorder r, OrderedIoRecorder io);
        void EmitZeroExisting(int index, CommandRecorder r);

        // VS -> VS operations (replay-aware)
        void EmitMultiplyBy(int index, int multIndex, CommandRecorder r);
        void EmitDecrement(int index, int decIndex, CommandRecorder r);

        // VS -> VS comparisons (replay-aware)
        void EmitEqualsOtherArrayIndex(int i1, int i2, CommandRecorder r);
        void EmitNotEqualsOtherArrayIndex(int i1, int i2, CommandRecorder r);
        void EmitGreaterThanOtherArrayIndex(int i1, int i2, CommandRecorder r);
        void EmitLessThanOtherArrayIndex(int i1, int i2, CommandRecorder r);

        // VS -> immediate comparisons (replay-aware)
        void EmitEqualsValue(int index, int value, CommandRecorder r);
        void EmitNotEqualsValue(int index, int value, CommandRecorder r);
    }

    internal sealed class RecordingMode : IEmissionMode
    {
        public static readonly RecordingMode Instance = new RecordingMode();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int EmitCopyToNew(int sourceIndex, bool fromOriginalSources, CommandRecorder r, OrderedIoRecorder io)
        {
            int fresh = r.NextArrayIndex++;
            if (fromOriginalSources && r.Acl.UseOrderedSourcesAndDestinations)
            {
                io.RecordSourceIndex(new OsIndex(sourceIndex));
                r.AddCommand(new ArrayCommand(ArrayCommandType.NextSource, fresh, -1));
            }
            else
            {
                r.AddCommand(new ArrayCommand(ArrayCommandType.CopyTo, fresh, sourceIndex));
            }
            return fresh;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EmitCopyToExisting(int index, int sourceIndex, bool isCheckpoint, CommandRecorder r, OrderedIoRecorder io)
        {
            if (isCheckpoint)
                r.AddCommand(new ArrayCommand(ArrayCommandType.Checkpoint, ArrayCommandList.CheckpointTrigger, sourceIndex));
            else
                r.AddCommand(new ArrayCommand(ArrayCommandType.CopyTo, index, sourceIndex));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EmitIncrement(int idx, bool targetOriginal, int incIdx, CommandRecorder r, OrderedIoRecorder io)
        {
            if (targetOriginal && r.Acl.UseOrderedSourcesAndDestinations)
            {
                io.RecordDestinationIndex(new OdIndex(idx));
                r.AddCommand(new ArrayCommand(ArrayCommandType.NextDestination, -1, incIdx));
            }
            else
            {
                r.AddCommand(new ArrayCommand(ArrayCommandType.IncrementBy, idx, incIdx));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EmitZeroExisting(int index, CommandRecorder r)
            => r.AddCommand(new ArrayCommand(ArrayCommandType.Zero, index, -1));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EmitMultiplyBy(int index, int multIndex, CommandRecorder r)
            => r.AddCommand(new ArrayCommand(ArrayCommandType.MultiplyBy, index, multIndex));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EmitDecrement(int index, int decIndex, CommandRecorder r)
            => r.AddCommand(new ArrayCommand(ArrayCommandType.DecrementBy, index, decIndex));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EmitEqualsOtherArrayIndex(int i1, int i2, CommandRecorder r)
            => r.AddCommand(new ArrayCommand(ArrayCommandType.EqualsOtherArrayIndex, i1, i2));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EmitNotEqualsOtherArrayIndex(int i1, int i2, CommandRecorder r)
            => r.AddCommand(new ArrayCommand(ArrayCommandType.NotEqualsOtherArrayIndex, i1, i2));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EmitGreaterThanOtherArrayIndex(int i1, int i2, CommandRecorder r)
            => r.AddCommand(new ArrayCommand(ArrayCommandType.GreaterThanOtherArrayIndex, i1, i2));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EmitLessThanOtherArrayIndex(int i1, int i2, CommandRecorder r)
            => r.AddCommand(new ArrayCommand(ArrayCommandType.LessThanOtherArrayIndex, i1, i2));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EmitEqualsValue(int index, int value, CommandRecorder r)
            => r.AddCommand(new ArrayCommand(ArrayCommandType.EqualsValue, index, value));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EmitNotEqualsValue(int index, int value, CommandRecorder r)
            => r.AddCommand(new ArrayCommand(ArrayCommandType.NotEqualsValue, index, value));
    }

    internal sealed class ReplayMode : IEmissionMode
    {
        public static readonly ReplayMode Instance = new ReplayMode();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int EmitCopyToNew(int sourceIndex, bool fromOriginalSources, CommandRecorder r, OrderedIoRecorder io)
        {
            var expected = r.Acl.UnderlyingCommands[r.NextCommandIndex];
            bool ns = expected.CommandType == ArrayCommandType.NextSource;
            bool ct = expected.CommandType == ArrayCommandType.CopyTo;
            if (!ns && !ct)
            {
                r.AddCommand(new ArrayCommand(
                    (fromOriginalSources && r.Acl.UseOrderedSourcesAndDestinations) ? ArrayCommandType.NextSource : ArrayCommandType.CopyTo,
                    expected.Index, ns ? -1 : sourceIndex));
            }

            int target = expected.Index;
            if (target + 1 > r.NextArrayIndex) r.NextArrayIndex = target + 1;

            if (ns && r.Acl.UseOrderedSourcesAndDestinations)
                io.RecordSourceIndex(new OsIndex(sourceIndex));

            r.AddCommand(expected);
            return target;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EmitCopyToExisting(int index, int sourceIndex, bool isCheckpoint, CommandRecorder r, OrderedIoRecorder io)
        {
            var expected = r.Acl.UnderlyingCommands[r.NextCommandIndex];
            int recordedTarget = expected.Index;
            if (recordedTarget + 1 > r.NextArrayIndex)
                r.NextArrayIndex = recordedTarget + 1;

            r.AddCommand(expected);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EmitIncrement(int idx, bool targetOriginal, int incIdx, CommandRecorder r, OrderedIoRecorder io)
        {
            var expected = r.Acl.UnderlyingCommands[r.NextCommandIndex];
            if (expected.CommandType == ArrayCommandType.NextDestination && r.Acl.UseOrderedSourcesAndDestinations)
                io.RecordDestinationIndex(new OdIndex(idx));

            r.AddCommand(expected);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EmitZeroExisting(int index, CommandRecorder r)
        {
            var expected = r.Acl.UnderlyingCommands[r.NextCommandIndex];
            r.AddCommand(new ArrayCommand(ArrayCommandType.Zero, expected.Index, -1));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EmitMultiplyBy(int index, int multIndex, CommandRecorder r)
        {
            var expected = r.Acl.UnderlyingCommands[r.NextCommandIndex];
            if (expected.Index >= 0)
                r.VS.AlignToAtLeast(expected.Index + 1);
            r.AddCommand(expected);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EmitDecrement(int index, int decIndex, CommandRecorder r)
        {
            var expected = r.Acl.UnderlyingCommands[r.NextCommandIndex];
            if (expected.Index >= 0)
                r.VS.AlignToAtLeast(expected.Index + 1);
            r.AddCommand(expected);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EmitEqualsOtherArrayIndex(int i1, int i2, CommandRecorder r)
        {
            var expected = r.Acl.UnderlyingCommands[r.NextCommandIndex];
            if (expected.Index >= 0)
                r.VS.AlignToAtLeast(expected.Index + 1);
            r.AddCommand(expected);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EmitNotEqualsOtherArrayIndex(int i1, int i2, CommandRecorder r)
        {
            var expected = r.Acl.UnderlyingCommands[r.NextCommandIndex];
            if (expected.Index >= 0)
                r.VS.AlignToAtLeast(expected.Index + 1);
            r.AddCommand(expected);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EmitGreaterThanOtherArrayIndex(int i1, int i2, CommandRecorder r)
        {
            var expected = r.Acl.UnderlyingCommands[r.NextCommandIndex];
            if (expected.Index >= 0)
                r.VS.AlignToAtLeast(expected.Index + 1);
            r.AddCommand(expected);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EmitLessThanOtherArrayIndex(int i1, int i2, CommandRecorder r)
        {
            var expected = r.Acl.UnderlyingCommands[r.NextCommandIndex];
            if (expected.Index >= 0)
                r.VS.AlignToAtLeast(expected.Index + 1);
            r.AddCommand(expected);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EmitEqualsValue(int index, int value, CommandRecorder r)
        {
            var expected = r.Acl.UnderlyingCommands[r.NextCommandIndex];
            if (expected.Index >= 0)
                r.VS.AlignToAtLeast(expected.Index + 1);
            r.AddCommand(expected);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EmitNotEqualsValue(int index, int value, CommandRecorder r)
        {
            var expected = r.Acl.UnderlyingCommands[r.NextCommandIndex];
            if (expected.Index >= 0)
                r.VS.AlignToAtLeast(expected.Index + 1);
            r.AddCommand(expected);
        }
    }
}
