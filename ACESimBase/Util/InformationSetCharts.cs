using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using CsvHelper;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using ACESim;

namespace ACESimBase.Util
{

    public static class InformationSetCharts
    {
        public static int BitmapMultiplier = 1; // DEBUG

        #region Drawings

        public static void CreateBlankDrawing(int width, int height, out Bitmap bmpOut, out Graphics g, out Rectangle overall)
        {
            int newWidth = width;
            int newHeight = height;
            bmpOut = new System.Drawing.Bitmap(newWidth, newHeight);
            g = System.Drawing.Graphics.FromImage(bmpOut);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            overall = new Rectangle(5 * BitmapMultiplier, 5 * BitmapMultiplier, newWidth - 10 * BitmapMultiplier, newHeight - 10 * BitmapMultiplier);
        }

        #endregion

        #region Data

        public static double[][] GetValuesFromCSV(string fileName, int firstRow, int firstColumn, int numRows, int numColumns)
        {
            // Change from 1 numbering to 0 numbering
            firstRow -= 2; // 1 to adjust for 1 numbering, and 1 to adjust for header row
            firstColumn--;
            double[][] proportions = new double[numRows][];
            for (int i = 0; i < numColumns; i++)
                proportions[i] = new double[numColumns];
            using (var reader = new StreamReader(fileName))
            using (var csv = new CsvReader(reader))
            {
                csv.Read();
                csv.ReadHeader();
                int row = -1;
                while (csv.Read())
                {
                    row++;
                    var rowHeader = csv.GetField(0);
                    if (row == firstRow + numRows)
                        return proportions;
                    if (row >= firstRow)
                    {
                        string percentOfObservationsString = csv.GetField(firstColumn - 1);
                        if (percentOfObservationsString == "")
                        {
                            // we didn't even get to this round (everything settled)
                            proportions[row - firstRow] = null;
                            continue;
                        }
                        double percentOfObservations = csv.GetField<double>(firstColumn - 1);
                        if (percentOfObservations < 0.005)
                        {
                            proportions[row - firstRow] = null;
                            continue;
                        }
                        for (int column = firstColumn; column < firstColumn + numColumns; column++)
                        {
                            if (csv.GetField<string>(column) == "")
                            {
                                proportions[row - firstRow] = null;
                                break;
                            }
                            proportions[row - firstRow][column - firstColumn] = csv.GetField<double>(column);
                        }
                        if (Math.Abs(proportions[row - firstRow].Sum() - 1.0) > 0.01)
                            throw new Exception();
                    }
                }
                if (row < firstRow)
                    throw new Exception("Expected row not found");
            }
            if (proportions.Any(x => x != null && Math.Abs(x.Sum() - 1.0) > 0.01))
                throw new Exception();
            return proportions;
        }

