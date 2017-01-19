using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    public class FileLoaderException : ApplicationException
    {
        public FileLoaderException(string message)
            : base(message)
        {
            //Nothing
        }

        public FileLoaderException(string message, Exception innerException)
            : base(message, innerException)
        {
            //Nothing
        }
    }
}
