using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace ACESim
{
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
}
