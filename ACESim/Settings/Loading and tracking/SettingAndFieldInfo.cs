using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;

namespace ACESim
{
    [Serializable]
    public class SettingAndFieldInfo
    {
        public int level;
        public Setting setting;
        public FieldInfo fieldInfo;
        public SettingAndFieldInfo parentSettingAndFieldInfoForList;
        public object itemsForList; // This will be a List<T>, but because T is not known at compile time, we must create it using Activator.
        public Type type;

        public SettingAndFieldInfo(Setting theSetting, FieldInfo theFieldInfo, int theLevel)
        { // This is a field, rather than a list.
            setting = theSetting;
            fieldInfo = theFieldInfo;
            if (theSetting is SettingClass && ((SettingClass)theSetting).SubclassType != null)
                type = ((SettingClass)theSetting).SubclassType;
            else
                type = fieldInfo.FieldType;
            level = theLevel;
        }

        public SettingAndFieldInfo(Setting theSetting, SettingAndFieldInfo theParent, Type theType)
        { // This is a setting in a list.
            setting = theSetting;
            parentSettingAndFieldInfoForList = theParent;
            type = theType;
            level = theParent == null ? 0 : theParent.level + 1;
        }

        public void SetValue(ref object objectToSet, object theValue)
        {
            if (fieldInfo == null)
                parentSettingAndFieldInfoForList.AddToList(theValue);
            else
            {
                try
                {
                    if (fieldInfo.FieldType == typeof(Int32) && theValue is double)
                        theValue = (int)((double)theValue);
                    else if (fieldInfo.FieldType == typeof(Int64) && theValue is double)
                        theValue = (long)((double)theValue);
                    fieldInfo.SetValue(objectToSet, theValue);
                }
                catch (Exception ex)
                {
                    throw new FileLoaderException("Could not assign value " + theValue.ToString() + ". Check to make sure that settings are of correct type and in correct order. " + ex.Message);
                }
            }
        }

        public void AddToList(object theValue)
        {
            if (itemsForList == null)
            {
                itemsForList = Activator.CreateInstance(type);
            }
            ((System.Collections.IList)itemsForList).Add(theValue);
        }

        public List<SettingAndFieldInfo> GetContainedSettingAndFieldInfos(Type theType)
        {
            List<SettingAndFieldInfo> theList = new List<SettingAndFieldInfo>();
            if (this.setting.Type == SettingType.List)
            {
                SettingList theSettingList = ((SettingList)this.setting);
                foreach (var containedSetting in theSettingList.ContainedSettings)
                {
                    Type typeToUse = theType;
                    if (containedSetting is SettingClass)
                        theType = ((SettingClass)containedSetting).SubclassType;
                    SettingAndFieldInfo theSettingAndFieldInfo =
                        new SettingAndFieldInfo(containedSetting, this, theType);
                    theList.Add(theSettingAndFieldInfo);
                }
                return theList;
            }
            else
            { // class setting
                FieldInfo[] fields = theType.GetFields();
                int i = 0;
                foreach (FieldInfo field in fields)
                {
                    bool skip = Attribute.IsDefined(field, typeof(InternallyDefinedSetting));
                    if (!skip)
                    {
                        Setting theSetting;
                        theSetting = ((SettingClass)this.setting).ContainedSettings.SingleOrDefault(x => x.Name == field.Name);
                        if (theSetting == null)
                        {
                            bool optional = Attribute.IsDefined(field, typeof(OptionalSettingAttribute));
                            if (!optional)
                                throw new Exception("The setting " + field.Name + " was expected but was not found in the settings file or was in an order different from what was expected. If this is a worker role, you may need to clear the task queue.");
                        }
                        else
                        {
                            theList.Add(new SettingAndFieldInfo(theSetting, field, this.level + 1));
                            i++;
                        }
                    }
                }
                return theList;
            }
        }

    }
}
