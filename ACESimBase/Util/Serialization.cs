using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization;
using System.Xml;
using ACESim.Util;

namespace ACESim
{

    public static class ByteArrayConversions
    {
        // Convert an object to a byte array
        public static byte[] ObjectToByteArray(Object obj)
        {
            if (obj == null)
                return null;
            BinaryFormatter bf = new BinaryFormatter();
            MemoryStream ms = new MemoryStream();
            bf.Serialize(ms, obj);
            return ms.ToArray();
        }
        // Convert a byte array to an Object
        public static Object ByteArrayToObject(byte[] arrBytes)
        {
            MemoryStream memStream = new MemoryStream();
            BinaryFormatter binForm = new BinaryFormatter();
            memStream.Write(arrBytes, 0, arrBytes.Length);
            memStream.Seek(0, SeekOrigin.Begin);
            Object obj = (Object)binForm.Deserialize(memStream);
            return obj;
        }

    }

    public interface ISerializationPrep
    {
        void PreSerialize();
        void UndoPreSerialize();
    }

    public static class XMLSerialization
    {
        public static string SerializeToString<T>(T value)
        {

            if (value == null)
            {
                return null;
            }

            XmlSerializer serializer = new XmlSerializer(typeof(T));

            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Encoding = new UnicodeEncoding(false, false); // no BOM in a .NET string
            settings.Indent = false;
            settings.OmitXmlDeclaration = false;

            using (StringWriter textWriter = new StringWriter())
            {
                using (XmlWriter xmlWriter = XmlWriter.Create(textWriter, settings))
                {
                    serializer.Serialize(xmlWriter, value);
                }
                return textWriter.ToString();
            }
        }


        public static void SerializeObject(string filename, object theObject)
        {
            Type theType = theObject.GetType();
            Type innerType = null;
            if (theType == typeof(List<object>)) // I dont' think this works for a List of any other type other than object, i.e. List<Strategy>
            {
                innerType = ((List<object>)theObject).First().GetType();
                //theType = typeof(List<>).MakeGenericType(innerType);
            }
            //if (theObject is List<Strategy>)
            //{
            //    innerType = ((List<Strategy>)theObject).First().GetType();
            //    theType = typeof(List<>).MakeGenericType(innerType);
            //    //theType = typeof(List<object>);
            //}
            //if (theType.IsArray)
            //{
            //    // It's possible to have arrays of interfaces.  We want the actual type of the object
            //    innerType = (theObject as object[])[0].GetType();

            //    //theType = typeof(object[]); // This creates XML such that deserializing object[] gives xml nodes, not our typed objects.
            //    theType = typeof(object[]);
            //}

            XmlSerializer s;
            if (innerType == null)
                s = new XmlSerializer(theType);
            else
                s = new XmlSerializer(theType, new Type[] { innerType });
            TextWriter w = new StreamWriter(filename + ".xml");
            s.Serialize(w, theObject);
            w.Close();
        }

        public static object GetSerializedObject(string filename, Type objectType)
        {
            XmlSerializer s = new XmlSerializer(objectType); 
            TextReader r = new StreamReader(filename + ".xml");
            object theObject = s.Deserialize(r);
            r.Close();
            return theObject;
        }

        //public static object GetSerializedArray(string fileName, Type type)
        //{
        //    // All this is just to get strategyArrayType.  Making an ArrayList and calling .ToArray(type) was the only way I could find to get a dynamically typed Array
        //    // Also, I'm not sure why loading the XML as an Array of whatever type of strategy is even working, because presently it is serialized
        //    //  As a list<>.  Perhaps the two are interchangeable wrt Xml Serialization?
        //    System.Collections.ArrayList tempArrayList = new System.Collections.ArrayList();
        //    object tempTypedArray = tempArrayList.ToArray(type);
        //    Type arrayType = tempTypedArray.GetType();

        //    return GetSerializedObject(fileName, arrayType);
        //}
    }