        public static void GetDataFromCSVAndPlotAcross(string path, Graphics g, Rectangle r, string filename, (int firstRow, int firstColumn)[] setLocations, int numRowsPerSet, int numColumnsPerSet, int marginBetweenSets, Color color, bool includeTextWherePossible)
        {
            int numSets = setLocations.Length;
            Rectangle[] rects = DivideRectangle(r, true, numSets, marginBetweenSets);
            for (int set = 0; set < numSets; set++)
            {
                double[][] data = GetValuesFromCSV(path + @"\" + filename + ".csv", setLocations[set].firstRow, setLocations[set].firstColumn, numRowsPerSet, numColumnsPerSet);
                BarFillMultiple(g, rects[set], true, data, color, includeTextWherePossible);
            }
        }

        public static void GetDataFromCSVAndPlot(string path, Graphics g, Rectangle r, string filename, int firstRow, int firstColumn, int numRows, int numColumns, Color color, bool includeTextWherePossible)
        {
            double[][] data = GetValuesFromCSV(path + @"\" + filename + ".csv", firstRow, firstColumn, numRows, numColumns);
            BarFillMultiple(g, r, true, data, color, includeTextWherePossible);
        }

        #endregion

        #region Bars

        public static void BarFillMultiple(Graphics g, Rectangle r, bool individualSetHorizontally, double[][] proportions, Color color, bool includeTextWherePossible)
        {
            Rectangle[] rectangles = DivideRectangle(r, !individualSetHorizontally, proportions.Length, 2 * BitmapMultiplier);
            for (int i = 0; i < proportions.Length; i++)
                BarFillSingle(g, rectangles[i], individualSetHorizontally, proportions[i], color, includeTextWherePossible);
        }

        public static void BarFillSingle(Graphics g, Rectangle r, bool horizontally, double[] proportions, Color color, bool includeTextWherePossible)
        {
            if (proportions == null)
                return;
            GetMainFont(out int fontSize, out Font f);
            if (r.Width <= 0 || r.Height <= 0)
                throw new Exception();
            Rectangle[] rects = DivideRectangle(r, horizontally, proportions.Length, 0, proportions);
            for (int i = 0; i < proportions.Length; i++)
            {
                if (proportions[i] > 0)
                {
                    DrawAndFillRectangle(g, rects[i], i, proportions.Length, color);
                    if (rects[i].Width > fontSize * 2 && includeTextWherePossible)
                        DrawTextCenteredHorizontally_Shadowed(g, rects[i], (i + 1).ToString(), f);
                }
            }
        }

        #endregion

        #region Rectangles

        public static void DrawAndFillRectangle(Graphics g, Rectangle r, int intensity, int maxIntensity, Color color)
        {
            if (r.Width == 0)
                return;
            Rectangle rect = new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1);
            g.DrawRectangle(new Pen(Brushes.Black), rect);
            Rectangle innerRect = new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
            int lowestAlpha = 30;
            int highestAlpha = 225;
            int stepSize = (int)(((float)(highestAlpha - lowestAlpha)) / ((float)(maxIntensity - 1)));
            byte alpha = (byte)(lowestAlpha + intensity * stepSize);
            System.Drawing.Color adjustedColor = Color.FromArgb(alpha, color);
            Brush brush = new SolidBrush(adjustedColor);
            g.FillRectangle(brush, innerRect);
        }

        public static Rectangle[] DivideRectangle_WithSpaceForHeader(Rectangle r, bool horizontally, int pixelsForHeader)
        {
            double proportionForHeader = (double)pixelsForHeader / (double)(horizontally ? r.Width : r.Height);
            double[] proportions = new double[] { proportionForHeader, 1.0 - proportionForHeader };
            return DivideRectangle(r, horizontally, 2, 0, proportions);
        }

        public static Rectangle[] DivideRectangle(Rectangle r, bool horizontally, int numRectangles, int margin, double[] proportions = null)
        {
            Rectangle[] result = new Rectangle[numRectangles];
            int[] pixels = new int[numRectangles];
            int existingPixels = horizontally ? r.Width : r.Height;
            int availableSpace = (existingPixels - (numRectangles - 1) * margin);
            int pixelsEach = availableSpace / numRectangles;
            if (proportions == null)
            {
                proportions = Enumerable.Range(1, numRectangles).Select(x => 1.0 / (double)numRectangles).ToArray();
            }
            if (proportions.Sum() == 0)
                throw new Exception();
            pixels = proportions.Select(x => (int)(x * availableSpace)).ToArray();
            int spaceAdjustment = pixels.Sum() - availableSpace;
            int indexToChange = -1;
            while (spaceAdjustment != 0)
            {
                indexToChange++;
                if (indexToChange == numRectangles)
                    indexToChange = 0;
                if (pixels[indexToChange] > 0)
                {
                    if (spaceAdjustment > 0)
                        pixels[indexToChange]--;
                    else
                        pixels[indexToChange]++;
                }
                spaceAdjustment = pixels.Sum() - availableSpace;
            }
            int pixelsSoFar = 0;
            for (int i = 0; i < numRectangles; i++)
            {
                bool avoidDoubleMargin = true;
                int marginAdjustment = 0;
                if (avoidDoubleMargin && i > 0)
                    marginAdjustment = 1;
                if (horizontally)
                    result[i] = new Rectangle(r.X + pixelsSoFar - marginAdjustment, r.Y, pixels[i] + marginAdjustment, r.Height);
                else
                    result[i] = new Rectangle(r.X, r.Y + pixelsSoFar - marginAdjustment, r.Width, pixels[i] + marginAdjustment);
                pixelsSoFar += pixels[i] + margin;
            }
            return result;
        }

        public static Rectangle AddRoundHeaders(Graphics g, Rectangle r, int numRounds, int fontSize)
        {
            r = AddTopHeaders(g, r, fontSize + 3 * BitmapMultiplier, 3 * (fontSize + 3 * BitmapMultiplier), Enumerable.Range(1, numRounds).Select(x => $"Round {x}").ToArray());
            return r;
        }

        public static Rectangle AddLeftHeaders(Graphics g, Rectangle overallRectangle, int marginBetweenSetsVertically, params string[] text)
        {
            GetMainFont(out int fontSize, out Font f);
            Rectangle[] leftHeaderAndRest = DivideRectangle_WithSpaceForHeader(overallRectangle, true, fontSize + 3 * BitmapMultiplier);
            Rectangle leftHeaderSet = leftHeaderAndRest[0];
            Rectangle[] leftHeaders = DivideRectangle(leftHeaderSet, false, text.Length, marginBetweenSetsVertically);
            for (int i = 0; i < text.Length; i++)
                DrawText270(g, leftHeaders[i], text[i], f, Brushes.Black);
            Rectangle everythingRemaining = leftHeaderAndRest[1];
            return everythingRemaining;
        }

        public static Rectangle AddTopHeaders(Graphics g, Rectangle overallRectangle, int marginBetweenSetsHorizontally, int spaceLeftForLeftHeaders, params string[] text)
        {
            GetMainFont(out int fontSize, out Font f);
            Rectangle[] topHeaderAndRest = DivideRectangle_WithSpaceForHeader(overallRectangle, false, fontSize + 3 * BitmapMultiplier);
            Rectangle topHeaderSet = topHeaderAndRest[0];
            topHeaderSet.Size = new Size(topHeaderSet.Width - spaceLeftForLeftHeaders, topHeaderSet.Height);
            topHeaderSet.Offset(spaceLeftForLeftHeaders, 0);
            Rectangle[] topHeaders = DivideRectangle(topHeaderSet, true, text.Length, marginBetweenSetsHorizontally);
            for (int i = 0; i < text.Length; i++)
                DrawTextCenteredHorizontally(g, topHeaders[i], text[i], f, Brushes.Black);
            Rectangle everythingRemaining = topHeaderAndRest[1];
            return everythingRemaining;
        }

        #endregion

        #region Text

        public static void GetMainFont(out int fontSize, out Font f)
        {
            fontSize = 12 * BitmapMultiplier;
            f = new Font(FontFamily.Families.First(x => x.Name == "Calibri Light"), fontSize, GraphicsUnit.World);
        }

        public static void DrawText270(Graphics g, Rectangle r, string text, Font font, Brush brush)
        {
            DrawRotatedText(g, r.X + r.Width / 2, r.Y + r.Height / 2, 270, text, font, brush);
        }

        public static void DrawRotatedText(Graphics g, int x, int y, float angle, string text, Font font, Brush brush)
        {
            g.TranslateTransform(x, y); // Set rotation point
            g.RotateTransform(angle); // Rotate text
            g.TranslateTransform(-x, -y); // Reset translate transform
            SizeF size = g.MeasureString(text, font); // Get size of rotated text (bounding box)
            g.DrawString(text, font, brush, new PointF(x - size.Width / 2.0f, y - size.Height / 2.0f)); // Draw string centered in x, y
            g.ResetTransform(); // Only needed if you reuse the Graphics object for multiple calls to DrawString
        }

        public static void DrawTextCenteredHorizontally_Shadowed(Graphics g, Rectangle r, string text, Font font)
        {
            r.Offset(2, 2);
            DrawTextCenteredHorizontally(g, r, text, font, Brushes.DarkGray);
            r.Offset(-2, -2);
            DrawTextCenteredHorizontally(g, r, text, font, Brushes.Black);
        }

        public static void DrawTextCenteredHorizontally(Graphics g, Rectangle r, string text, Font font, Brush brush)
        {
            StringFormat stringFormatHorizontally = new StringFormat();
            stringFormatHorizontally.Alignment = StringAlignment.Center;
            stringFormatHorizontally.LineAlignment = StringAlignment.Center;
            g.DrawString(text, font, brush, r, stringFormatHorizontally);
        }

        #endregion

        #region Information set charts

        public static void CreateInformationSetChart(List<InformationSetNode> informationSets, string fullPath)
        {
            var startNodes = informationSets.Where(x => x.ParentInformationSet == null).OrderBy(x => x.PlayerIndex).ThenBy(x => x.InformationSetContentsSinceParentString).ToList();
            List<(InformationSetNode, Rectangle)> layout = new List<(InformationSetNode, Rectangle)>();
            Rectangle r = default;
            CreateInformationSetChartLayout(layout, startNodes, ref r, out int totalSizeNeededVertically, out int totalSizeNeededHorizontally);
            CreateBlankDrawing(r.Width, r.Height, out Bitmap bmpOut, out Graphics g, out r);
            foreach ((InformationSetNode, Rectangle) infoSetAndRectangle in layout)
            {
                PlotInformationSetStrategy(g, infoSetAndRectangle.Item2, infoSetAndRectangle.Item1);
            }
            bmpOut.Save(fullPath);
        }

        public static void PlotInformationSetStrategy(Graphics g, Rectangle r, InformationSetNode informationSet)
        {
            GetMainFont(out int fontSize, out Font f);
            int pixels = fontSize;
            Rectangle[] horizontallySplit = DivideRectangle_WithSpaceForHeader(r, true, pixels);
            DrawText270(g, horizontallySplit[0], informationSet.InformationSetContentsSinceParentString, f, Brushes.Black);
            BarFillSingle(g, horizontallySplit[1], true, informationSet.GetAverageStrategiesAsArray() /* DEBUG SUPERDEBUG */, informationSet.PlayerIndex == 0 ? Color.Blue : Color.Orange, true);
        }

        public static void CreateInformationSetChartLayout(List<(InformationSetNode, Rectangle)> layout, List<InformationSetNode> startNodes, ref Rectangle r, out int totalSizeNeededVertically, out int totalSizeNeededHorizontally)
        {
            const int margin = 0;
            int inset = 5 * BitmapMultiplier;
            totalSizeNeededVertically = startNodes.Sum(x => x.SizeNeededToDisplayDescendants);
            int totalSizeNeededVertically_Copy = totalSizeNeededVertically; // to use in anon lambaa
            totalSizeNeededHorizontally = startNodes.Max(x => x.NumGenerationsFromHere);
            if (r == default)
            {
                const int widthEach = 150;
                const int heightEach = 30;
                r = new Rectangle(0, 0, widthEach * totalSizeNeededHorizontally * BitmapMultiplier, heightEach * totalSizeNeededVertically * BitmapMultiplier);
            }
            double[] verticalProportions = startNodes.Select(x => x.SizeNeededToDisplayDescendants / (double)totalSizeNeededVertically_Copy).ToArray();
            double[] horizontalProportions = new[] { 1.0 / (double)totalSizeNeededHorizontally, 1 - 1.0 / (double)totalSizeNeededHorizontally };
            Rectangle[] verticalSplit = DivideRectangle(r, false, startNodes.Count(), margin, verticalProportions);
            if (verticalSplit.Any(x => x.Width <= 0))
                throw new Exception("DEBUG");
            for (int i = 0; i < startNodes.Count; i++)
            {
                var node = startNodes[i];
                Rectangle[] horizontalSplit = totalSizeNeededHorizontally == 1? new Rectangle[] { verticalSplit[i] } : DivideRectangle(verticalSplit[i], true, 2, margin, horizontalProportions);
                if (horizontalSplit.Any(x => x.Width <= 0))
                    throw new Exception("DEBUG");
                //horizontalSplit[0].Offset(-inset, -inset);
                layout.Add((node, horizontalSplit[0]));
                if (node.ChildInformationSets.Any())
                    CreateInformationSetChartLayout(layout, node.ChildInformationSets, ref horizontalSplit[1], out _, out _);
            }
        }

        #endregion

        #region Plaintiff-Defendant plots

        public static void PlotPAndD(string path, string filename, Graphics g, Rectangle r, int numRounds, int numSignals, int numOffers, bool addRoundHeaders = true)
        {
            int marginBetweenSetsAcross = 10 * BitmapMultiplier;
            int marginBetweenSetsVertically = 10 * BitmapMultiplier;
            GetMainFont(out int fontSize, out Font f);
            r = AddLeftHeaders_WithFurtherSubdivision(g, r, marginBetweenSetsVertically, 2 * BitmapMultiplier, new string[] { "P Signal", "D Signal" }, Enumerable.Range(1, numSignals).Select(x => x.ToString()).ToArray());
            if (addRoundHeaders)
                r = AddRoundHeaders(g, r, numRounds, fontSize);

            Rectangle[] verticallyDivided = DivideRectangle(r, false, 2, marginBetweenSetsVertically);
            (int firstRow, int firstColumn)[] plaintiffRoundLocations, defendantRoundLocations;
            if (numRounds == 3)
            {
                plaintiffRoundLocations = new (int firstRow, int firstColumn)[]
                {
                    (140, 85),
                    (152, 97),
                    (164, 109)
                };
                defendantRoundLocations = new (int firstRow, int firstColumn)[]
                {
                    (146, 91),
                    (158, 103),
                    (170, 115)
                };
            }
            else if (numRounds == 2)
            {
                plaintiffRoundLocations = new (int firstRow, int firstColumn)[]
                {
                    (133, 69),
                    (145, 81),
                };
                defendantRoundLocations = new (int firstRow, int firstColumn)[]
                {
                    (139, 75),
                    (151, 87),
                };
            }
            else throw new NotSupportedException();
            bool includeTextWherePossible = true;
            GetDataFromCSVAndPlotAcross(path, g, verticallyDivided[0], filename, plaintiffRoundLocations, numSignals, numOffers, marginBetweenSetsAcross, Color.Blue, includeTextWherePossible);
            GetDataFromCSVAndPlotAcross(path, g, verticallyDivided[1], filename, defendantRoundLocations, numSignals, numOffers, marginBetweenSetsAcross, Color.Orange, includeTextWherePossible);
        }

        public static void PlotPAndD_WithHidden(string path, string filename, int numRounds, int numSignals, int numOffers)
        {
            GetMainFont(out int fontSize, out Font f);
            CreateBlankDrawing(500 * BitmapMultiplier, 300 * BitmapMultiplier, out Bitmap bmpOut, out Graphics g, out Rectangle r);
            int margin = 10 * BitmapMultiplier;
            r = AddRoundHeaders(g, r, numRounds, fontSize);
            r = AddLeftHeaders(g, r, margin, "Revealed Offers", "Hidden Offers");
            Rectangle[] regularAndHidden = DivideRectangle(r, false, 2, margin);
            PlotPAndD(path, filename, g, regularAndHidden[0], numRounds, numSignals, numOffers, false);
            PlotPAndD(path, filename + "-hidden", g, regularAndHidden[1], numRounds, numSignals, numOffers, false);
            bmpOut.Save(path + @"\plot-" + filename + ".png");
        }

        public static void PlotPAndD(string path, string filename, int numRounds, int numSignals, int numOffers)
        {
            Bitmap bmpOut;
            Graphics g;
            Rectangle overall;
            CreateBlankDrawing(500 * BitmapMultiplier, 300 * BitmapMultiplier, out bmpOut, out g, out overall);
            PlotPAndD(path, filename, g, overall, numRounds, numSignals, numOffers);
            bmpOut.Save(path + @"\plot-" + filename + ".png");
        }

        public static Rectangle AddLeftHeaders_WithFurtherSubdivision(Graphics g, Rectangle overallRectangle, int outerMarginBetweenSetsVertically, int innerMarginBetweenSetsVertically, string[] textOuter, string[] textInner)
        {
            overallRectangle = AddLeftHeaders(g, overallRectangle, outerMarginBetweenSetsVertically, textOuter);
            var divided = DivideRectangle(overallRectangle, false, textOuter.Length, outerMarginBetweenSetsVertically);
            Rectangle remaining = default;
            foreach (var division in divided)
            {
                Rectangle inside = AddLeftHeaders(g, division, innerMarginBetweenSetsVertically, textInner);
                if (remaining == default)
                    remaining = inside;
                else
                    remaining = Rectangle.Union(remaining, inside);
            }
            return remaining;
        }

        #endregion
    }
}
