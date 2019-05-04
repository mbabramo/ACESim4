using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.Util
{
    public static class StringUtil
    {
        public static List<int> AllIndexesOf(this string str, string value)
        {
            if (String.IsNullOrEmpty(value))
                throw new ArgumentException("the string to find may not be empty", "value");
            List<int> indexes = new List<int>();
            for (int index = 0; ; index += value.Length)
            {
                index = str.IndexOf(value, index);
                if (index == -1)
                    return indexes;
                indexes.Add(index);
            }
        }

        public static List<(int indexOfStart, int indexOfEndOfWord)> AllIndexRangesOfPrefix(this string str, string prefix)
        {
            List<int> wordStarts = AllIndexesOf(str, prefix);
            List<(int indexOfStart, int indexOfEndOfWord)> l = new List<(int indexOfStart, int indexOfEndOfWord)>();
            foreach (int start in wordStarts)
            {
                int spaceIndex = str.IndexOf(' ', start);
                int carriageReturn = str.IndexOf('\r', start);
                if (carriageReturn != -1)
                    spaceIndex = Math.Min(spaceIndex, carriageReturn);
                int lineFeed = str.IndexOf('\n', start);
                if (lineFeed != -1)
                    spaceIndex = Math.Min(spaceIndex, lineFeed);
                l.Add(spaceIndex == -1 ? (start, str.Length - 1) : (start, spaceIndex - 1));
            }
            return l;
        }

        public static string ReplaceWordsBeginningWithPrefix(this string str, string prefix, Func<string, string> replacementFn)
        {
            var ranges = AllIndexRangesOfPrefix(str, prefix);
            StringBuilder b = new StringBuilder();
            if (ranges.Any())
            {
                int stringIndex = 0;
                for (int rangeIndex = 0; rangeIndex < ranges.Count; rangeIndex++)
                {
                    var range = ranges[rangeIndex];
                    if (stringIndex < range.indexOfStart)
                        b.Append(str.Substring(stringIndex, range.indexOfStart - stringIndex));
                    string stringToReplace = str.Substring(range.indexOfStart, range.indexOfEndOfWord - range.indexOfStart + 1);
                    string replacement = replacementFn(stringToReplace);
                    b.Append(replacement);
                    stringIndex = range.indexOfEndOfWord + 1;
                }
            }
            return b.ToString();
        }

        public static string ReplaceArrayDesignationWithArrayItem(this string str, double[] array)
        {
            return ReplaceWordsBeginningWithPrefix(str, "ARRAY", x =>
            {
                string numericString = x.Substring(5);
                if (numericString[numericString.Length - 1] == '*')
                    numericString = numericString.Substring(0, numericString.Length - 1);
                int i = Convert.ToInt32(numericString);
                return array[i].ToString();
            });
        }
    }
}
