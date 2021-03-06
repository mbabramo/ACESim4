﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACESim.Util;

namespace ACESim
{
    public ref struct NWayTreeStorageKeyStackOnly
    {
        public byte PrefaceByte { get; set; }
        public Span<byte> Sequence;

        public byte Element(int i) => Sequence[i];

        public NWayTreeStorageKeyStackOnly(byte prefaceByte, Span<byte> sequence)
        {
            PrefaceByte = prefaceByte;
            Sequence = sequence;
        }

        public override string ToString() => PrefaceByte + ": " + String.Join(",", Util.ListExtensions.GetSpan255TerminatedAsList(Sequence));

        public NWayTreeStorageKey ToStorable()
        {
            return new NWayTreeStorageKey(PrefaceByte, Util.ListExtensions.GetSpan255TerminatedAsList(Sequence).ToArray());
        }

        public NWayTreeStorageKey ToThreadOnlyKey()
        {
            // This is the key (pun intended) to making this storage efficient. We need arrays for the storage within the tree, but we reuse the thread-only key when we need to lookup, by copying the stack only key.
            int i = 0;
            byte[] targetSequence = NWayTreeStorageKey.KeyForThread.Sequence;
            if (Sequence.Length > 0)
                do
                {
                    targetSequence[i] = Sequence[i];
                }
                while (Sequence[i++] != 255); // Note: We include the 255 because the thread-only key may be longer than necessary
            else
                targetSequence[0] = 255;
            NWayTreeStorageKey.KeyForThread = new NWayTreeStorageKey(PrefaceByte, targetSequence);
            return NWayTreeStorageKey.KeyForThread;
        }
    }
}
