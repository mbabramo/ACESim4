using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Util.Collections
{
    public static class DictionaryExtensions
    {
        static DataTable ToDataTable<V>(this List<Dictionary<string, V>> list)
        {
            DataTable result = new DataTable();
            if (list.Count == 0)
                return result;

            var columnNames = list.SelectMany(dict => dict.Keys).Distinct();
            result.Columns.AddRange(columnNames.Select(c => new DataColumn(c)).ToArray());
            foreach (Dictionary<string, V> item in list)
            {
                var row = result.NewRow();
                foreach (var key in item.Keys)
                {
                    row[key] = item[key];
                }

                result.Rows.Add(row);
            }

            return result;
        }

        public static string ToFormattedTable<V>(this List<Dictionary<string, V>> list) => list.ToDataTable().ToFormattedTable();

        public static string ToFormattedTable(this DataTable dataTable)
        {
            StringBuilder sb = new StringBuilder();
            if (dataTable != null)
            {
                string seperator = " | ";

                #region get min length for columns
                Hashtable hash = new Hashtable();
                foreach (DataColumn col in dataTable.Columns)
                    hash[col.ColumnName] = col.ColumnName.Length;
                foreach (DataRow row in dataTable.Rows)
                    for (int i = 0; i < row.ItemArray.Length; i++)
                        if (row[i] != null && row[i] is not DBNull)
                            if (((string)row[i]).Length > (int)hash[dataTable.Columns[i].ColumnName])
                                hash[dataTable.Columns[i].ColumnName] = ((string)row[i]).Length;
                int rowLength = (hash.Values.Count + 1) * seperator.Length;
                foreach (object o in hash.Values)
                    rowLength += (int)o;
                #endregion get min length for columns

                sb.Append(new string('=', (rowLength - " DataTable ".Length) / 2));
                sb.Append(" DataTable ");
                sb.AppendLine(new string('=', (rowLength - " DataTable ".Length) / 2));
                if (!string.IsNullOrEmpty(dataTable.TableName))
                    sb.AppendLine(string.Format("{0,-" + rowLength + "}", string.Format("{0," + ((rowLength + dataTable.TableName.Length) / 2).ToString() + "}", dataTable.TableName)));

                #region write values
                foreach (DataColumn col in dataTable.Columns)
                    sb.Append(seperator + string.Format("{0,-" + hash[col.ColumnName] + "}", col.ColumnName));
                sb.AppendLine(seperator);
                sb.AppendLine(new string('-', rowLength));
                foreach (DataRow row in dataTable.Rows)
                {
                    for (int i = 0; i < row.ItemArray.Length; i++)
                    {
                        sb.Append(seperator + string.Format("{0," + hash[dataTable.Columns[i].ColumnName] + "}", row[i]));
                        if (i == row.ItemArray.Length - 1)
                            sb.AppendLine(seperator);
                    }
                }
                #endregion write values

                sb.AppendLine(new string('=', rowLength));
            }
            else
                sb.AppendLine("================ DataTable is NULL ================");

            return sb.ToString();
        }
    }
}
