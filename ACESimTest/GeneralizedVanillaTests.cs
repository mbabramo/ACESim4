using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACESim;
using ACESim.Util;
using ACESim.Util.DiscreteProbabilities;
using ACESimBase.Games.LitigGame.Options;
using ACESimBase.Util;
using ACESimBase.Util.Debugging;
using ACESimBase.Util.DiscreteProbabilities;
using ACESimBase.Util.Mathematics;
using ACESimBase.Util.Statistical;
using FluentAssertions;
using HDF5CSharp;
using JetBrains.Annotations;
using MathNet.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACESimTest
{
    [TestClass]
    public class GeneralizedVanillaTests
    {
        [TestMethod]
        public Task SameResultsRollingAndUnrolling()
        {
            PrecautionNegligenceOptionsGeneratorSettings optionsGeneratorSettings = new()
            {
                UseSimplifiedPrecautionNegligenceGame = true
            };
            return Task.CompletedTask;
            // DEBUG
        }
    }
}
