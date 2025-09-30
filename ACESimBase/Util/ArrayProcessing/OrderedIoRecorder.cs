// OrderedIoRecorder.cs
using System.Runtime.CompilerServices;

namespace ACESimBase.Util.ArrayProcessing
{
    public sealed class OrderedIoRecorder
    {
        private readonly ArrayCommandList _acl;
        public OrderedIoRecorder(ArrayCommandList acl) => _acl = acl;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordSourceIndex(int originalIndex) => _acl.OrderedSourceIndices.Add(originalIndex);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordDestinationIndex(int originalIndex) => _acl.OrderedDestinationIndices.Add(originalIndex);
    }
}
