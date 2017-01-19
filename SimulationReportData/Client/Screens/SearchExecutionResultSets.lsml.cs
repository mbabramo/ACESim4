using System;
using System.Linq;
using System.IO;
using System.IO.IsolatedStorage;
using System.Collections.Generic;
using Microsoft.LightSwitch;
using Microsoft.LightSwitch.Framework.Client;
using Microsoft.LightSwitch.Presentation;
using Microsoft.LightSwitch.Presentation.Extensions;
namespace LightSwitchApplication
{
    public partial class SearchExecutionResultSets
    {
        partial void DeleteAll_Execute()
        {
            var all = this.ExecutionResultSets;
            foreach (var item in all)
                item.Delete();
        }
    }
}
