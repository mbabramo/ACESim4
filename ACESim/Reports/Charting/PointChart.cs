using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace ACESim
{
    public partial class PointChart : Form
    {
        int whollyDistinctSeries = 0;
        Graph2DSettings Graph2DSettings;

        public PointChart()
        {
            InitializeComponent();
        }

        public void SetName(string name)
        {
            this.Text = name;
        }

        Color[] colorsForSeries = new Color[] { Color.Brown, Color.Blue, Color.Green, Color.Orange, Color.Red, Color.Yellow, Color.Violet };
        ChartDashStyle[] dashStyleForSeries = new ChartDashStyle[] { ChartDashStyle.Solid, ChartDashStyle.Dash, ChartDashStyle.DashDot, ChartDashStyle.DashDotDot, ChartDashStyle.Dot, ChartDashStyle.Solid, ChartDashStyle.Dash };
        int[] borderWidth = new int[] { 3, 3, 3, 3, 3, 1, 1 };
        Dictionary<string, int> seriesNumberDictionary = new Dictionary<string, int>();

        public void SetPoints(List<Tuple<double,double>> points, Graph2DSettings graph2DSettings)
        {
            Graph2DSettings = graph2DSettings;

            chart1.Titles.First().Text = graph2DSettings.graphName;

            if (graph2DSettings.replaceSeriesOfSameName)
            {
                var existingSeries = chart1.Series.SingleOrDefault(x => x.Name == graph2DSettings.seriesName);
                bool addingToChart = existingSeries == null;
                if (!addingToChart)
                    chart1.Series.Remove(existingSeries);
            }
            int numSameName = chart1.Series.Count(x => x.Name.StartsWith(graph2DSettings.seriesName));
            string seriesName = graph2DSettings.seriesName;
            int seriesNumber;
            if (numSameName > 0)
            {
                seriesNumber = seriesNumberDictionary[seriesName];
                //seriesName += " " + (numSameName + 1).ToString();
                var previousSeries = chart1.Series.Where(x => x.Name.StartsWith(graph2DSettings.seriesName));
                foreach (var prev in previousSeries)
                {
                    prev.Name += "-"; // add a minus to reflect that this is an old version
                    prev.IsVisibleInLegend = false;
                }
            }
            else
            {
                seriesNumber = whollyDistinctSeries;
                seriesNumberDictionary.Add(seriesName, seriesNumber);
                whollyDistinctSeries++;
            }
            chart1.Legends.First().Enabled = whollyDistinctSeries > 1;
            Series newSeries = chart1.Series.Add(seriesName);
            foreach (double[] point in points)
                chart1.Series[seriesName].Points.AddXY(point[0], point[1]);
            chart1.Series[seriesName].ChartType = graph2DSettings.spline ? SeriesChartType.Spline : SeriesChartType.Line;
            chart1.Series[seriesName].Color = Color.FromArgb(255, colorsForSeries[seriesNumber]);
            chart1.Series[seriesName].BorderDashStyle = dashStyleForSeries[seriesNumber];
            chart1.Series[seriesName].BorderWidth = borderWidth[seriesNumber];

            // make the previous series slightly more transparent, depending on settings
            foreach (var series in chart1.Series)
                if (series != newSeries && ((graph2DSettings.fadeSeriesOfSameName && series.Name.StartsWith(graph2DSettings.seriesName) || (graph2DSettings.fadeSeriesOfDifferentName && !series.Name.StartsWith(graph2DSettings.seriesName)))))
                    series.Color = Color.FromArgb((int) (series.Color.A * 0.50), series.Color.R, series.Color.G, series.Color.B);
            var xAxis = chart1.ChartAreas["Default"].AxisX;
            var yAxis = chart1.ChartAreas["Default"].AxisY;
            xAxis.Title = graph2DSettings.xAxisLabel;
            yAxis.Title = graph2DSettings.yAxisLabel;
            if (graph2DSettings.xMin != null && graph2DSettings.xMax != null)
            {
                xAxis.Minimum = (double)graph2DSettings.xMin;
                xAxis.Maximum = (double)graph2DSettings.xMax;
                xAxis.Interval = ((double)graph2DSettings.xMax - (double)graph2DSettings.xMin) / 10.0;
            }
            else
                xAxis.Interval = (points.Max(x => x[0]) - points.Min(x => x[0])) / 10.0;
            if (graph2DSettings.yMin != null && graph2DSettings.yMax != null)
            {
                yAxis.Minimum = (double)graph2DSettings.yMin;
                yAxis.Maximum = (double)graph2DSettings.yMax;
                yAxis.Interval = ((double)graph2DSettings.yMax - (double)graph2DSettings.yMin) / 10.0;
            }
            else
                yAxis.Interval = (points.Max(x => x[0]) - points.Min(x => x[0])) / 10.0;
        }

        public void ExportImageIfDownloadLocationSpecified()
        {
            try
            {
                if (Graph2DSettings.downloadLocation != null && Graph2DSettings.downloadLocation.Trim() != "")
                    chart1.SaveImage(Graph2DSettings.downloadLocation, ChartImageFormat.Jpeg);
            }
            catch
            {
            }
        }

        public static Color ColorFromAhsb(int a, float h, float s, float b)
        {

            if (0 > a || 255 < a)
            {
                throw new Exception();
            }
            if (0f > h || 360f < h)
            {
                throw new Exception();
            }
            if (0f > s || 1f < s)
            {
                throw new Exception();
            }
            if (0f > b || 1f < b)
            {
                throw new Exception();
            }

            if (0 == s)
            {
                return Color.FromArgb(a, Convert.ToInt32(b * 255),
                  Convert.ToInt32(b * 255), Convert.ToInt32(b * 255));
            }

            float fMax, fMid, fMin;
            int iSextant, iMax, iMid, iMin;

            if (0.5 < b)
            {
                fMax = b - (b * s) + s;
                fMin = b + (b * s) - s;
            }
            else
            {
                fMax = b + (b * s);
                fMin = b - (b * s);
            }

            iSextant = (int)Math.Floor(h / 60f);
            if (300f <= h)
            {
                h -= 360f;
            }
            h /= 60f;
            h -= 2f * (float)Math.Floor(((iSextant + 1f) % 6f) / 2f);
            if (0 == iSextant % 2)
            {
                fMid = h * (fMax - fMin) + fMin;
            }
            else
            {
                fMid = fMin - h * (fMax - fMin);
            }

            iMax = Convert.ToInt32(fMax * 255);
            iMid = Convert.ToInt32(fMid * 255);
            iMin = Convert.ToInt32(fMin * 255);

            switch (iSextant)
            {
                case 1:
                    return Color.FromArgb(a, iMid, iMax, iMin);
                case 2:
                    return Color.FromArgb(a, iMin, iMax, iMid);
                case 3:
                    return Color.FromArgb(a, iMin, iMid, iMax);
                case 4:
                    return Color.FromArgb(a, iMid, iMin, iMax);
                case 5:
                    return Color.FromArgb(a, iMax, iMin, iMid);
                default:
                    return Color.FromArgb(a, iMax, iMid, iMin);
            }
        }
    }
}
