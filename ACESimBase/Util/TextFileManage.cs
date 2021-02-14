using System;
using System.IO;
using ACESim.Util;

namespace ACESim
{
    public static class TextFileManage
    {
        public static string[] GetLinesOfFile(string filename)
        {
            var filestream = new System.IO.FileStream(filename,
                                                      System.IO.FileMode.Open,
                                                      System.IO.FileAccess.Read,
                                                      System.IO.FileShare.ReadWrite);
            var streamreader = new System.IO.StreamReader(filestream, System.Text.Encoding.UTF8, true, 128);
            string[] lines = streamreader.ReadToEnd().Split(Environment.NewLine,
                              StringSplitOptions.RemoveEmptyEntries);
            return lines;
        }
        public static void CreateTextFile(string filename, string theText)
        {
            try
            {
               TextWriter w = new StreamWriter(filename);
                w.Write(theText);
                w.Close();
            }
            catch
            {
            }
        }

        public static void CopyFileFromAzure(string containerName, string azureFilename, string targetPath,  string targetFilenameIfDifferent = null, bool copyEmpty = false)
        {
            string text = AzureBlob.GetBlobText("results", azureFilename);

            if (copyEmpty || (text != null && text != ""))
                TextFileManage.CreateTextFile(Path.Combine(targetPath, targetFilenameIfDifferent ?? azureFilename), text);
        }
    }
}
