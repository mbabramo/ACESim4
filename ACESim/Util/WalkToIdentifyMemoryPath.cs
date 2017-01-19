using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    public class SkipInWalkToIdentifyMemoryPathAttribute : Attribute
    {
    }

    public static class WalkToIdentifyMemoryPath
    {
        static Dictionary<object, bool> alreadyWalked = null;
        static int minSizeToReport = 0;

        public static void Walk(object myObject)
        {
            alreadyWalked = new Dictionary<object, bool>();
            WalkHelper("Root", myObject, true, myObject.GetType());
        }

        private static void WalkHelper(string rootName, object myObject, bool displaySubObject, Type objectType)
        {
            if (myObject == null)
                return;
            if (alreadyWalked.ContainsKey(myObject))
                return;
            alreadyWalked.Add(myObject, true);
            if (!objectType.IsSerializable)
                return;
            TabbedText.Tabs++;
            int length = BinarySerialization.GetByteArray(myObject).Length;
            if (length > minSizeToReport)
                TabbedText.WriteLine(rootName + "--" + objectType.FullName + ": " + length);

          if (myObject == null) 
          {
              // do nothing
          }
          else 
          {
            //check for collection
            if (objectType.GetInterface("IEnumerable") != null) 
            {
              int itemNb = 0;
              foreach (object item in (IEnumerable)myObject) 
              {
                  WalkHelper(itemNb.ToString(), item, displaySubObject, item.GetType());
                itemNb += 1;
              }
            }
            else 
            {
              ArrayList al = new ArrayList();
              System.Reflection.PropertyInfo pi = default(System.Reflection.PropertyInfo);
              System.Reflection.MemberInfo[] members = objectType.GetMembers();
              foreach (System.Reflection.MemberInfo mi in objectType.GetMembers()) 
              {
                  if (mi.GetCustomAttributes(true).Any(x => x is SkipInWalkToIdentifyMemoryPathAttribute || x is NonSerializedAttribute))
                      continue;
                if ((mi.MemberType & System.Reflection.MemberTypes.Constructor) != 0)
                {//ignore constructor
                }
                else if (object.ReferenceEquals(mi.DeclaringType, typeof(object))) 
                {//ignore inherited
                }
                else if (!al.Contains(mi.Name) & (mi.MemberType & System.Reflection.MemberTypes.Property) != 0) 
                {
                  al.Add(mi.Name);
                  pi = (System.Reflection.PropertyInfo)mi;
                  if (!(displaySubObject) || (pi.PropertyType.IsValueType || pi.PropertyType.Equals(typeof(string)))) 
                  {
                    // do nothing print(pi, myObject);
                  }
                  else 
                  {
                    //display sub objects
                      WalkHelper(mi.Name, pi.GetValue(myObject, null), displaySubObject, pi.PropertyType);
                  }
                }
              }
            }
          }
          TabbedText.Tabs--;
        }
    }
}
