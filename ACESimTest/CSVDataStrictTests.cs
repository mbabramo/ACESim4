using ACESimBase.Util.Serialization;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Text;

namespace ACESimTest
{
    [TestClass]
    public class CSVDataStrictTests
    {
        [TestMethod]
        public void StrictSinglePass_RequiresExactlyOneSourceRow()
        {
            var criteria = new[] { new[] { ("OptionSet", "A"), ("Filter", "All") } };
            string[] columns = { "Trial" };

            using StreamReader exactReader = Reader("OptionSet,Filter,Trial\nA,All,0.25\n");
            CSVData.GetCSVData_SinglePassStrict(criteria, columns, exactReader)[0, 0].Should().Be(0.25);

            using StreamReader missingReader = Reader("OptionSet,Filter,Trial\nB,All,0.25\n");
            Action missing = () => CSVData.GetCSVData_SinglePassStrict(criteria, columns, missingReader);
            missing.Should().Throw<InvalidDataException>().WithMessage("*matches=0*");

            using StreamReader duplicateReader = Reader("OptionSet,Filter,Trial\nA,All,0.25\nA,All,0.30\n");
            Action duplicate = () => CSVData.GetCSVData_SinglePassStrict(criteria, columns, duplicateReader);
            duplicate.Should().Throw<InvalidDataException>().WithMessage("*matches=2*");
        }

        private static StreamReader Reader(string text) =>
            new(new MemoryStream(Encoding.UTF8.GetBytes(text)));
    }
}
