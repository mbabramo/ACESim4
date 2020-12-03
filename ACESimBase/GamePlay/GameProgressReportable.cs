using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections;
using System.Collections.Concurrent;

namespace ACESim
{
    [Serializable]
    public class GameProgressReportable
    {
        [NonSerialized]
		[System.Xml.Serialization.XmlIgnore]
        internal static ConcurrentDictionary<Type, FieldInfo[]> FieldInfosStorage = new ConcurrentDictionary<Type, FieldInfo[]>();
        internal FieldInfo[] FieldInfos
        {
            get
            {
                Type theType = this.GetType();
                if (!FieldInfosStorage.ContainsKey(theType))
                {
                    var fields = theType.GetFields();
                    FieldInfosStorage.TryAdd(theType, fields); // if it doesn't work, don't worry about it
                    return fields;
                }
                return FieldInfosStorage[theType];
            }
        }

        public object GetValueForReport(string variableNameForReport, int? listIndex, out bool found)
        {
            object result = GetFieldValueForReport(variableNameForReport, listIndex, out found);
            if (!found)
                result = GetNonFieldValueForReport(variableNameForReport, out found);
            return result;
        }

        public virtual object GetNonFieldValueForReport(string variableNameForReport, out bool found)
        {
            found = false;
            return null;
        }

        string lastVariableName;
        FieldInfo lastFieldInfo;
        public virtual object GetFieldValueForReport(string variableNameForReport, int? listIndex, out bool found)
        {
            FieldInfo fieldInfo; 
            //if (variableNameForReport == lastVariableName)
            //    fieldInfo = lastFieldInfo;
            //else
            //{
                fieldInfo = FieldInfos.FirstOrDefault(x => x.Name == variableNameForReport);
                lastFieldInfo = fieldInfo;
                lastVariableName = variableNameForReport;
            //}
            if (fieldInfo == null)
            {
                found = false;
                return null;
            }
            found = true;
            if (listIndex == null)
                return fieldInfo.GetValue(this);
            else
            {
                IList theList = (IList)fieldInfo.GetValue(this);
                if (theList == null || (int)listIndex > theList.Count - 1)
                    return null;
                object indexObject = theList[(int)listIndex];
                return indexObject;
            }
        }
    }
}
