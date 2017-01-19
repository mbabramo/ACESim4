using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class Graph2DSettings
    {
        public string graphName = "";
        [OptionalSetting]
        public string subtitle = "";
        [OptionalSetting]
        public string seriesName = "";
        [OptionalSetting]
        public bool addRepetitionInfoToSeriesName = false;
        [OptionalSetting]
        public double? xMin = null;
        [OptionalSetting]
        public double? xMax = null;
        [OptionalSetting]
        public double? yMin = null;
        [OptionalSetting]
        public double? yMax = null;
        [OptionalSetting]
        public string replacementXValues = null;
        [OptionalSetting]
        public string replacementYValues = null;
        [OptionalSetting]
        public int? maxNumberPoints = null;
        [OptionalSetting]
        public string xAxisLabel = "";
        [OptionalSetting]
        public string yAxisLabel = "";
        [OptionalSetting]
        public bool dockLegendLeft = false;
        [OptionalSetting]
        public string downloadLocation = null;
        [OptionalSetting]
        public bool exportFramesOfMovies;
        [OptionalSetting]
        public bool replaceSeriesOfSameName;
        [OptionalSetting]
        public bool fadeSeriesOfSameName;
        [OptionalSetting]
        public bool fadeSeriesOfDifferentName;
        [OptionalSetting]
        public int? maxVisiblePerSeries;
        [OptionalSetting]
        public bool spline;
        [OptionalSetting]
        public bool scatterplot;
        [OptionalSetting]
        public List<Graph2DSuperimposedLine> superimposedLines;
        [OptionalSetting]
        public string superimposeLinesToSeriesWithName;
        [OptionalSetting]
        public bool highlightSuperimposedWhenFirstHigher;
    }

    [Serializable]
    public class Graph2DSuperimposedLine
    {
        public double superimposedLineStartX;
        public double superimposedLineStartY;
        public double superimposedLineEndX;
        public double superimposedLineEndY;
        public Color color;
    }
}
