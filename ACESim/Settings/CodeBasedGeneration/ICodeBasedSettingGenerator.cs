using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    public interface ICodeBasedSettingGenerator
    {
        object GenerateSetting(string options);
    }
}
