using CsvHelper;
using Microsoft.CSharp.RuntimeBinder;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace ACESimBase.Util
{
    public static class DynamicUtilities
    {

        public static object GetProperty(object o, string member)
        {
            if (o == null) throw new ArgumentNullException("o");
            if (member == null) throw new ArgumentNullException("member");
            Type scope = o.GetType();
            IDynamicMetaObjectProvider provider = o as IDynamicMetaObjectProvider;
            if (provider != null)
            {
                ParameterExpression param = Expression.Parameter(typeof(object));
                DynamicMetaObject mobj = provider.GetMetaObject(param);
                GetMemberBinder binder = (GetMemberBinder)Microsoft.CSharp.RuntimeBinder.Binder.GetMember(0, member, scope, new CSharpArgumentInfo[] { CSharpArgumentInfo.Create(0, null) });
                DynamicMetaObject ret = mobj.BindGetMember(binder);
                BlockExpression final = Expression.Block(
                    Expression.Label(CallSiteBinder.UpdateLabel),
                    ret.Expression
                );
                LambdaExpression lambda = Expression.Lambda(final, param);
                Delegate del = lambda.Compile();
                return del.DynamicInvoke(o);
            }
            else
            {
                return o.GetType().GetProperty(member, BindingFlags.Public | BindingFlags.Instance).GetValue(o, null);
            }
        }

        public static string GetStringProperty(dynamic dyn, string propertyName)
        {
            IDictionary<string, object> dict = dyn;
            if (dict.ContainsKey(propertyName))
                return dict[propertyName] as string;
            return null;
        }

        public static bool StringPropertyMatches(dynamic d1, dynamic d2, string propertyName)
        {
            string d1s = GetStringProperty(d1, propertyName);
            string d2s = GetStringProperty(d2, propertyName);
            return d1s == d2s;
        }

        public static void MergeDynamic(List<dynamic> original, List<dynamic> set2, List<string> IDColumnNames, out Dictionary<string, int> columnOrder)
        {
            columnOrder = new Dictionary<string, int>();
            if (!set2.Any())
                return;
            if (!original.Any())
            {
                original.AddRange(set2);
                return;
            }
            List<string> originalKeys = original.Select(x => (IDictionary<string, object>)x).First().Keys.ToList();
            List<string> set2Keys = set2.Select(x => (IDictionary<string, object>)x).First().Keys.ToList();
            List<string> keysInOriginalOnly = originalKeys.Except(set2Keys).ToList();
            List<string> keysInSet2Only = set2Keys.Except(originalKeys).ToList();
            int i = 0;
            foreach (string originalKey in originalKeys)
                columnOrder[originalKey] = i++;
            foreach (string set2OnlyKey in keysInSet2Only)
                columnOrder[set2OnlyKey] = i++;

            IDictionary<string, object> first_orig = original.First();
            foreach (var key in set2Keys)
                if (first_orig.ContainsKey(key) == false)
                    first_orig[key] = null;
            foreach (IDictionary<string, object> d in set2)
            {
                IDictionary<string, object> match = null;
                foreach (IDictionary<string, object> d_orig in original)
                {
                    if (IDColumnNames.All(x => StringPropertyMatches(d, d_orig, x)))
                    {
                        match = d_orig;
                        break;
                    }
                }
                if (match == null)
                {
                    foreach (string propertyName in keysInOriginalOnly)
                        d[propertyName] = null;
                    original.Add(d);
                }
                else
                {
                    foreach (string propertyName in set2Keys)
                    {
                        match[propertyName] = d[propertyName];
                    }
                }
            }
        }

        private static IEnumerable<string> GetPropertyNames(dynamic d)
        {
            return ((object)d).GetType().GetProperties().Select(p => p.Name);
        }

        /// <summary>
        /// Merges two csv files, represented by in memory strings, matching rows by a set of ID column names. New columns and rows are added as needed.
        /// </summary>
        /// <param name="csv1"></param>
        /// <param name="csv2"></param>
        /// <param name="IDColumnNames"></param>
        /// <returns></returns>
        public static string MergeCSV(string csv1, string csv2, List<string> IDColumnNames)
        {
            using (var csv_r1 = new CsvReader(new StringReader(csv1)))
            using (var csv_r2 = new CsvReader(new StringReader(csv2)))
            using (var writer = new StringWriter())
            using (var csv_w = new CsvWriter(writer))
            {
                csv_r1.Configuration.MissingFieldFound = null;
                csv_r2.Configuration.MissingFieldFound = null;
                var records1 = csv_r1.GetRecords<dynamic>().ToList();
                var records2 = csv_r2.GetRecords<dynamic>().ToList();
                MergeDynamic(records1, records2, IDColumnNames, out Dictionary<string, int> columnOrder);
                csv_w.Configuration.ShouldQuote = (field, context) => true;
                csv_w.Configuration.DynamicPropertySort = Comparer<string>.Create((x, y) => columnOrder[x].CompareTo(columnOrder[y]));
                csv_w.WriteRecords(records1);
                return writer.ToString();
            }
        }
    }
}
