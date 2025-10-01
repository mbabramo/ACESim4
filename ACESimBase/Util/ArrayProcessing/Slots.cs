using System;

namespace ACESimBase.Util.ArrayProcessing.Slots
{
    public readonly record struct OsPort(OsIndex OriginalIndex)
    {
        public OsPort(int index) : this(new OsIndex(index)) { }
    }
    public readonly record struct OdPort(OdIndex OriginalIndex)
    {
        public OdPort(int index) : this(new OdIndex(index)) { }
    }
    public readonly record struct VsSlot(VsIndex Index)
    {
        public VsSlot(int index) : this(new VsIndex(index)) { }
    }
    public readonly record struct ParamSlot(VsIndex Index);

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
        public VsSlot NewZero()          => new(new VsIndex(_r.NewZero()));
        public VsSlot NewUninitialized() => new(new VsIndex(_r.NewUninitialized()));

        // Copies
        public VsSlot CopyToNew(VsSlot src)
            => new(new VsIndex(_r.CopyToNew(src.Index.Val(), fromOriginalSources: false)));

        public void CopyTo(VsSlot dst, VsSlot src)
            => _r.CopyToExisting(dst.Index, src.Index);

        // Arithmetic
        public void Add(VsSlot dst, VsSlot by) => _r.Increment(dst.Index, targetOriginal: false, by.Index);
        public void Sub(VsSlot dst, VsSlot by) => _r.Decrement(dst.Index, by.Index);
        public void Mul(VsSlot dst, VsSlot by) => _r.MultiplyBy(dst.Index, by.Index);
        public void Zero(VsSlot dst)           => _r.ZeroExisting(dst.Index.Val());

        // Ordered IO
        public VsSlot Read(OsPort port)
            => new(new VsIndex(_r.CopyToNew(port.OriginalIndex.Value, fromOriginalSources: true)));

        public void Accumulate(OdPort port, VsSlot value)
            => _r.Increment(port.OriginalIndex.Value, targetOriginal: true, value.Index.Value);

        // Parameters
        public ParamSlot StageParam(VsSlot src)
            => new(new VsIndex(_r.CopyToNew(src.Index.Value, fromOriginalSources: false)));

        public ParamSlot StageParam(OsPort port)
            => new(new VsIndex(_r.CopyToNew(port.OriginalIndex.Value, fromOriginalSources: true)));

        public VsSlot UseParam(ParamSlot p)
            => new(new VsIndex(_r.CopyToNew(p.Index.Value, fromOriginalSources: false)));


        public void Checkpoint(VsSlot src)
        {
            if (_acl.UseCheckpoints)
                _acl.CreateCheckpoint(src.Index.Val());
        }

        public VsSlot[] NewZeroArray(int count)
        {
            var a = new VsSlot[count];
            for (int i = 0; i < count; i++) a[i] = NewZero();
            return a;
        }
    }
}
