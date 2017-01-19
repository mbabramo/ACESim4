using ACESim.Reports;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Samples.EntityDataReader;

namespace ACESim
{
    public class SaveExecutionResults
    {
        ExecutionResultSet ExecutionResult;
        ACESIMEntities Database;
        List<Row> Rows;
        List<Column> Columns;
        List<DataPoint> DataPointsToAdd;

        public void DeleteAllExistingExecutionResultSets()
        {
            try
            {
                bool keepGoing = true;
                while (keepGoing)
                {
                    Database = new ACESIMEntities();
                    var ers = Database.ExecutionResultSets.FirstOrDefault();
                    if (ers == null)
                        keepGoing = false;
                    else
                    {
                        Database.ExecutionResultSets.Remove(ers);
                        Database.SaveChanges();
                    }
                }
            }
            catch
            {
            }
            Database = null;
        }

        public void CreateNewExecutionResultSet(List<string> settingCategories, List<string> settingChoices, string fullSettingsList, string fullVariablesList, DateTime commandSetStartTime)
        {
            if (settingCategories.Count != settingChoices.Count)
                throw new Exception("Setting categories must match setting choices.");
            Database = new ACESIMEntities();
            // We store execution results by time the execution command set began. It is highly unlikely we will have two identical times from two computers running simultaneously, even after we round
            // to anticipate sql server rounding
            DateTime roundedTime = new DateTime(commandSetStartTime.Ticks - (commandSetStartTime.Ticks % TimeSpan.TicksPerSecond), commandSetStartTime.Kind);
            ExecutionResult = Database.ExecutionResultSets.SingleOrDefault(x => x.Time == roundedTime);
            if (ExecutionResult == null)
            {
                ExecutionResult = new ExecutionResultSet() { FullSettingsList = fullSettingsList, SettingChoiceSummary = String.Join(", ", settingChoices), FullVariableList = fullVariablesList, Time = roundedTime };
                Database.ExecutionResultSets.Add(ExecutionResult);
            }
            for (int g = 0; g < settingChoices.Count; g++)
            {
                SettingCategory settingCategory = GetExistingOrNewSettingCategory(settingCategories[g]);
                SettingChoice settingChoice = GetExistingOrNewSettingChoice(settingChoices[g], settingCategory);
                SettingChoiceToExecutionResultSet junction = new SettingChoiceToExecutionResultSet() { ExecutionResultSet = ExecutionResult, SettingChoice = settingChoice };
                Database.SettingChoiceToExecutionResultSets.Add(junction);
                ExecutionResult.SettingChoiceToExecutionResultSets.Add(junction);
            }
            Rows = Database.Rows.Where(x => true).ToList();
            Columns = Database.Columns.Where(x => true).ToList();
            DataPointsToAdd = new List<DataPoint>();
            Database.SaveChanges();
        }

        public void SaveChanges()
        {
            Database.SaveChanges();
            //string connectionString = "metadata=res://*/ApplicationData.csdl|res://*/ApplicationData.ssdl|res://*/ApplicationData.msl;provider=System.Data.SqlClient;provider connection string=&quot;server=kpv2vox12p.database.windows.net;initial catalog=ACESIM;integrated security=True;multipleactiveresultsets=True;App=EntityFramework;User ID=mbabramo;Password=qwerty9876!&quot;"; 
            //string connectionString = "server=kpv2vox12p.database.windows.net;initial catalog=ACESIM;integrated security=True;multipleactiveresultsets=True;App=EntityFramework;User ID=mbabramo;Password=qwerty9876!";
            string connectionString = Database.Database.Connection.ConnectionString;
            //var connectionString = System.Configuration.ConfigurationManager.ConnectionStrings["ACESIMEntities1"].ConnectionString;
            var bulkCopy = new SqlBulkCopy(connectionString);
            bulkCopy.DestinationTableName = "DataPoints";
            try
            {
                bulkCopy.WriteToServer(DataPointsToAdd.AsDataReader());
            }
            catch
            {
            }
            Database.SaveChanges();
        }

        public SettingCategory GetExistingOrNewSettingCategory(string name)
        {
            bool mustUpdateDatabase = false;
            var item = Database.SettingCategories.SingleOrDefault(x => x.Name == name);
            if (item == null)
            {
                item = new SettingCategory() { Name = name };
                Database.SettingCategories.Add(item);
                mustUpdateDatabase = true;
            }
            if (mustUpdateDatabase)
                Database.SaveChanges();
            return item;
        }

        public SettingChoice GetExistingOrNewSettingChoice(string name, SettingCategory category)
        {
            bool mustUpdateDatabase = false;
            var item = Database.SettingChoices.SingleOrDefault(x => x.Name == name);
            if (item == null)
            {
                item = new SettingChoice() { Name = name, SettingCategory = category };
                Database.SettingChoices.Add(item);
                mustUpdateDatabase = true;
            }
            if (mustUpdateDatabase)
                Database.SaveChanges();
            return item;
        }

        public void AddRowsAndColumns(List<string> rowNames, List<string> colNames)
        {
            foreach (string rowName in rowNames)
            {
                Row row = Rows.SingleOrDefault(x => x.Name == rowName);
                if (row == null)
                {
                    row = new Row() { Name = rowName };
                    Database.Rows.Add(row);
                    Rows.Add(row);
                }
            } 
            foreach (string colName in colNames)
            {
                Column column = Columns.SingleOrDefault(x => x.Name == colName);
                if (column == null)
                {
                    column = new Column() { Name = colName };
                    Database.Columns.Add(column);
                    Columns.Add(column);
                }
            }
            Database.SaveChanges();
        }

        public void AddDataPoint(string rowName, string colName, double? value)
        {
            Row row = Rows.SingleOrDefault(x => x.Name == rowName);
            Column column = Columns.SingleOrDefault(x => x.Name == colName);
            if (row == null || column == null)
                throw new Exception("Must add rows and columns to database before adding datapoint.");
            double? rounded = value;
            if (rounded != null)
                rounded = NumberPrint.RoundToSignificantFigures((double)rounded, 3);
            // We must set the Id fields because we plan to add these with SqlBulkCopy
            DataPoint dataPoint = new DataPoint() { DataPoint_Row = row.Id, DataPoint_Column = column.Id, ExecutionResultSet_DataPoint = ExecutionResult.Id, Row = row, Column = column, Value = rounded, ExecutionResultSet = ExecutionResult };
            DataPointsToAdd.Add(dataPoint);
        }


    }
}
