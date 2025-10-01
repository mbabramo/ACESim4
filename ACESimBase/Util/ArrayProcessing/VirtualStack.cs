using System;

namespace ACESimBase.Util.ArrayProcessing
{
    /// <summary>
    /// Minimal virtual stack facade that mirrors the recorder's virtual stack state.
    /// Subsequent refactors can move allocation/reuse policy into this type.
    /// </summary>
    public sealed class VirtualStack
    {
        private readonly CommandRecorder _recorder;

        public VirtualStack(CommandRecorder recorder)
        {
            _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        }

        public VsIndex NextVs => new VsIndex(_recorder.NextArrayIndex);
        public VsIndex HighVs => new VsIndex(_recorder.MaxArrayIndex);
        public void AlignToAtLeast(VsIndex nextRequired) => AlignToAtLeast(nextRequired.Value);


        /// <summary>Gets the current top of the virtual stack.</summary>
        public int Next => _recorder.NextArrayIndex;

        /// <summary>Gets the high-water mark of the virtual stack.</summary>
        public int High => _recorder.MaxArrayIndex;

        /// <summary>
        /// Updates the high-water mark based on the current <see cref="Next"/>.
        /// </summary>
        public void TouchHighWater()
        {
            if (_recorder.NextArrayIndex > _recorder.MaxArrayIndex)
                _recorder.MaxArrayIndex = _recorder.NextArrayIndex;
        }

        /// <summary>
        /// Ensures the virtual stack top is at least the specified value and updates
        /// the high-water mark when needed. Intended for replay alignment.
        /// </summary>
        public void AlignToAtLeast(int nextRequired)
        {
            if (nextRequired > _recorder.NextArrayIndex)
                _recorder.NextArrayIndex = nextRequired;

            if (_recorder.NextArrayIndex > _recorder.MaxArrayIndex)
                _recorder.MaxArrayIndex = _recorder.NextArrayIndex;
        }
    }
}
