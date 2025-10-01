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
    }

    internal sealed class RecordingMode : IEmissionMode
    {
        public static readonly RecordingMode Instance = new RecordingMode();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int EmitCopyToNew(int sourceIndex, bool fromOriginalSources, CommandRecorder r, OrderedIoRecorder io)
        {
            // Same behavior as your current recording path: emit NextSource + record sources
            // when OS/OD is enabled; otherwise emit CopyTo. :contentReference[oaicite:0]{index=0}
            int fresh = r.NextArrayIndex++;
            if (fromOriginalSources && r.Acl.UseOrderedSourcesAndDestinations)
            {
                io.RecordSourceIndex(sourceIndex);
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
            // Same behavior as your current *recording* branch for CopyToExisting. :contentReference[oaicite:1]{index=1}
            if (isCheckpoint)
                r.AddCommand(new ArrayCommand(ArrayCommandType.Checkpoint, ArrayCommandList.CheckpointTrigger, sourceIndex));
            else
                r.AddCommand(new ArrayCommand(ArrayCommandType.CopyTo, index, sourceIndex));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EmitIncrement(int idx, bool targetOriginal, int incIdx, CommandRecorder r, OrderedIoRecorder io)
        {
            // Route to NextDestination + record destinations when OS/OD is enabled; else IncrementBy. :contentReference[oaicite:2]{index=2}
            if (targetOriginal && r.Acl.UseOrderedSourcesAndDestinations)
            {
                io.RecordDestinationIndex(idx);
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
    }

    internal sealed class ReplayMode : IEmissionMode
    {
        public static readonly ReplayMode Instance = new ReplayMode();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int EmitCopyToNew(int sourceIndex, bool fromOriginalSources, CommandRecorder r, OrderedIoRecorder io)
        {
            // Accept recorded NextSource *or* CopyTo (your current replay logic). :contentReference[oaicite:3]{index=3}
            var expected = r.Acl.UnderlyingCommands[r.NextCommandIndex];
            bool ns = expected.CommandType == ArrayCommandType.NextSource;
            bool ct = expected.CommandType == ArrayCommandType.CopyTo;
            if (!ns && !ct)
            {
                // Let AddCommand produce the mismatch diagnostics.
                r.AddCommand(new ArrayCommand(
                    (fromOriginalSources && r.Acl.UseOrderedSourcesAndDestinations) ? ArrayCommandType.NextSource : ArrayCommandType.CopyTo,
                    expected.Index, ns ? -1 : sourceIndex));
                // The call above throws on mismatch via AddCommand.
            }

            int target = expected.Index;
            if (target + 1 > r.NextArrayIndex) r.NextArrayIndex = target + 1;  // keep VS high-water mark aligned

            if (ns && r.Acl.UseOrderedSourcesAndDestinations)
                io.RecordSourceIndex(sourceIndex);

            // Re-emit the *recorded* opcode to remain byte-identical. :contentReference[oaicite:4]{index=4}
            r.AddCommand(expected);
            return target;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EmitCopyToExisting(int index, int sourceIndex, bool isCheckpoint, CommandRecorder r, OrderedIoRecorder io)
        {
            // Look at the recorded instruction we are about to replay.
            var expected = r.Acl.UnderlyingCommands[r.NextCommandIndex];

            // Keep the recorder’s VS high‑water mark aligned to the recorded target.
            // This mirrors the behavior already present today and avoids drifting NextArrayIndex.
            int recordedTarget = expected.Index;
            if (recordedTarget + 1 > r.NextArrayIndex)
                r.NextArrayIndex = recordedTarget + 1;

            // Re‑emit the *recorded* instruction verbatim so the stream remains byte‑identical.
            // (Do not substitute the caller’s sourceIndex here; the recorded SourceIndex is authoritative.)
            r.AddCommand(expected);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EmitIncrement(int idx, bool targetOriginal, int incIdx, CommandRecorder r, OrderedIoRecorder io)
        {
            // During replay, recorded shape may be NextDestination or IncrementBy;
            // if it is NextDestination, mirror the ordered-destinations side-effect. :contentReference[oaicite:7]{index=7}
            var expected = r.Acl.UnderlyingCommands[r.NextCommandIndex];
            if (expected.CommandType == ArrayCommandType.NextDestination && r.Acl.UseOrderedSourcesAndDestinations)
                io.RecordDestinationIndex(idx);

            r.AddCommand(expected); // lets AddCommand verify the opcode matches
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EmitZeroExisting(int index, CommandRecorder r)
        {
            // Your current replay branch for ZeroExisting reproduces the recorded target. :contentReference[oaicite:8]{index=8}
            var expected = r.Acl.UnderlyingCommands[r.NextCommandIndex];
            r.AddCommand(new ArrayCommand(ArrayCommandType.Zero, expected.Index, -1));
        }
    }
}
