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

        public static string GetStringProperty(dynamic d, string propertyName)
        {
            var propertyInfo = d.GetType().GetProperty(propertyName);
            var value = propertyInfo.GetValue(d, null);
            return value as string;
        }

        public static bool StringPropertyMatches(dynamic d1, dynamic d2, string propertyName)
        {
            string d1s = GetStringProperty(d1, propertyName);
            string d2s = GetStringProperty(d2, propertyName);
            return d1s == d2s;
        }

        public static void MergeDynamic(List<dynamic> original, List<dynamic> set2, List<string> IDColumnNames)
        {
            foreach (dynamic d in set2)
            {
                dynamic match = null;
                foreach (dynamic d_orig in original)
                {
                    if (IDColumnNames.All(x => StringPropertyMatches(d, d_orig, x)))
                    {
                        match = d_orig;
                        break;
                    }
                }
                if (match == null)
                    original.Add(d);
                else
                {
                    foreach (string propertyName in ((object)d).GetType().GetProperties().Select(p => p.Name))
                    {
                        match[propertyName] = d[propertyName];
                    }
                }
            }
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
                var records1 = csv_r1.GetRecords<dynamic>().ToList();
                var records2 = csv_r2.GetRecords<dynamic>().ToList();
                MergeDynamic(records1, records2, IDColumnNames);
                csv_w.WriteRecords(records1);
                return writer.ToString();
            }
        }
    }
}
