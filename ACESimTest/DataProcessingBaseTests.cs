using FluentAssertions;
using LitigCharts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACESimTest
{
    [TestClass]
    [DoNotParallelize]
    public class DataProcessingBaseTests
    {
        [TestMethod]
        public void BlackAndWhiteOption_ConvertsEntireDispositionPalette()
        {
            const string latex = @"\definecolor{violet}{RGB}{100,0,100}
\providecolor{magenta}{RGB}{200,0,200}
\draw[violet] (0,0) -- (1,1);
\draw[magenta] (0,0) -- (1,1);
\draw[blue] (0,0) -- (1,1);
\draw[green] (0,0) -- (1,1);
\draw[yellow] (0,0) -- (1,1);
\draw[orange] (0,0) -- (1,1);
\draw[red] (0,0) -- (1,1);";
            bool previousSetting = DataProcessingBase.forceBlackAndWhiteForNonDarkLatexFiles;

            try
            {
                DataProcessingBase.forceBlackAndWhiteForNonDarkLatexFiles = true;

                string converted = DataProcessingBase.ApplyBlackAndWhiteOptionToLatex(
                    latex,
                    "Disposition.pdf");

                converted.Should().Contain(@"\definecolor{violet}");
                converted.Should().Contain(@"\providecolor{magenta}");
                converted.Should().NotContain(@"\draw[violet]");
                converted.Should().NotContain(@"\draw[magenta]");
                converted.Should().Contain(@"\draw[black]");
                converted.Split(@"\draw[black]").Should().HaveCount(8);
            }
            finally
            {
                DataProcessingBase.forceBlackAndWhiteForNonDarkLatexFiles = previousSetting;
            }
        }

        [TestMethod]
        public void BlackAndWhiteOption_PreservesDarkPresentationFiles()
        {
            const string latex = @"\draw[violet] (0,0) -- (1,1);
\draw[magenta] (0,0) -- (1,1);";
            bool previousSetting = DataProcessingBase.forceBlackAndWhiteForNonDarkLatexFiles;

            try
            {
                DataProcessingBase.forceBlackAndWhiteForNonDarkLatexFiles = true;

                DataProcessingBase.ApplyBlackAndWhiteOptionToLatex(
                    latex,
                    "Cost Breakdown (dark).pdf").Should().Be(latex);
            }
            finally
            {
                DataProcessingBase.forceBlackAndWhiteForNonDarkLatexFiles = previousSetting;
            }
        }
    }
}
