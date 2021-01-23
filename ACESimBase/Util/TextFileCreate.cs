using System.IO;
using ACESim.Util;

namespace ACESim
{
    public static class TextFileCreate
    {
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
                TextFileCreate.CreateTextFile(Path.Combine(targetPath, targetFilenameIfDifferent ?? azureFilename), text);
        }
    }
}
