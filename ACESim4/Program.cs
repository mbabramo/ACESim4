using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACESim;
using System.Threading;
using System.Reflection;
using System.Security;
using System.Security.Permissions;
using System.IO;
using System.Runtime.Serialization;
using System.Diagnostics;
using ACESim.Util;

namespace ACESim
{
    class Program
    {
        static void Main(string[] args)
        {
            string baseOutputDirectory = "C:\\GitHub\\ACESim\\ACESim\\Games\\MyGame";
            string strategiesPath = Path.Combine(baseOutputDirectory, "Strategies");
            MyGameRunner.EvolveMyGame();
        }
    }
}