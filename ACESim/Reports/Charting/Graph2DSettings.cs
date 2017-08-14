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
        
        public string subtitle = "";
        
        public string seriesName = "";
        
        public bool addRepetitionInfoToSeriesName = false;
        
        public double? xMin = null;
        
        public double? xMax = null;
        
        public double? yMin = null;
        
        public double? yMax = null;
        
        public string replacementXValues = null;
        
        public string replacementYValues = null;
        
        public int? maxNumberPoints = null;
        
        public string xAxisLabel = "";
        
        public string yAxisLabel = "";
        
        public bool dockLegendLeft = false;
        
        public string downloadLocation = null;
        
        public bool exportFramesOfMovies;
        
        public bool replaceSeriesOfSameName;
        
        public bool fadeSeriesOfSameName;
        
        public bool fadeSeriesOfDifferentName;
        
        public int? maxVisiblePerSeries;
        
        public bool spline;
        
        public bool scatterplot;
        
        public List<Graph2DSuperimposedLine> superimposedLines;
        
        public string superimposeLinesToSeriesWithName;
        
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
