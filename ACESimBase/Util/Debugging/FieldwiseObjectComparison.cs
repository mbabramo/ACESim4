using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Util.Debugging
{
    public class FieldwiseComparisonSkipAttribute : Attribute
    {
    }

    public static class FieldwiseObjectComparison
    {
        public static bool AreEqual(object obj1, object obj2, bool throwIfUnequal = false, bool reportIfUnequal = false)
        {
            if (reportIfUnequal)
            {
                bool result = AreEqualHelper(obj1, obj2, false, false, new List<object>());
                if (throwIfUnequal && !result)
                    AreEqualHelper(obj1, obj2, throwIfUnequal, true, new List<object>()); // Report on the problem and then throw
                return result;
            }
            else
                return AreEqualHelper(obj1, obj2, throwIfUnequal, false, new List<object>());
        }

        private static object lockObj = new object();

        private static bool AreEqualHelper(object obj1, object obj2, bool throwIfUnequal, bool provideReport, List<object> objectsAlreadyProcessed)
        {
            string textToReport = "";
            bool returnVal = AreEqualHelper2(obj1, obj2, throwIfUnequal, provideReport, new List<string>(), "", ref textToReport, 0, objectsAlreadyProcessed);
            if (textToReport != "" && provideReport)
            {
                lock (lockObj)
                { // only write one report at a time
                    System.Diagnostics.Debug.WriteLine(textToReport);
                }
            }
            if (!returnVal && throwIfUnequal)
                throw new Exception("Objects supposed to be equal were not equal.");
            return returnVal;
        }

        private static void IndentString(ref string theString, int level)
        {
            string prefix = "";
            for (int l = 0; l < level; l++)
                prefix += "   ";
            theString = prefix + theString;
        }

        private static bool AreEqualHelper2(object obj1, object obj2, bool throwIfUnequal, bool provideReport, List<string> fieldNamesList, string preText, ref string textToReport, int level, List<object> objectsAlreadyProcessed)
        {
            bool reportOnlyFailedPath = false;

            bool returnVal = true;

            string textToAddToReport = "";

            if (obj1 == null || obj2 == null)
            {
                if (throwIfUnequal)
                    throw new Exception("At least one of the objects to be compared is null.");
                return false;
            }

            if (level > 30)
            {
                throw new Exception("The tree depth is too great for comparison.");
            }

            if (objectsAlreadyProcessed.Any(x => ReferenceEquals(x, obj1) && ReferenceEquals(x, obj2)))
                return true; // we've already processed this
            objectsAlreadyProcessed.Add(obj1);
            objectsAlreadyProcessed.Add(obj2);

            if (obj1.GetType() == obj2.GetType())
            {
                if (provideReport && level == 0)
                {
                    string lineOfText = obj1.GetType().ToString() + "\n";
                    IndentString(ref lineOfText, level);
                    textToAddToReport += lineOfText;
                }

                FieldInfo[] fields = obj1.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                foreach (FieldInfo field in fields)
                {
                    if (field.GetCustomAttribute(typeof(FieldwiseComparisonSkipAttribute)) != null)
                        continue;

                    List<string> deeperFieldNamesList = fieldNamesList.Concat(new List<string> { field.Name }).ToList();
                    string fieldName = field.Name;

                    object fieldVal1 = field.GetValue(obj1);
                    object fieldVal2 = field.GetValue(obj2);
                    //if (fieldVal1 == null && fieldVal2 == null)
                    //    ;
                    //else if (field.FieldType.IsArray)
                    //{
                    //    Array array1 = fieldVal1 as Array;
                    //    Array array2 = fieldVal2 as Array;
                    //    int length1 = array1.Length;
                    //    int length2 = array2.Length;
                    //    if (length1 != length2)
                    //    {
                    //        string lineOfText = preText + "Array length incompatibility (" + length1 + " vs. " + length2 + ")\n";
                    //        IndentString(ref lineOfText, level);
                    //        textToAddToReport += lineOfText;
                    //        returnVal = false;
                    //        break;
                    //    }
                    //    else
                    //    {
                    //        IEnumerator array1Enum = array1.GetEnumerator();
                    //        IEnumerator array2Enum = array2.GetEnumerator();
                    //        int index = 0;
                    //        while (returnVal && array1Enum.MoveNext() && array2Enum.MoveNext())
                    //        {
                    //            returnVal = AreEqualHelper2(array1Enum.Current, array2Enum.Current, throwIfUnequal, provideReport, fieldName + "[" + index + "]" + ": ", ref textToAddToReport, level + 1, objectsAlreadyProcessed);
                    //            index++;
                    //        }
                    //        if (returnVal == false)
                    //        {
                    //            bool canPutBreakpointHere = true;
                    //        }
                    //    }
                    //}
                    //else
                    if (fieldVal1 is IEnumerable)
                    {
                        IEnumerable enumer1 = (IEnumerable)fieldVal1;
                        IEnumerable enumer2 = (IEnumerable)fieldVal2;
                        int length1 = 0;
                        IEnumerator enumer1a = ((IEnumerable)fieldVal1).GetEnumerator();
                        while (enumer1a.MoveNext())
                            length1++;
                        int length2 = 0;
                        IEnumerator enumer2a = ((IEnumerable)fieldVal1).GetEnumerator();
                        while (enumer2a.MoveNext())
                            length2++;
                        if (length1 != length2)
                        {
                            string lineOfText = preText + "Array length incompatibility (" + length1 + " vs. " + length2 + ")\n";
                            IndentString(ref lineOfText, level);
                            textToAddToReport += lineOfText;
                            returnVal = false;
                            break;
                        }
                        else
                        {
                            IEnumerator array1Enum = enumer1.GetEnumerator();
                            IEnumerator array2Enum = enumer2.GetEnumerator();
                            int index = 0;
                            while (returnVal && array1Enum.MoveNext() && array2Enum.MoveNext())
                            {
                                if (array1Enum.Current == null && array2Enum.Current == null)
                                {
                                    // this is fine -- nothing to do
                                }
                                else if (array1Enum.Current == null || array2Enum.Current == null)
                                {
                                    string lineOfText = preText + "One of compared objects is null, other isn't\n";
                                    IndentString(ref lineOfText, level);
                                    textToAddToReport += lineOfText;
                                    returnVal = false;
                                }
                                else
                                {
                                    // Neither is null
                                    List<string> deeperFieldNamesList2 =
                                        deeperFieldNamesList
                                            .Concat(new List<string> { array1Enum.Current.GetType().ToString() })
                                            .ToList();
                                    returnVal = AreEqualHelper2(array1Enum.Current, array2Enum.Current, throwIfUnequal,
                                        provideReport, deeperFieldNamesList2, fieldName + "[" + index + "]" + ": ",
                                        ref textToAddToReport, level + 1, objectsAlreadyProcessed);
                                }
                                index++;
                            }
                            if (returnVal == false)
                            {
                                //bool canPutBreakpointHere = true;
                            }
                        }
                    }
                    else if (field.FieldType.IsClass)
                    {
                        if (!AreEqualHelper2(fieldVal1, fieldVal2, throwIfUnequal, provideReport, deeperFieldNamesList, fieldName + ": ", ref textToAddToReport, level + 1, objectsAlreadyProcessed))
                        {
                            returnVal = false;
                            break;
                        }
                    }
                    else if (!fieldVal1.Equals(fieldVal2))
                    {
                        string lineOfText = preText + fieldVal1.ToString() + " != " + fieldVal2.ToString() + "\n";
                        IndentString(ref lineOfText, level);
                        textToAddToReport += lineOfText;
                        returnVal = false;
                        break;
                    }
                    else
                    {

                        string lineOfText = preText + fieldVal1.ToString() + "\n";
                        IndentString(ref lineOfText, level);
                        textToAddToReport += lineOfText;
                    }
                }
            }
            else
                returnVal = false;

            if (!reportOnlyFailedPath || !returnVal)
                textToReport = textToAddToReport + textToReport;

            return returnVal;
        }
    }
}
