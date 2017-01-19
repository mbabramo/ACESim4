using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SilverlightClassLibrary1
{
    public partial class DataPointReport : UserControl
    {
        public DataPointReport()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty StringArrayProperty =
            DependencyProperty.Register(
            "StringArray", typeof(IEnumerable<DataPoint>),
            typeof(DynamicReport), null
            );

        public string[,] StringArray
        {
            get { return (string[,])GetValue(StringArrayProperty); }
            set { SetValue(StringArrayProperty, value); }
        }
    }
}
