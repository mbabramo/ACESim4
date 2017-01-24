using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public class AllInventorsInfo
    {
        public InventorInfo Inventor00, Inventor01, Inventor02, Inventor03, Inventor04, Inventor05, Inventor06, Inventor07, Inventor08, Inventor09;

        public int NumPotentialInventors => 10;

        public InventorInfo InventorToOptimize => Inventor00;

        public InventorInfo Inventor(int i)
        {
            switch (i)
            {
                case 0:
                    return Inventor00;
                case 1:
                    return Inventor01;
                case 2:
                    return Inventor02;
                case 3:
                    return Inventor03;
                case 4:
                    return Inventor04;
                case 5:
                    return Inventor05;
                case 6:
                    return Inventor06;
                case 7:
                    return Inventor07;
                case 8:
                    return Inventor08;
                case 9:
                    return Inventor09;
                default:
                    throw new Exception();
            }
        }

        public IEnumerable<InventorInfo> AllInventors()
        {
            yield return Inventor00;
            yield return Inventor01;
            yield return Inventor02;
            yield return Inventor03;
            yield return Inventor04;
            yield return Inventor05;
            yield return Inventor06;
            yield return Inventor07;
            yield return Inventor08;
            yield return Inventor09;
        }

        public IEnumerable<InventorInfo> InventorsNotBeingOptimized() => AllInventors().Skip(1);
    }
}
