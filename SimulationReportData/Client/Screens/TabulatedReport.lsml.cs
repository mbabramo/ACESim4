using System;
using System.Linq;
using System.IO;
using System.IO.IsolatedStorage;
using System.Collections.Generic;
using Microsoft.LightSwitch;
using Microsoft.LightSwitch.Framework.Client;
using Microsoft.LightSwitch.Presentation;
using Microsoft.LightSwitch.Presentation.Extensions;
using SilverlightClassLibrary1;

namespace LightSwitchApplication
{
    public partial class TabulatedReport
    {
        DynamicReport dynamicReportControl;

        partial void DataPointsFiltered_Loaded(bool succeeded)
        {
            if (succeeded)
                FillDynamicReport();
        }

        partial void DataPointsFiltered_Changed(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            FillDynamicReport();
        }

        partial void DataPointsFiltered_SelectionChanged()
        {
            //FillDynamicReport();
        }

        partial void TabulatedReport_Activated()
        {
            this.FindControl("DynamicReport").ControlAvailable += new EventHandler<ControlAvailableEventArgs>(DynamicReport_ControlAvailable);
            //this.FindControl("DynamicReport").SetProperty("StringArray", stringTest);
            //var data = this.DataWorkspace.SimReportData;
            //DynamicReport dr = this.FindControl("DynamicReport") as DynamicReport;
            //dr.BindDynamicReport();
        }

        partial void TransposeRowsAndColumns_Changed()
        {
            FillDynamicReport();
        }

        void DynamicReport_ControlAvailable(object sender, ControlAvailableEventArgs e)
        {
            dynamicReportControl = (DynamicReport)e.Control;
            //FillDynamicReport();

            //string[,] stringTest = new string[,] { { "Row", "Col1", "Col2" }, { "Row1", "a", "b" }, { "Row2", "c", "d" }};
            //myControl.StringArray = stringTest;

        }

        private void FillDynamicReport()
        {
            if (dynamicReportControl == null)
                return;

            List<DataPoint> dpl = this.DataPointsFiltered.ToList();
            List<Row> rows = dpl.Select(x => x.Row).Distinct().ToList();
            List<Column> columns = dpl.Select(x => x.Column).Distinct().ToList();
            string[,] data = new string[rows.Count(), columns.Count()];
            for (int i = 0; i < rows.Count(); i++)
                for (int j = 0; j < columns.Count(); j++)
                {
                    DataPoint dp = null;
                    try
                    {
                        dp = dpl.SingleOrDefault(x => x.Row == rows[i] && x.Column == columns[j]);
                    }
                    catch
                    {
                        throw new Exception("More than one data point exists for row " + rows[i].Name + " and column " + columns[j].Name + " in the specified table.");
                    }
                    if (dp == null)
                        data[i, j] = "N/A";
                    else
                        data[i, j] = dp.Value.ToString();
                }
            DynamicReportData drd = new DynamicReportData() { Data = data, RowNames = rows.Select(x => x.Name).ToArray(), ColNames = columns.Select(x => x.Name).ToArray(), TopLeftCornerText = "" };
            if (this.TransposeRowsAndColumns)
                drd.Transpose();
            System.Windows.Deployment.Current.Dispatcher.BeginInvoke(() =>
            {
                dynamicReportControl.DynamicReportData = drd;
            });
        }

        partial void TabulatedReport_Created()
        {
            SetSettingCategoriesAndChoicesToDefaults();
        }

        partial void SelectedExecutionResultSet_Changed()
        {
            SetSettingCategoriesAndChoicesToDefaults();
        }

        private void SetSettingCategoriesAndChoicesToDefaults()
        {
            // This doesn't work at this point. We would need to add queries for SettingCategory so that 
            // we could get the VisualCollection for each one and then access the SelectedItem property.

            var db = this.DataWorkspace.SimReportData;
            // OrderBy(x => x.SettingChoices.SelectMany(y => y.SettingChoiceToExecutionResultSets).Distinct().Select(z => z.ExecutionResultSet).Distinct().Count()) NOTE: Distinct is not supported.
            var settingCategories = db.SettingCategories.Where(x => true).Execute().ToList();
            const int maxPresets = 1;
            int settingCategoryIndex = 0;

            List<VisualCollection<SettingChoice>> localSettingChoicesLists = new List<VisualCollection<SettingChoice>>() { SettingChoicesByCategory1, SettingChoicesByCategory2, SettingChoicesByCategory3, SettingChoicesByCategory4, SettingChoicesByCategory5, SettingChoicesByCategory6, SettingChoicesByCategory7, SettingChoicesByCategory8 };
            List<SettingCategory> localSettingCategoryProperties = new List<SettingCategory>() { SettingCategory1, SettingCategory2, SettingCategory3, SettingCategory4, SettingCategory5, SettingCategory6, SettingCategory7, SettingCategory8 };
            List<SettingChoice> localSettingChoiceProperties = new List<SettingChoice>() { SettingChoice1, SettingChoice2, SettingChoice3, SettingChoice4, SettingChoice5, SettingChoice6, SettingChoice7, SettingChoice8 };
            foreach (var settingCategory in settingCategories)
            {
                if (settingCategoryIndex == maxPresets)
                    break;
                var defaultChoice = settingCategory.SettingChoices.OrderBy(x => x.Id).FirstOrDefault();
                localSettingCategoryProperties[settingCategoryIndex] = settingCategory;
                localSettingChoicesLists[settingCategoryIndex].SelectedItem = defaultChoice;
                // localSettingChoiceProperties[settingCategoryIndex] = defaultChoice;
                settingCategoryIndex++;
            }
        }

        partial void TabulatedReport_InitializeDataWorkspace(List<IDataService> saveChangesTo)
        {
            // Write your code here.

        }
    }
}
