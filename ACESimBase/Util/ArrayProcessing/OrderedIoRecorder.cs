using System.Runtime.CompilerServices;

namespace ACESimBase.Util.ArrayProcessing
{
    public sealed class OrderedIoRecorder
    {
        private readonly ArrayCommandList _acl;
        public OrderedIoRecorder(ArrayCommandList acl) => _acl = acl;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordSourceIndex(OsIndex originalIndex) => _acl.OrderedSourceIndices.Add(originalIndex);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordDestinationIndex(OdIndex originalIndex) => _acl.OrderedDestinationIndices.Add(originalIndex);
    }
}
