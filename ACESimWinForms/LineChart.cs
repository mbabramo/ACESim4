using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace ACESim
{
    [Serializable]
    public partial class LineChart : Form
    {
        int numberCallsToCreateOrAddToGraph = 0;
        int whollyDistinctSeries = 0;
        List<int> randomItemsToChoose;
        Graph2DSettings Graph2DSettings;

        public LineChart()
        {
            InitializeComponent();
        }

        public void SetName(string name)
        {
            this.Text = name;
        }

        Color[] colorsForSeries = new Color[] { Color.Brown, Color.Blue, Color.Green, Color.Orange, Color.Red, Color.Yellow, Color.Violet, Color.DarkCyan, Color.DarkGoldenrod, Color.DarkGray, Color.DeepPink, Color.BurlyWood, Color.BlanchedAlmond, Color.Bisque, Color.Azure, Color.Aqua, Color.Aquamarine, Color.BlueViolet, Color.CadetBlue, Color.Chocolate, Color.CornflowerBlue, Color.Brown, Color.Blue, Color.Green, Color.Orange, Color.Red, Color.Yellow, Color.Violet, Color.DarkCyan, Color.DarkGoldenrod, Color.DarkGray, Color.DeepPink, Color.BurlyWood, Color.BlanchedAlmond, Color.Bisque, Color.Azure, Color.Aqua, Color.Aquamarine, Color.BlueViolet, Color.CadetBlue, Color.Chocolate, Color.CornflowerBlue, Color.Brown, Color.Blue, Color.Green, Color.Orange, Color.Red, Color.Yellow, Color.Violet, Color.DarkCyan, Color.DarkGoldenrod, Color.DarkGray, Color.DeepPink, Color.BurlyWood, Color.BlanchedAlmond, Color.Bisque, Color.Azure, Color.Aqua, Color.Aquamarine, Color.BlueViolet, Color.CadetBlue, Color.Chocolate, Color.CornflowerBlue };
        ChartDashStyle[] dashStyleForSeries = new ChartDashStyle[] { ChartDashStyle.Solid, ChartDashStyle.Dot, ChartDashStyle.Dash, ChartDashStyle.DashDot, ChartDashStyle.DashDotDot, ChartDashStyle.Solid, ChartDashStyle.Dash, ChartDashStyle.DashDot, ChartDashStyle.DashDotDot, ChartDashStyle.Solid, ChartDashStyle.Dash, ChartDashStyle.DashDot, ChartDashStyle.DashDotDot, ChartDashStyle.Solid, ChartDashStyle.Dash, ChartDashStyle.DashDot, ChartDashStyle.DashDotDot, ChartDashStyle.Solid, ChartDashStyle.Dash, ChartDashStyle.DashDot, ChartDashStyle.DashDotDot, ChartDashStyle.Solid, ChartDashStyle.Dash, ChartDashStyle.DashDot, ChartDashStyle.DashDotDot, ChartDashStyle.Solid, ChartDashStyle.Dash, ChartDashStyle.DashDot, ChartDashStyle.DashDotDot, ChartDashStyle.Solid, ChartDashStyle.Dash };
        int[] borderWidth = new int[] { 3, 3, 3, 3, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 };
        Dictionary<string, int> seriesNumberDictionary = new Dictionary<string, int>();

        public void CreateOrAddToGraph(List<double[]> points, Graph2DSettings graph2DSettings, string repetitionTagString)
        {
            Graph2DSettings = graph2DSettings;
            if (Graph2DSettings.replacementXValues != null && Graph2DSettings.replacementYValues != null && Graph2DSettings.replacementXValues != "" && Graph2DSettings.replacementYValues != "")
            {
                List<double> newXVals = graph2DSettings.replacementXValues.Trim().Split(',').Select(x => Convert.ToDouble(x)).ToList();
                List<double> newYVals = graph2DSettings.replacementYValues.Trim().Split(',').Select(x => Convert.ToDouble(x)).ToList();
                points = newXVals.Zip(newYVals, (x, y) => new double[] { x, y }).ToList();
            }
            if (Graph2DSettings.maxNumberPoints != null)
            { // We are going to pick a random set of these points (rather than just the first n), to make sure we don't get an arbitrary section of the graph
                if (randomItemsToChoose == null)
                    randomItemsToChoose = RandomSubset.GetRandomIntegersInRandomOrder(0, points.Count - 1, (int)Graph2DSettings.maxNumberPoints);
                List<double[]> points2 = new List<double[]>();
                for (int r = 0; r < randomItemsToChoose.Count; r++)
                    points2.Add(points[randomItemsToChoose[r]]);
                points = points2;
            }
            numberCallsToCreateOrAddToGraph++;
            chart1.Titles.First().Text = Graph2DSettings.graphName;
            if (Graph2DSettings.subtitle != null && Graph2DSettings.subtitle != "")
            {
                var subtitle = chart1.Titles[1];
                subtitle.Text = Graph2DSettings.subtitle;
                subtitle.Visible = true;
            }
            string seriesName;
            int seriesNumber;
            GetSeriesNameAndNumber(out seriesName, out seriesNumber, repetitionTagString);
            Series newSeries = DefineNewSeriesPointsAndStyle(points, seriesName, seriesNumber);
            SetVisibility(newSeries);
            AdjustAxes();

        }

        private void GetSeriesNameAndNumber(out string seriesName, out int seriesNumber, string repetitionTagString)
        {
            seriesName = Graph2DSettings.seriesName;
            if (Graph2DSettings.addRepetitionInfoToSeriesName)
            {
                if (seriesName == "Unnamed")
                    seriesName = repetitionTagString;
                else
                    seriesName += " (" + repetitionTagString + ")";
            }

            string seriesNameCopyForLambdaExpression = seriesName;
            int numSameName = chart1.Series.Count(x => x.Name.StartsWith(seriesNameCopyForLambdaExpression));

            if (Graph2DSettings.replaceSeriesOfSameName)
            {
                var existingSeries = chart1.Series.SingleOrDefault(x => x.Name == seriesNameCopyForLambdaExpression);
                if (existingSeries != null)
                    chart1.Series.Remove(existingSeries);
            }

            if (numSameName > 0)
            {
                seriesNumber = seriesNumberDictionary[seriesName];
                //seriesName += " " + (numSameName + 1).ToString();
                var previousSeries = chart1.Series.Where(x => x.Name.StartsWith(seriesNameCopyForLambdaExpression));
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
            if (Graph2DSettings.dockLegendLeft)
                chart1.Legends.First().Docking = Docking.Left;
        }

        private Series DefineNewSeriesPointsAndStyle(List<double[]> points, string seriesName, int seriesNumber)
        {
            Series newSeries = chart1.Series.Add(seriesName);
            Series seriesToDrawLinesTo = null;
            if (Graph2DSettings.superimposeLinesToSeriesWithName != null && Graph2DSettings.superimposeLinesToSeriesWithName != "")
                seriesToDrawLinesTo = chart1.Series.SingleOrDefault(x => x.Name == Graph2DSettings.superimposeLinesToSeriesWithName);
            int pointNumber = 0;
            foreach (double[] point in points)
            {
                newSeries.Points.AddXY(point[0], point[1]);
                if (seriesToDrawLinesTo != null)
                {
                    if (Graph2DSettings.superimposedLines == null)
                        Graph2DSettings.superimposedLines = new List<Graph2DSuperimposedLine>();
                    Color color = (Graph2DSettings.highlightSuperimposedWhenFirstHigher && seriesToDrawLinesTo.Points[pointNumber].YValues.First() > point[1]) ? Color.Yellow : Color.Gray;
                    Graph2DSettings.superimposedLines.Add(new Graph2DSuperimposedLine() { superimposedLineStartX = point[0], superimposedLineStartY = point[1], superimposedLineEndX = seriesToDrawLinesTo.Points[pointNumber].XValue, superimposedLineEndY = seriesToDrawLinesTo.Points[pointNumber].YValues.First(), color = color });
                }
                pointNumber++;
            }
            if (Graph2DSettings.scatterplot)
                newSeries.ChartType = SeriesChartType.Point;
            else
                newSeries.ChartType = Graph2DSettings.spline ? SeriesChartType.Spline : SeriesChartType.Line;
            newSeries.Color = Color.FromArgb(255, colorsForSeries[seriesNumber]);
            newSeries.BorderDashStyle = dashStyleForSeries[seriesNumber];
            newSeries.BorderWidth = borderWidth[seriesNumber];
            return newSeries;
        }

        private void AdjustAxes()
        {
            var xAxis = chart1.ChartAreas["Default"].AxisX;
            var yAxis = chart1.ChartAreas["Default"].AxisY;
            xAxis.Title = Graph2DSettings.xAxisLabel;
            yAxis.Title = Graph2DSettings.yAxisLabel;
            if (Graph2DSettings.xMin != null && Graph2DSettings.xMax != null)
            {
                xAxis.Minimum = (double)Graph2DSettings.xMin;
                xAxis.Maximum = (double)Graph2DSettings.xMax;
                xAxis.Interval = ((double)Graph2DSettings.xMax - (double)Graph2DSettings.xMin) / 10.0;
            }

            //else
            //    xAxis.Interval = (points.Max(x => x[0]) - points.Min(x => x[0])) / 10.0;
            if (Graph2DSettings.yMin != null && Graph2DSettings.yMax != null)
            {
                yAxis.Minimum = (double)Graph2DSettings.yMin;
                yAxis.Maximum = (double)Graph2DSettings.yMax;
                yAxis.Interval = ((double)Graph2DSettings.yMax - (double)Graph2DSettings.yMin) / 10.0;
            }
            else
            {
                //yAxis.IntervalAutoMode = IntervalAutoMode.FixedCount;
                //yAxis.Interval = (points.Max(x => x[1]) - points.Min(x => x[1])) / 10.0;
            }
        }

        private void SetVisibility(Series newSeries)
        {
            // make the previous series slightly more transparent up to a point, depending on settings
            bool[] visible = new bool[chart1.Series.Count];
            double[] visibility = new double[chart1.Series.Count];
            visibility[chart1.Series.Count - 1] = 255.0;
            for (int v = chart1.Series.Count - 2; v >= 0; v--)
            {
                visibility[v] = visibility[v + 1] * 0.6;
                if (visibility[v] < 25.0)
                    visibility[v] = 25.0;

            }
            if (Graph2DSettings.maxVisiblePerSeries == null)
            {
                for (int v = 2; v < chart1.Series.Count; v++)
                    visible[v] = true;
            }
            else
            {
                for (int v = 0; v < chart1.Series.Count; v++)
                    visible[v] = false;
                int numToSkipAtBeginning = 2; // Series1 and Series2
                List<string> seriesNames = chart1.Series.Where(x => !x.Name.StartsWith("Series")).Select(x => x.Name.Replace("-", "")).ToList();
                List<string> distinctSeries = seriesNames.Distinct().ToList();
                int numberDistinctSeries = distinctSeries.Count();

                for (int ds = 0; ds < numberDistinctSeries; ds++)
                {
                    List<int> indicesForThisSeries = new List<int>();
                    for (int sn = 0; sn < seriesNames.Count; sn++)
                        if (seriesNames[sn] == distinctSeries[ds])
                            indicesForThisSeries.Add(numToSkipAtBeginning + sn);
                    visible[indicesForThisSeries[0]] = visible[indicesForThisSeries.Last()] = true; // always show first and last
                    int remainingToSetTruePerSeries = (int)Graph2DSettings.maxVisiblePerSeries - 2;
                    if (remainingToSetTruePerSeries > 0 && indicesForThisSeries.Count > 2)
                    { // We must step from indicesForThisSeries[0] to indicesForThisSeries.Last() in (remainingToSetTruePerSeries + 1) steps (but not doing anything the last step)
                        double stepSize = ((double)(indicesForThisSeries.Last() - indicesForThisSeries[0])) / ((double)(remainingToSetTruePerSeries + 1));
                        for (int v = 1; v <= remainingToSetTruePerSeries; v++)
                            visible[indicesForThisSeries[0] + (int)((double)v * stepSize)] = true;
                    }
                }
            }

            int v2 = 0;
            foreach (var series in chart1.Series)
            {
                if (series != newSeries && ((Graph2DSettings.fadeSeriesOfSameName && series.Name.StartsWith(Graph2DSettings.seriesName) || (Graph2DSettings.fadeSeriesOfDifferentName && !series.Name.StartsWith(Graph2DSettings.seriesName)))))
                    series.Color = Color.FromArgb(visible[v2] ? (int)visibility[v2] : 0, series.Color.R, series.Color.G, series.Color.B);
                v2++;
            }
        }

        public void ExportFrameOfMovieIfDownloadLocationSpecified()
        {
            string originalLocation = Graph2DSettings.downloadLocation;
            Graph2DSettings.downloadLocation = Graph2DSettings.downloadLocation.Replace(".jpg", "-" + 10000 + numberCallsToCreateOrAddToGraph.ToString() + ".jpg");
            ExportImageIfDownloadLocationSpecified();
            Graph2DSettings.downloadLocation = originalLocation;
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

        private void chart1_PostPaint(object sender, System.Windows.Forms.DataVisualization.Charting.ChartPaintEventArgs e)
        {
            try
            {

                Chart theChart = (Chart)sender;
                ChartArea area = theChart.ChartAreas[0];
                ChartGraphics chartGraphics = e.ChartGraphics;
                Graphics graph = chartGraphics.Graphics;
                if (area.Name == "Default")
                    DrawSuperimposedLines(chartGraphics, graph);
            }
            catch
            {
            }
        }

        private void DrawSuperimposedLines(ChartGraphics chartGraphics, Graphics graph)
        {
            // If Connection line is not checked return
            if (Graph2DSettings.superimposedLines != null && (Graph2DSettings.superimposedLines.Any()))
            {
                foreach (Graph2DSuperimposedLine lineSettings in Graph2DSettings.superimposedLines)
                {

                    double max = (double)lineSettings.superimposedLineEndY;
                    double min = (double)lineSettings.superimposedLineStartY;
                    double xMax = (double)lineSettings.superimposedLineEndX;
                    double xMin = (double)lineSettings.superimposedLineStartX;

                    // Convert X and Y values to screen position
                    float pixelYMax = (float)chartGraphics.GetPositionFromAxis("Default", AxisName.Y, max);
                    float pixelXMax = (float)chartGraphics.GetPositionFromAxis("Default", AxisName.X, xMax);
                    float pixelYMin = (float)chartGraphics.GetPositionFromAxis("Default", AxisName.Y, min);
                    float pixelXMin = (float)chartGraphics.GetPositionFromAxis("Default", AxisName.X, xMin);

                    PointF point1 = PointF.Empty;
                    PointF point2 = PointF.Empty;

                    // Set Maximum and minimum points
                    point1.X = pixelXMax;
                    point1.Y = pixelYMax;
                    point2.X = pixelXMin;
                    point2.Y = pixelYMin;

                    // Convert relative coordinates to absolute coordinates.
                    point1 = chartGraphics.GetAbsolutePoint(point1);
                    point2 = chartGraphics.GetAbsolutePoint(point2);

                    // Draw connection line
                    Pen myPen = new Pen(lineSettings.color);
                    myPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
                    graph.DrawLine(myPen, point1, point2);
                }
            }
        }

        private void chart1_Click(object sender, EventArgs e)
        {

        }

    }


}
