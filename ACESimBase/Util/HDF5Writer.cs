
using HDF5CSharp;
using System;
using System.Collections.Generic;
using System.IO;

namespace ACESimBase.Util
{

    public class HDF5Writer
    {
        public static void WriteArrayToFile(float[,] array, string filePath)
        {
            long fileId = Hdf5.CreateFile(filePath);

            using (var chunkedDataset = new ChunkedDataset<float>("/dataset", fileId, array))
            {
                // Optionally append more datasets if needed
                // chunkedDataset.AppendDataset(anotherArray);
            }

            Hdf5.CloseFile(fileId);
        }
    }
}
