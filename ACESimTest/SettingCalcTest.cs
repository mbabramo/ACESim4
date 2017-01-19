using ACESim;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Xml;
using System.IO;

namespace ACESimTest
{
    
    
    /// <summary>
    ///This is a test class for SettingCalcTest and is intended
    ///to contain all SettingCalcTest Unit Tests
    ///</summary>
    [TestClass()]
    public class SettingCalcTest
    {


        private TestContext testContextInstance;

        /// <summary>
        ///Gets or sets the test context which provides
        ///information about and functionality for the current test run.
        ///</summary>
        public TestContext TestContext
        {
            get
            {
                return testContextInstance;
            }
            set
            {
                testContextInstance = value;
            }
        }

        #region Additional test attributes
        // 
        //You can use the following additional attributes as you write your tests:
        //
        //Use ClassInitialize to run code before running the first test in the class
        //[ClassInitialize()]
        //public static void MyClassInitialize(TestContext testContext)
        //{
        //}
        //
        //Use ClassCleanup to run code after all tests in a class have run
        //[ClassCleanup()]
        //public static void MyClassCleanup()
        //{
        //}
        //
        //Use TestInitialize to run code before running each test
        //[TestInitialize()]
        //public void MyTestInitialize()
        //{
        //}
        //
        //Use TestCleanup to run code after each test has run
        //[TestCleanup()]
        //public void MyTestCleanup()
        //{
        //}
        //
        #endregion


        /// <summary>
        ///A test for GetValue
        ///</summary>
        [TestMethod()]
        public void GetValue_BinaryIntegerMultiplicationTest()
        {
            string settingXml = @"
<setting name=""StartingPopulationSize"" type=""calc"" operator=""*"">
    <setting name=""n/a"" type=""int"">3</setting>
    <setting name=""n/a"" type=""int"">8</setting>
</setting>";
            StringReader reader = new StringReader(settingXml);
            XDocument theSettingsFile = XDocument.Load(reader);
            XElement element = theSettingsFile.Element("setting");
            SettingsLoader settingsLoader = new SettingsLoader(String.Empty);
            SettingCalc setting = settingsLoader.ProcessSetting(element, null) as SettingCalc;
            List<double> inputs = null;
            int expected = 24;

            int actual = (int)setting.GetValue(inputs, null);

            Assert.AreEqual(expected, actual);
        }

        /// <summary>
        ///A test for GetValue
        ///</summary>
        [TestMethod()]
        public void GetValue_TernaryIntegerMultiplicationTest()
        {
            string settingXml = @"
<setting name=""StartingPopulationSize"" type=""calc"" operator=""*"">
    <setting name=""n/a"" type=""int"">7</setting>
    <setting name=""n/a"" type=""int"">9</setting>
    <setting name=""n/a"" type=""int"">10</setting>
</setting>";
            StringReader reader = new StringReader(settingXml);
            XDocument theSettingsFile = XDocument.Load(reader);
            XElement element = theSettingsFile.Element("setting");
            SettingsLoader settingsLoader = new SettingsLoader(String.Empty);
            SettingCalc setting = settingsLoader.ProcessSetting(element, null) as SettingCalc;
            List<double> inputs = null;
            int expected = 630;

            int actual = (int)setting.GetValue(inputs, null);

            Assert.AreEqual(expected, actual);
        }

        /// <summary>
        ///A test for GetValue
        ///</summary>
        [TestMethod()]
        public void GetValue_BinaryFloatMultiplicationTest()
        {
            string settingXml = @"
<setting name=""StartingPopulationSize"" type=""calc"" operator=""*"">
    <setting name=""n/a"" type=""double"">2.4</setting>
    <setting name=""n/a"" type=""double"">12.9</setting>
</setting>";
            StringReader reader = new StringReader(settingXml);
            XDocument theSettingsFile = XDocument.Load(reader);
            XElement element = theSettingsFile.Element("setting");
            SettingsLoader settingsLoader = new SettingsLoader(String.Empty);
            SettingCalc setting = settingsLoader.ProcessSetting(element, null) as SettingCalc;
            List<double> inputs = null;
            double expected = 30.96;

            double actual = (double)setting.GetValue(inputs, null);

            Assert.AreEqual(expected, actual, 1e-5);
        }

