using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Browser;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SilverlightClassLibrary1
{
    public class DynamicReportData
    {
        public string TopLeftCornerText;
        public string[] RowNames;
        public string[] ColNames;
        public string[,] Data;

        public void Transpose()
        {
            var temp = RowNames;
            RowNames = ColNames;
            ColNames = temp;
            int origRowsCount = Data.GetLength(0);
            int origColsCount = Data.GetLength(1);
            var tempArray = new string[origColsCount, origRowsCount];
            for (int i = 0; i < origRowsCount; i++)
                for (int j = 0; j < origColsCount; j++)
                    tempArray[j, i] = Data[i, j];
            Data = tempArray;
        }

        public string[,] ToDataIncludingRowAndColumnNames()
        {
            string[,] fullData = new string[RowNames.Count() + 1, ColNames.Count() + 1];
            for (int i = 0; i < RowNames.Count() + 1; i++)
                for (int j = 0; j < ColNames.Count() + 1; j++)
                {
                    string cell = "";
                    if (i == 0 && j == 0)
                        cell = TopLeftCornerText ?? "";
                    else if (i == 0)
                        cell = ColNames[j - 1];
                    else if (j == 0)
                        cell = RowNames[i - 1];
                    else
                        cell = Data[i - 1, j - 1];
                    fullData[i, j] = cell;
                }
            return fullData;
        }

        public string ToClipboardText()
        {
            StringBuilder clipText = new StringBuilder(); 
            for (int i = 0; i < RowNames.Count() + 1; i++)
                for (int j = 0; j < ColNames.Count() + 1; j++)
                {
                    string cell = "";
                    if (i == 0 && j == 0)
                        cell = TopLeftCornerText ?? "";
                    else if (i == 0)
                        cell = ColNames[j - 1];
                    else if (j == 0)
                        cell = RowNames[i - 1];
                    else
                        cell = Data[i - 1, j - 1];
                    clipText.Append(cell);
                    if (j < ColNames.Count())
                        clipText.Append("\t");
                    else if (i < RowNames.Count())
                        clipText.Append(Environment.NewLine);
                }
            return clipText.ToString();
        }
    }

    public partial class DynamicReport : UserControl
    {
        public DynamicReport()
        {
            InitializeComponent();
            BindDynamicReport();
        }

        public static readonly DependencyProperty DynamicReportDataProperty =
            DependencyProperty.Register(
            "DynamicReportData", typeof(DynamicReportData),
            typeof(DynamicReport), null
            );

        public DynamicReportData DynamicReportData
        {
            get { return (DynamicReportData)GetValue(DynamicReportDataProperty); }
            set { SetValue(DynamicReportDataProperty, value); BindDynamicReport(); }
        }

        //string[,] GetTwoDimensinalArray()
        //{
        //    //Generating two dimensional array
        //    string[,] returnValue = new string[5, 5];
        //    for (int i = 0; i < 5; i++)
        //    {
        //        for (int j = 0; j < 5; j++)
        //        {
        //            returnValue[i, j] = "cell [" + i.ToString() + "," + j.ToString() + "]";
        //        }
        //    }

        //    return returnValue;
        //}

        public void BindDynamicReport()
        {
            if (DynamicReportData == null)
                return;

            // automatically fill clipboard
            string clipText = DynamicReportData.ToClipboardText(); //DEBUG // regular text works even with \t and newline
            Clipboard.SetText(clipText); // DEBUG "asdfasdfasdf\t sdfgsdfgsdf g" + Environment.NewLine);

            string[,] _array = DynamicReportData.ToDataIncludingRowAndColumnNames();
            ObservableCollection<ObservableCollection<string>> dataSource = new ObservableCollection<ObservableCollection<string>>();
            //converting two dimensional array into ObservableCollection
            int rows = _array.GetLength(0);
            int columns = _array.GetLength(1);
            for (int i = 1; i < rows; i++)
            {
                ObservableCollection<string> row = new ObservableCollection<string>();
                for (int j = 0; j < columns; j++)
                {
                    row.Add(_array[i, j]);
                }
                dataSource.Add(row);
            }

            // remove any existing columns
            foreach (var column in ReportDataGrid.Columns.ToList())
                ReportDataGrid.Columns.Remove(column);

            for (int i = 0; i < columns; i++)
            {
                //Creating Datagrid columns and binding them to their apropriate columns
                DataGridTextColumn dataColumn = new DataGridTextColumn();
                dataColumn.Header = _array[0, i];
                dataColumn.Binding = new Binding("[" + i.ToString() + "]");
                ReportDataGrid.Columns.Add(dataColumn);
            }
            //Binding DataGrid with collection
            ReportDataGrid.ItemsSource = dataSource;
        }
    }
}
