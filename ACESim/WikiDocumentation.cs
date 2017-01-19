using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Diagnostics = System.Diagnostics;
using Forms = System.Windows.Forms;
using Path = System.IO.Path;
using File = System.IO.File;
using IO = System.IO;
using System.Text.RegularExpressions;
using Xml = System.Xml;
using Xsl = System.Xml.Xsl;
using XPath = System.Xml.XPath;
using System.Diagnostics;


namespace ACESim
{
    static class WikiDocumentation
    {
        public static void TranslateXmlToWikiDocumentation()
        {
            const string UP = "..";

            string buildPath = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
            string projectDirectory = Path.GetFullPath(Path.Combine(buildPath, UP, UP, UP));
            string wikiDirectory = Path.Combine(projectDirectory, "WikiDocumentation");

            string xmlDocsPath = Path.Combine(buildPath, "ACESim.XML");
            string wikiXsltPath = Path.Combine(wikiDirectory, "wikidocs.xslt");
            string wikiOutputPath = Path.Combine(wikiDirectory, "ACESim.wiki.txt");

            // Find all badly formed XML and report: <!-- Badly formed XML comment ignored for member "M:ACESim.SettingsLoader.ProcessSettings(System.Xml.Linq.XElement,ACESim.SettingsSet)" -->
            IO.StreamReader xmlReader = new IO.StreamReader(xmlDocsPath);
            string xml = xmlReader.ReadToEnd();
            xmlReader.Close();
            Regex badlyFormedRe = new Regex("<!-- Badly formed XML comment ignored for member \"(.+)\" -->");
            MatchCollection badlyFormedMatches = badlyFormedRe.Matches(xml);
            foreach (Match match in badlyFormedMatches)
            {
                Debug.WriteLine(String.Format("The following item has a badly formed XML comment: {0}", match.Value));
            }

            //string wiki = TranslateXmlToWikiDocumenationUsingDotNet(xmlDocsPath, wikiXsltPath);
            string wiki = TranslateXmlToWikiDocumentationUsingCommandLineMSXML(xmlDocsPath, wikiXsltPath, wikiOutputPath);

            wiki = EscapePascal(wiki);

            try
            {
                using (IO.StreamWriter writer = new IO.StreamWriter(wikiOutputPath))
                {
                    writer.Write(wiki);
                }
            }
            catch (IO.IOException ex)
            {
                // Sometimes I get an error about a user-mapped portion of a file.  I have no idea why,
                //  but dont' hold up the show.
                Trace.WriteLine(String.Format("Error.  Failed to output wiki documentation: {0}", ex.Message));
            }
        }

        public static string TranslateXmlToWikiDocumenationUsingDotNet(
            string xmlDocsPath,
            string wikiXsltPath)
        {
            try
            {
                XPath.XPathDocument myXPathDocument = new XPath.XPathDocument(xmlDocsPath);
                Xsl.XslTransform myXslTransform = new Xsl.XslTransform();
                myXslTransform.Load(wikiXsltPath);
                StringBuilder builder = new StringBuilder();
                using (Xml.XmlReader reader = myXslTransform.Transform(myXPathDocument, null))
                {
                    while (reader.Read())
                    {
                        builder.AppendLine(reader.ReadOuterXml());
                    }
                }
	            return builder.ToString();

                //return reader.ToString();
                //Xml.XmlDocument doc = new Xml.XmlDocument();
                //doc.Load(reader);
                //using (writer = new Xml.XmlTextWriter(wikiOutputPath, null))
                //{
                //  Xml.XmlReader reader = myXslTransform.Transform(myXPathDocument, null, writer);
                //}
                //IO.StreamReader stream = new IO.StreamReader(wikiOutputPath);
                //return stream.ReadToEnd();
            }
            catch (Exception e)
            {
                throw;
            }
        }

