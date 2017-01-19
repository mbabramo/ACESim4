using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public class SwapInputSeedsAttribute : Attribute
    {
        public string Name;

        public SwapInputSeedsAttribute(string name)
        {
            this.Name = name;
        }

    }
}
