using System;
using System.Collections.Generic;
using System.Text;

namespace ACESimBase.Util
{
    public static class ThrowHelper
    {
        public static void Throw(string s = null)
        {
            throw new Exception(s);
        }
        public static void ThrowNotImplemented(string s = null)
        {
            throw new NotImplementedException(s);
        }
        public static void ThrowNotSupported(string s = null)
        {
            throw new NotSupportedException(s);
        }
    }
}
