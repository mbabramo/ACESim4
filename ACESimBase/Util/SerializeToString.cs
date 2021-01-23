using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace ACESim
{
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
