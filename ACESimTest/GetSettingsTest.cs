using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using ACESim;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Xml.Linq;
using System.Xml;
using System.IO;

namespace ACESimTest
{
    public class SettingsTestClass
    {
        public double a;
        public double b;
    }

    [TestClass]
    public class GetSettingsTest
    {
        [TestMethod]
        public void TestMethod1()
        {
            string settingXml = @"
<setting name=""SettingsTestClass"" type=""class"" operator=""*"">
    <setting name=""a"" type=""double"">3.0</setting>
    <setting name=""b"" type=""double"">8.0</setting>
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
    }
}
