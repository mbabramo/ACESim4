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
    public partial class EditableDataPointsGrid
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

        partial void EditableDataPointsGrid_Activated()
        {
            this.FindControl("DynamicReport").ControlAvailable += new EventHandler<ControlAvailableEventArgs>(DynamicReport_ControlAvailable);
            //this.FindControl("DynamicReport").SetProperty("StringArray", stringTest);
            //var data = this.DataWorkspace.SimReportData;
            //DynamicReport dr = this.FindControl("DynamicReport") as DynamicReport;
            //dr.BindDynamicReport();
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
                    DataPoint dp = dpl.SingleOrDefault(x => x.Row == rows[i] && x.Column == columns[j]);
                    if (dp == null)
                        data[i, j] = "N/A";
                    else
                        data[i, j] = dp.Value.ToString();
                }
            System.Windows.Deployment.Current.Dispatcher.BeginInvoke(() =>
            {
                DynamicReportData drd = new DynamicReportData() { Data = data, RowNames = rows.Select(x => x.Name).ToArray(), ColNames = columns.Select(x => x.Name).ToArray(), TopLeftCornerText = "Row" };
                dynamicReportControl.DynamicReportData = drd;
            });
        }

        partial void EditableDataPointsGrid_Created()
        {
            // Write your code here.

        }

        partial void EditableDataPointsGrid_InitializeDataWorkspace(List<IDataService> saveChangesTo)
        {
            // Write your code here.

        }
    }

}
