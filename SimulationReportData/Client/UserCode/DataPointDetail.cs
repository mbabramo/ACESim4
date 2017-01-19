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
    public partial class DataPointDetail
    {
        partial void DataPoint_Loaded(bool succeeded)
        {
            // Write your code here.
            this.SetDisplayNameFromEntity(this.DataPoint);
        }

        partial void DataPoint_Changed()
        {
            // Write your code here.
            this.SetDisplayNameFromEntity(this.DataPoint);
        }

        partial void DataPointDetail_Saved()
        {
            // Write your code here.
            this.SetDisplayNameFromEntity(this.DataPoint);
        }
    }
}