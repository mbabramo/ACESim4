using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    class MyGameProgressInfo : GameProgress
    {
        // No customizations

        public override GameProgress DeepCopy()
        {
            MyGameProgressInfo copy = new MyGameProgressInfo();

            copy.GameComplete = this.GameComplete;
            base.CopyFieldInfo(copy);

            return copy;
        }
    }
}
