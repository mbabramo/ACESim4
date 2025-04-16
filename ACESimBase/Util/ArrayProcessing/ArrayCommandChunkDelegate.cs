using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace ACESimBase.Util.ArrayProcessing
{
    // The delegate signature we want:
    public delegate void ArrayCommandChunkDelegate(
        double[] vs,
        double[] os,
        double[] od,
        ref int cosi,
        ref int codi
    );

    // A small helper to keep track of an "If" block
    internal struct IfBlockInfo
    {
        public Label SkipLabel; // Where to jump if condition fails
    }
}
