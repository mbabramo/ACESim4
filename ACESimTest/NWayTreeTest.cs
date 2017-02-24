using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ACESim;

namespace ACESimTest
{
    [TestClass]
    public class NWayTreeTest
    {
        [TestMethod]
        public void NWayTreeWorks()
        {
            NWayTreeStorageInternal<float> f = new NWayTreeStorageInternal<float>(false, 5);
            List<byte> seq1 = new List<byte> { 2 };
            List<byte> branches1 = new List<byte> { 5 };
            f.AddValue(seq1.GetEnumerator(), branches1.GetEnumerator(), true, 1.0F);
            var v1 = f.GetValue(seq2.GetEnumerator());
            f = new NWayTreeStorageInternal<float>(false, 5);
            List<byte> seq2 = new List<byte> { 2, 3, 4 };
            List<byte> branches2 = new List<byte> { 5, 6, 7 };
            f.AddValue(seq2.GetEnumerator(), branches2.GetEnumerator(), true, 1.0F);
            f.AddValue(seq2.Take(2).GetEnumerator(), branches2.Take(2).GetEnumerator(), false, 2.0F);
            var v2 = f.GetValue(seq2.GetEnumerator());
            var v3 = f.GetValue(seq2.Take(2).GetEnumerator());
            f = new NWayTreeStorageInternal<float>(false, 5);
            for (int i = 0; i <= 3; i++)
                f.AddValue(seq2.Take(i).GetEnumerator(), branches2.Take(i).GetEnumerator(), i == 3, (float)i);

            for (int i = 0; i <= 3; i++)
                f.GetValue(seq2.Take(i).GetEnumerator()).Should().Be((float)i);
        }
    }
}
