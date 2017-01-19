using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    public class OptionalSettingAttribute : Attribute
    {
        public override string ToString()
        {
            return "OptionalSetting";
        }
    }

    public class InternallyDefinedSetting : Attribute
    {
        public override string ToString()
        {
            return "SkipSetting";
        }
    }
}