        /// <summary>
        /// Translates the XML documentation output by the build process into wiki documentation
        /// 
        /// msxsl.exe "c:\projects\Myapp\debug\bin\MyApp.xml" c:\dev\wikidocs.xslt -o "c:\projects\Myapp\debug\bin\WikiDocs.txt"
        /// 
        /// </summary>
        public static string TranslateXmlToWikiDocumentationUsingCommandLineMSXML(
            string xmlDocsPath,
            string wikiXsltPath,
            string wikiOutputPath)
        {

            string wikiDirectory = Path.GetDirectoryName(wikiXsltPath);
            string msxslPath = Path.Combine(wikiDirectory, "msxsl.exe");

            //string commandFormat = "\"{0}\" \"{1}\" \"{2}\" -o \"{3}\"";
            //string[] commandFormatParameters = new string[] { msxslPath, xmlDocsPath, wikiXsltPath, wikiOutputPath };
            //string command = String.Format(commandFormat, commandFormatParameters);

            string argumentsFormat = "\"{0}\" \"{1}\" -o \"{2}\"";
            string[] argumentsFormatParameters = new string[] { xmlDocsPath, wikiXsltPath, wikiOutputPath };
            string arguments = String.Format(argumentsFormat, argumentsFormatParameters);

            // These args were supposed to skip writing to an intermediate file (currently the same path eventually written to again)
            // but the process just hung.
            //string argumentsFormat = "\"{0}\" \"{1}\"";
            //string[] argumentsFormatParameters = new string[] { xmlDocsPath, wikiXsltPath };
            //string arguments = String.Format(argumentsFormat, argumentsFormatParameters);

            Diagnostics.ProcessStartInfo startInfo = new Diagnostics.ProcessStartInfo(msxslPath);
            //startInfo.WindowStyle = ProcessWindowStyle.Minimized;
            startInfo.Arguments = arguments;
            startInfo.ErrorDialog = false;
            startInfo.CreateNoWindow = true; 
            startInfo.UseShellExecute = false; // Required to redirect output
            startInfo.RedirectStandardError = true;
            startInfo.RedirectStandardOutput = true;

            Diagnostics.Process process = Diagnostics.Process.Start(startInfo);

            string error = null;
            using (IO.StreamReader errorReader = process.StandardError)
            {
                error = errorReader.ReadToEnd();
            }
            if (!String.IsNullOrEmpty(error))
            {
                Forms.MessageBox.Show(
                    error,
                    "WikiDocumentation Output",
                    Forms.MessageBoxButtons.OK,
                    Forms.MessageBoxIcon.Error);
            }

            string output = null;
            using (IO.StreamReader outputReader = process.StandardOutput)
            {
                output = outputReader.ReadToEnd();
            }

            if (!String.IsNullOrEmpty(output))
            {
                Forms.MessageBox.Show(
                    output,
                    "WikiDocumentation Output",
                    Forms.MessageBoxButtons.OK,
                    Forms.MessageBoxIcon.Information);
            }
            string result = null;
            using (IO.StreamReader reader = File.OpenText(wikiOutputPath))
            {
                result = reader.ReadToEnd();
            }

            //if (String.IsNullOrEmpty(output))
            //{
            //    Forms.MessageBox.Show(
            //        "MSXML returned no output.",
            //        "WikiDocumentation Output",
            //        Forms.MessageBoxButtons.OK,
            //        Forms.MessageBoxIcon.Information);
            //}
            //string result = output;

            return result;
        }

        /// <summary>
        /// Prefixes all PascalCase substrings in toEscape with "!".  This stops
        /// automatic wiki linking in the wiki engine we're using.
        /// </summary>
        /// <param name="toEscape"></param>
        /// <returns></returns>
        public static string EscapePascal(string toEscape)
        {
            //IO.FileStream stream = File.Open(filePath, IO.FileMode.Open);

            //string contents = "";
            //using(IO.StreamReader reader = File.OpenText(filePath))
            //{
            //    contents = reader.ReadToEnd();
            //}
            MatchEvaluator evaluator = new MatchEvaluator(PascalCaseMatchEvaluator);
            Regex pascalCaseWordRe = new Regex(@"\b[A-Z][a-z]+[A-Z][a-z]\w*");
            return pascalCaseWordRe.Replace(toEscape, evaluator);

            //Regex re = new Regex(@"\W");
            //IEnumerable<string> tokens = new List<string>(re.Split(toEscape)).Distinct();

            //foreach(string token in tokens)
            //{
            //    if (IsPascalCaseInTracWiki(token))
            //    {
            //        toEscape = toEscape.Replace(token, String.Format("!{0}", token));
            //    }
            //}

            //return toEscape;

            //using (IO.StreamWriter writer = new IO.StreamWriter(filePath))
            //{
            //    writer.Write(contents);
            //}
        }

        public static string PascalCaseMatchEvaluator(Match match)
        {
            return String.Format("!{0}", match.Value);
        }


        /// <summary>
        /// The Wiki used by Trac doesn't think something with consecutive capitals is PascalCase, e.g.
        /// </summary>
        /// <param name="aString"></param>
        /// <returns></returns>
        public static bool IsPascalCaseInTracWiki(string aString)
        {
            if (
                aString.Length > 1 &&
                char.IsUpper(aString[0]) &&
                char.IsLower(aString[1])
                )
            {
                bool seenAnotherUpperCase = false;

                // Check after the first character, so 1, and don't check the last char because it doesn't have a next char, so Length-1
                IEnumerable<int> checkRange = Enumerable.Range(1, aString.Length - 1);
                foreach (int charIndex in checkRange)
                {
                    char otherChar = aString[charIndex];
                    if (char.IsUpper(otherChar))
                    {
                        seenAnotherUpperCase = true;
                        char nextChar = aString[charIndex + 1];
                        if (!char.IsLower(nextChar))
                        {
                            return false;
                        }
                    }
                }
                return seenAnotherUpperCase;
            }
            else
            {
                return false;
            }
        }
    }
}
