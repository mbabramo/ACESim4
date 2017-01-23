using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public class AllInventorsInfo
    {
        public InventorInfo Inventor01, Inventor02, Inventor03, Inventor04, Inventor05, Inventor06, Inventor07, Inventor08, Inventor09, Inventor10;

        public InventorInfo InventorToOptimize => Inventor01;

        public IEnumerable<InventorInfo> AllInventors()
        {
            yield return Inventor01;
            yield return Inventor02;
            yield return Inventor03;
            yield return Inventor04;
            yield return Inventor05;
            yield return Inventor06;
            yield return Inventor07;
            yield return Inventor08;
            yield return Inventor09;
            yield return Inventor10;
        }

        public IEnumerable<InventorInfo> InventorsNotBeingOptimized() => AllInventors().Skip(1);
    }
}