    public static class FolderFinder
    {
        static Dictionary<string, DirectoryInfo> Locations = new Dictionary<string, DirectoryInfo>();

        private static DirectoryInfo TryGetSolutionDirectoryInfo(string currentPath = null)
        {
            var directory = new DirectoryInfo(
                currentPath ?? Directory.GetCurrentDirectory());
            while (directory != null && !directory.GetFiles("*.sln").Any())
            {
                directory = directory.Parent;
            }
            return directory;
        }

        private static DirectoryInfo GetTopDirectory(string currentPath = null)
        {
            var directory = new DirectoryInfo(
                currentPath ?? Directory.GetCurrentDirectory());
            while (directory.Parent != null)
            {
                directory = directory.Parent;
            }
            return directory;
        }

        public static DirectoryInfo GetFolderToWriteTo(string folderName)
        {
            lock (Locations)
                if (Locations.ContainsKey(folderName))
                    return Locations[folderName];
            DirectoryInfo containingDirectory = TryGetSolutionDirectoryInfo() ?? GetTopDirectory();
            DirectoryInfo result = (containingDirectory.GetDirectories(folderName)).FirstOrDefault();
            if (result == null)
                result = containingDirectory.CreateSubdirectory(folderName);
            lock (Locations)
                Locations[folderName] = result;
            return result;
        }
    }

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

    public static class BinarySerialization
    {
        public static void SerializeObject(string filename, object theObject, bool rewriteFile=true, bool preSerialize = true)
        {
            if (!rewriteFile)
            {
                if (File.Exists(filename))
                    return;
            }
            FileStream fs = new FileStream(filename, FileMode.Create);
            BinaryFormatter formatter = new BinaryFormatter();
            if (theObject is ISerializationPrep && preSerialize)
                ((ISerializationPrep)theObject).PreSerialize();
            formatter.Serialize(fs, theObject);
            fs.Close();
        }

        public static object GetSerializedObject(string filename, bool undoPreserialize = true)
        {
            FileStream fs = new FileStream(filename, FileMode.Open);
            BinaryFormatter formatter = new BinaryFormatter();
            object theObject = formatter.Deserialize(fs);
            if (theObject is ISerializationPrep && undoPreserialize)
                ((ISerializationPrep)theObject).UndoPreSerialize();
            fs.Close();
            return theObject;
        }

        public static byte[] GetByteArray(object theObject, bool preserialize=true)
        {
            MemoryStream ms = new MemoryStream();
            BinaryFormatter formatter = new BinaryFormatter();
            if (theObject is ISerializationPrep && preserialize)
                ((ISerializationPrep)theObject).PreSerialize();
            formatter.Serialize(ms, theObject);
            return ms.ToArray();
        }

        public static long GetSize(object theObject)
        {
            if (theObject == null)
                return 0;
            return GetByteArray(theObject).LongLength;
        }

        public static object GetObjectFromByteArray(byte[] theByteArray)
        {
            MemoryStream ms = new MemoryStream(theByteArray);
            BinaryFormatter formatter = new BinaryFormatter();
            object theObject = formatter.Deserialize(ms);
            if (theObject is ISerializationPrep)
                ((ISerializationPrep)theObject).UndoPreSerialize();
            return theObject;
        }

        public static int GetHashCodeFromByteArray(object theObject)
        {
            return GetByteArray(theObject).GetHashCode();
        }
    }

    public static class SerializeToString
    {
        public static string Serialize<T>(T objectToSerialize)
        {
            if (objectToSerialize == null)
                return "";

            BinaryFormatter bf = new BinaryFormatter();
            MemoryStream memStr = new MemoryStream();

            try
            {
                bf.Serialize(memStr, objectToSerialize);
                memStr.Position = 0;

                return Convert.ToBase64String(memStr.ToArray());
            }
            finally
            {
                memStr.Close();
            }
        }

    }
}
