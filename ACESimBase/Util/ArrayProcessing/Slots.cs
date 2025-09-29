using System;

namespace ACESimBase.Util.ArrayProcessing.Slots
{
    public readonly record struct VsSlot(int Index);
    public readonly record struct OsPort(int OriginalIndex);
    public readonly record struct OdPort(int OriginalIndex);
    public readonly record struct ParamSlot(int Index);

    public sealed class ArraySlots
    {
        private readonly ArrayCommandList _acl;
        private readonly CommandRecorder _r;

        public ArraySlots(ArrayCommandList acl)
        {
            _acl = acl ?? throw new ArgumentNullException(nameof(acl));
            _r = _acl.Recorder ?? throw new InvalidOperationException("ACL recorder not initialized.");
        }

        // VS creation
        public VsSlot NewZero()           => new VsSlot(_r.NewZero());
        public VsSlot NewUninitialized()  => new VsSlot(_r.NewUninitialized());

        // Copies
        public VsSlot CopyToNew(VsSlot src)
            => new VsSlot(_r.CopyToNew(src.Index, fromOriginalSources: false));

        public void CopyTo(VsSlot dst, VsSlot src)
            => _r.CopyToExisting(dst.Index, src.Index);

        // Arithmetic
        public void Add(VsSlot dst, VsSlot by) => _r.Increment(dst.Index, targetOriginal: false, by.Index);
        public void Sub(VsSlot dst, VsSlot by) => _r.Decrement(dst.Index, by.Index);
        public void Mul(VsSlot dst, VsSlot by) => _r.MultiplyBy(dst.Index, by.Index);
        public void Zero(VsSlot dst)           => _r.ZeroExisting(dst.Index);

        // Ordered IO
        public VsSlot Read(OsPort port)
            => new VsSlot(_r.CopyToNew(port.OriginalIndex, fromOriginalSources: true));
            // recorder appends to OrderedSourceIndices and emits NextSource when ordered mode is active

        public void Accumulate(OdPort port, VsSlot value)
            => _r.Increment(port.OriginalIndex, targetOriginal: true, value.Index);
            // recorder appends to OrderedDestinationIndices and emits NextDestination when ordered mode is active

        // Parameters
        public ParamSlot StageParam(VsSlot src)
            => new ParamSlot(_r.CopyToNew(src.Index, fromOriginalSources: false));

        public ParamSlot StageParam(OsPort port)
            => new ParamSlot(_r.CopyToNew(port.OriginalIndex, fromOriginalSources: true));

        public VsSlot UseParam(ParamSlot p)
            => new VsSlot(_r.CopyToNew(p.Index, fromOriginalSources: false));

        public void Checkpoint(VsSlot src)
        {
            if (_acl.UseCheckpoints)
                _acl.CreateCheckpoint(src.Index); 
        }

        public VsSlot[] NewZeroArray(int count)
        {
            var a = new VsSlot[count];
            for (int i = 0; i < count; i++) a[i] = NewZero();
            return a;
        }
    }
}