        /// <summary>
        ///A test for GetValue
        ///</summary>
        [TestMethod()]
        public void GetValue_TernaryFloatMultiplicationTest()
        {
            string settingXml = @"
<setting name=""StartingPopulationSize"" type=""calc"" operator=""*"">
    <setting name=""n/a"" type=""double"">8.42</setting>
    <setting name=""n/a"" type=""double"">10.0</setting>
    <setting name=""n/a"" type=""double"">-3.99</setting>
</setting>";
            StringReader reader = new StringReader(settingXml);
            XDocument theSettingsFile = XDocument.Load(reader);
            XElement element = theSettingsFile.Element("setting");
            SettingsLoader settingsLoader = new SettingsLoader(String.Empty);
            SettingCalc setting = settingsLoader.ProcessSetting(element, null) as SettingCalc;
            List<double> inputs = null;
            double expected = -335.958;

            double actual = (double)setting.GetValue(inputs, null);

            Assert.AreEqual(expected, actual, 1e-3);
        }

        /// <summary>
        ///A test for GetValue
        ///</summary>
        [TestMethod()]
        public void GetValue_BinaryIntegerAdditionTest()
        {
            string settingXml = @"
<setting name=""StartingPopulationSize"" type=""calc"" operator=""+"">
    <setting name=""n/a"" type=""int"">17</setting>
    <setting name=""n/a"" type=""int"">56</setting>
</setting>";
            StringReader reader = new StringReader(settingXml);
            XDocument theSettingsFile = XDocument.Load(reader);
            XElement element = theSettingsFile.Element("setting");
            SettingsLoader settingsLoader = new SettingsLoader(String.Empty);
            SettingCalc setting = settingsLoader.ProcessSetting(element, null) as SettingCalc;
            List<double> inputs = null;
            int expected = 73;

            int actual = (int)setting.GetValue(inputs, null);

            Assert.AreEqual(expected, actual);
        }

        /// <summary>
        ///A test for GetValue
        ///</summary>
        [TestMethod()]
        public void GetValue_TernaryIntegerAdditionTest()
        {
            string settingXml = @"
<setting name=""StartingPopulationSize"" type=""calc"" operator=""+"">
    <setting name=""n/a"" type=""int"">-128</setting>
    <setting name=""n/a"" type=""int"">56</setting>
    <setting name=""n/a"" type=""int"">149</setting>
</setting>";
            StringReader reader = new StringReader(settingXml);
            XDocument theSettingsFile = XDocument.Load(reader);
            XElement element = theSettingsFile.Element("setting");
            SettingsLoader settingsLoader = new SettingsLoader(String.Empty);
            SettingCalc setting = settingsLoader.ProcessSetting(element, null) as SettingCalc;
            List<double> inputs = null;
            int expected = 77;

            int actual = (int)setting.GetValue(inputs, null);

            Assert.AreEqual(expected, actual);
        }

        /// <summary>
        ///A test for GetValue
        ///</summary>
        [TestMethod()]
        public void GetValue_BinaryFloatAdditionTest()
        {
            string settingXml = @"
<setting name=""StartingPopulationSize"" type=""calc"" operator=""+"">
    <setting name=""n/a"" type=""double"">42.0</setting>
    <setting name=""n/a"" type=""double"">87.5</setting>
</setting>";
            StringReader reader = new StringReader(settingXml);
            XDocument theSettingsFile = XDocument.Load(reader);
            XElement element = theSettingsFile.Element("setting");
            SettingsLoader settingsLoader = new SettingsLoader(String.Empty);
            SettingCalc setting = settingsLoader.ProcessSetting(element, null) as SettingCalc;
            List<double> inputs = null;
            double expected = 129.5;

            double actual = (double)setting.GetValue(inputs, null);

            Assert.AreEqual(expected, actual);
        }

        /// <summary>
        ///A test for GetValue
        ///</summary>
        [TestMethod()]
        public void GetValue_SubCalcTest()
        {
            string settingXml = @"
<setting name=""n/a"" type=""calc"" operator=""+"">
    <setting name=""n/a"" type=""double"">42.0</setting>
    <setting name=""n/a"" type=""double"">87.5</setting>
    <setting name=""n/a"" type=""calc"" operator=""*"">
        <setting name=""n/a"" type=""double"">2</setting>
        <setting name=""n/a"" type=""double"">4</setting>
    </setting>
</setting>";
            StringReader reader = new StringReader(settingXml);
            XDocument theSettingsFile = XDocument.Load(reader);
            XElement element = theSettingsFile.Element("setting");
            SettingsLoader settingsLoader = new SettingsLoader(String.Empty);
            SettingCalc setting = settingsLoader.ProcessSetting(element, null) as SettingCalc;
            List<double> inputs = null;
            double expected = 137.5;

            double actual = (double)setting.GetValue(inputs, null);

            Assert.AreEqual(expected, actual);
        }
    }
}
