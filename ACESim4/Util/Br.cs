using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// This is a simple static class that is useful for debugging. 
// Suppose we want to stop at point B but only after point A executes.
// Then, at point A, we insert code or an action breakpoint: B.reak.Add("A"),
// and at point B, we insert B.reak.On("A"). 
// Or a more complex pattern -- stop when we get to B for the second time after A.
//if (B.reak.Contains("A"))
//{
//    B.reak.Add("B");
//    B.reak.After("B", 2);
//}

namespace Br
{
#pragma warning disable IDE1006 // Naming Styles
    public static class eak
#pragma warning restore IDE1006 // Naming Styles
    {
        public static bool Active => true; // set to false to disable this mechanism
        public static Dictionary<string, int> d = new Dictionary<string, int>();
        public static void Add(string s)
        {
            if (!d.ContainsKey(s))
                d[s] = 1;
            else
                d[s] = d[s] + 1;
        }
        public static void Remove(string s)
        {
            if (d.ContainsKey(s))
                d.Remove(s);
        }
        public static bool Contains(IEnumerable<string> s) => s.All(x => d.ContainsKey(x));
        public static bool Contains(string s) => d.ContainsKey(s);
        public static void IfAdded(string s)
        {
            if (Contains(s) && Active && System.Diagnostics.Debugger.IsAttached)
                System.Diagnostics.Debugger.Break();
        }
        public static void IfAddedAtIteration(string s, int iteration)
        {
            if (IsExactly(s, iteration) && Active && System.Diagnostics.Debugger.IsAttached)
                System.Diagnostics.Debugger.Break();
        }

        public static void IfAddedAtLeastIteration(string s, int iteration)
        {
            if (IsAtLeast(s, iteration) && Active && System.Diagnostics.Debugger.IsAttached)
                System.Diagnostics.Debugger.Break();
        }

        public static bool IsExactly(string s, int iteration)
        {
            return Contains(s) && d[s] == iteration;
        }

        public static bool IsAtLeast(string s, int iteration)
        {
            return Contains(s) && d[s] >= iteration;
        }
    }
}
