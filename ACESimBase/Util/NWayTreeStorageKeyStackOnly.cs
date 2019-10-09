using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACESim.Util;

namespace ACESim
{
    public unsafe ref struct NWayTreeStorageKeyStackOnly
    {
        public byte PrefaceByte { get; set; }
        public Span<byte> Sequence;

        public byte Element(int i) => Sequence[i];

        public NWayTreeStorageKeyStackOnly(byte prefaceByte, byte* sequence)
        {
            // DEBUG -- SUPERDEBUG -- we must get rid of this so that we can make this data structure safe.
            PrefaceByte = prefaceByte;
            var DEBUG = Util.ListExtensions.GetPointerAsList_255Terminated(sequence);
            DEBUG.Add(255);
            Sequence = DEBUG.ToArray();
        }

        public NWayTreeStorageKeyStackOnly(byte prefaceByte, Span<byte> sequence)
        {
            PrefaceByte = prefaceByte;
            Sequence = sequence;
        }

        public override string ToString() => PrefaceByte + ": " + String.Join(",", Util.ListExtensions.GetPointerAsList_255Terminated(Sequence));

        public NWayTreeStorageKey ToStorable()
        {
            return new NWayTreeStorageKey(PrefaceByte, Util.ListExtensions.GetPointerAsList_255Terminated(Sequence).ToArray());
        }

        public NWayTreeStorageKey ToThreadOnlyKey()
        {
            // This is the key (pun intended) to making this storage efficient. We need arrays for the storage within the tree, but we reuse the thread-only key when we need to lookup, by copying the stack only key.
            int i = 0;
            byte[] targetSequence = NWayTreeStorageKey.KeyForThread.Sequence;
            do
            {
                targetSequence[i] = Sequence[i];
            }
            while (Sequence[i++] != 255); // Note: We include the 255 because the thread-only key may be longer than necessary
            NWayTreeStorageKey.KeyForThread = new NWayTreeStorageKey(PrefaceByte, targetSequence);
            return NWayTreeStorageKey.KeyForThread;
        }
    }
}
