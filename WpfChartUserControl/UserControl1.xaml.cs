using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Media.Media3D;
using System.Collections;

namespace WPFChart3D
{
    /// <summary>
    /// Interaction logic for UserControl1.xaml
    /// </summary>
    public partial class UserControl1 : UserControl
    {
        // transform class object for rotate the 3d model
        public WPFChart3D.TransformMatrix m_transformMatrix = new WPFChart3D.TransformMatrix();

        // ***************************** 3d chart ***************************
        private WPFChart3D.Chart3D m_3dChart;       // data for 3d chart
        public int m_nChartModelIndex = -1;         // model index in the Viewport3d
        public int m_nSurfaceChartGridNo = 100;     // surface chart grid no. in each axis
        public int m_nScatterPlotDataNo = 5000;     // total data number of the scatter plot

        // ***************************** selection rect ***************************
        ViewportRect m_selectRect = new ViewportRect();
        public int m_nRectModelIndex = -1;

        public UserControl1()
        {
            InitializeComponent();

            // selection rect
            m_selectRect.SetRect(new Point(-0.5, -0.5), new Point(-0.5, -0.5));
            WPFChart3D.Model3D model3d = new WPFChart3D.Model3D();
            ArrayList meshs = m_selectRect.GetMeshes();
            m_nRectModelIndex = model3d.UpdateModel(meshs, null, m_nRectModelIndex, this.mainViewport);

            // display the 3d chart data no.
            //gridNo.Text = String.Format("{0:d}", m_nSurfaceChartGridNo);
            //dataNo.Text = String.Format("{0:d}", m_nScatterPlotDataNo);

            // display surface chart
            //TestScatterPlot(1000);
            //TransformChart();
        }

        // function for testing surface chart
        public void TestSurfacePlot(int nGridNo)
        {
            int nXNo = nGridNo;
            int nYNo = nGridNo;
            // 1. set the surface grid
            m_3dChart = new UniformSurfaceChart3D();
            ((UniformSurfaceChart3D)m_3dChart).SetGrid(nXNo, nYNo, -100, 100, -100, 100);

            // 2. set surface chart z value
            double xC = m_3dChart.XCenter();
            double yC = m_3dChart.YCenter();
            int nVertNo = m_3dChart.GetDataNo();
            double zV;
            for (int i = 0; i < nVertNo; i++)
            {
                Vertex3D vert = m_3dChart[i];

                double r = 0.15 * Math.Sqrt((vert.x - xC) * (vert.x - xC) + (vert.y - yC) * (vert.y - yC));
                if (r < 1e-10) zV = 1;
                else zV = Math.Sin(r) / r;

                m_3dChart[i].z = (float)zV;
            }
            m_3dChart.GetDataRange();

            // 3. set the surface chart color according to z vaule
            double zMin = m_3dChart.ZMin();
            double zMax = m_3dChart.ZMax();
            for (int i = 0; i < nVertNo; i++)
            {
                Vertex3D vert = m_3dChart[i];
                double h = (vert.z - zMin) / (zMax - zMin);

                Color color = WPFChart3D.TextureMapping.PseudoColor(h);
                m_3dChart[i].color = color;
            }

            // 4. Get the Mesh3D array from surface chart
            ArrayList meshs = ((UniformSurfaceChart3D)m_3dChart).GetMeshes();

            // 5. display vertex no and triangle no of this surface chart
            UpdateModelSizeInfo(meshs);
            
            // 6. Set the model display of surface chart
            WPFChart3D.Model3D model3d = new WPFChart3D.Model3D();
            Material backMaterial = new DiffuseMaterial(new SolidColorBrush(Colors.Gray));
            m_nChartModelIndex = model3d.UpdateModel(meshs, backMaterial, m_nChartModelIndex, this.mainViewport);

            // 7. set projection matrix, so the data is in the display region
            float xMin = m_3dChart.XMin();
            float xMax = m_3dChart.XMax();
            m_transformMatrix.CalculateProjectionMatrix(xMin, xMax, xMin, xMax, zMin, zMax, 0.5);
            TransformChart();         
        }

        // function for testing 3d scatter plot
        public void TestScatterPlot(int nDotNo)
        {
            // 1. set scatter chart data no.
            m_3dChart = new ScatterChart3D();
            m_3dChart.SetDataNo(nDotNo);

            // 2. set property of each dot (size, position, shape, color)
            Random randomObject = new Random();
            int nDataRange = 200;
            for (int i = 0; i < nDotNo; i++)
            {
                ScatterPlotItem plotItem = new ScatterPlotItem();

                plotItem.w = 4;
                plotItem.h = 6;

                plotItem.x = (float)randomObject.Next(nDataRange);
                plotItem.y = (float)randomObject.Next(nDataRange);
                plotItem.z = (float)randomObject.Next(nDataRange);

                plotItem.shape = randomObject.Next(4);

                Byte nR = (Byte)randomObject.Next(256);
                Byte nG = (Byte)randomObject.Next(256);
                Byte nB = (Byte)randomObject.Next(256);

                plotItem.color = Color.FromRgb(nR, nG, nB);
                ((ScatterChart3D)m_3dChart).SetVertex(i, plotItem);
            }

            // 3. set the axes
            m_3dChart.GetDataRange();
            m_3dChart.SetAxes();

            // 4. get Mesh3D array from the scatter plot
            ArrayList meshs = ((ScatterChart3D)m_3dChart).GetMeshes();

            // 5. display model vertex no and triangle no
            UpdateModelSizeInfo(meshs);

            // 6. display scatter plot in Viewport3D
            WPFChart3D.Model3D model3d = new WPFChart3D.Model3D();
            m_nChartModelIndex = model3d.UpdateModel(meshs, null, m_nChartModelIndex, this.mainViewport);
 
            // 7. set projection matrix
            float viewRange = (float)nDataRange;
            m_transformMatrix.CalculateProjectionMatrix(0, viewRange, 0, viewRange, 0, viewRange, 0.5);
            TransformChart();
        }

        // function for testing 3d scatter plot
        public void CreateScatterPlot(List<double[]> points, List<Color> colors)
        {
            // 1. set scatter chart data no.
            m_3dChart = new ScatterChart3D();
            m_3dChart.SetDataNo(points.Count);

            // 1.5 Calculate point adjustments
            // The camera is set in a good position for data from 0 to 200. Let's adjust our data to
            // fit within this, instead of moving the camera.
            float xmin = (float)points.Min(x => x[0]);
            float ymin = (float)points.Min(x => x[1]);
            float zmin = (float)points.Min(x => x[2]);
            float xmax = (float)points.Max(x => x[0]);
            float ymax = (float)points.Max(x => x[1]);
            float zmax = (float)points.Max(x => x[2]);
            float xmult = 200f / (xmax - xmin);
            float ymult = 200f / (ymax - ymin);
            float zmult = 200f / (zmax - zmin);

            // 2. set property of each dot (size, position, shape, color)
            Random randomObject = new Random();
            for (int i = 0; i < points.Count; i++)
            {
                ScatterPlotItem plotItem = new ScatterPlotItem();

                plotItem.w = 4;
                plotItem.h = 4;

                plotItem.x = xmult * ((float)points[i][0] - xmin);
                plotItem.y = ymult * ((float)points[i][1] - ymin);
                plotItem.z = zmult * ((float)points[i][2] - zmin);

                plotItem.shape = (int) WPFChart3D.Chart3D.SHAPE.BAR;  //randomObject.Next(4);

                Byte nR = (Byte)randomObject.Next(256);
                Byte nG = (Byte)randomObject.Next(256);
                Byte nB = (Byte)randomObject.Next(256);

                plotItem.color = colors[i]; // Color.FromRgb(nR, nG, nB);
                ((ScatterChart3D)m_3dChart).SetVertex(i, plotItem);
            }

            // 3. set the axes
            m_3dChart.GetDataRange();
            m_3dChart.SetAxes((float) 0.01);

            // 4. get Mesh3D array from the scatter plot
            ArrayList meshs = ((ScatterChart3D)m_3dChart).GetMeshes();

            // 5. display model vertex no and triangle no
            UpdateModelSizeInfo(meshs);

            // 6. display scatter plot in Viewport3D
            WPFChart3D.Model3D model3d = new WPFChart3D.Model3D();
            m_nChartModelIndex = model3d.UpdateModel(meshs, null, m_nChartModelIndex, this.mainViewport);

            m_transformMatrix.ResetView();

            // 7. set projection matrix
            float viewRange = 200;
            m_transformMatrix.CalculateProjectionMatrix(0, viewRange, 0, viewRange, 0, viewRange, 0.5);
            TransformChart();
        }


        // function for set a scatter plot, every dot is just a simple pyramid.
        public void TestSimpleScatterPlot(int nDotNo)
        {
            // 1. set the scatter plot size
            m_3dChart = new ScatterChart3D();
            m_3dChart.SetDataNo(nDotNo);

            // 2. set the properties of each dot
            Random randomObject = new Random();
            int nDataRange = 200;
            for (int i = 0; i < nDotNo; i++)
            {
                ScatterPlotItem plotItem = new ScatterPlotItem();

                plotItem.w = 2;
                plotItem.h = 2;

                plotItem.x = (float)randomObject.Next(nDataRange);
                plotItem.y = (float)randomObject.Next(nDataRange);
                plotItem.z = plotItem.x + plotItem.y; 

                plotItem.shape = (int)Chart3D.SHAPE.PYRAMID;

                Byte nR = (Byte)randomObject.Next(256);
                Byte nG = (Byte)randomObject.Next(256);
                Byte nB = (Byte)randomObject.Next(256);

                plotItem.color = Color.FromRgb(nR, nG, nB);
                ((ScatterChart3D)m_3dChart).SetVertex(i, plotItem);
            }
            // 3. set axes
            m_3dChart.GetDataRange();
            m_3dChart.SetAxes();

            // 4. Get Mesh3D array from scatter plot
            ArrayList meshs = ((ScatterChart3D)m_3dChart).GetMeshes();

            // 5. display vertex no and triangle no.
            UpdateModelSizeInfo(meshs);

            // 6. show 3D scatter plot in Viewport3d
            WPFChart3D.Model3D model3d = new WPFChart3D.Model3D();
            m_nChartModelIndex = model3d.UpdateModel(meshs, null, m_nChartModelIndex, this.mainViewport);

            // 7. set projection matrix
            float viewRange = (float)nDataRange;
            m_transformMatrix.CalculateProjectionMatrix(0, viewRange, 0, viewRange, 0, viewRange, 0.5);
            TransformChart();
        }


        public void OnViewportMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs args)
        {
            Point pt = args.GetPosition(mainViewport);
            if (args.ChangedButton == MouseButton.Left)         // rotate or drag 3d model
            {
                m_transformMatrix.OnLBtnDown(pt);
            }
            else if (args.ChangedButton == MouseButton.Right)   // select rect
            {
                m_selectRect.OnMouseDown(pt, mainViewport, m_nRectModelIndex);
            }
        }

        public void OnViewportMouseMove(object sender, System.Windows.Input.MouseEventArgs args)
        {
            Point pt = args.GetPosition(mainViewport);

            if (args.LeftButton == MouseButtonState.Pressed)                // rotate or drag 3d model
            {
                m_transformMatrix.OnMouseMove(pt, mainViewport);

                TransformChart();
            }
            else if (args.RightButton == MouseButtonState.Pressed)          // select rect
            {
                m_selectRect.OnMouseMove(pt, mainViewport, m_nRectModelIndex);
            }
            else
            {
                /*
                String s1;
                Point pt2 = m_transformMatrix.VertexToScreenPt(new Point3D(0.5, 0.5, 0.3), mainViewport);
                s1 = string.Format("Screen:({0:d},{1:d}), Predicated: ({2:d}, H:{3:d})", 
                    (int)pt.X, (int)pt.Y, (int)pt2.X, (int)pt2.Y);
                this.statusPane.Text = s1;
                */
            }
        }

        public void OnViewportMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs args)
        {
            Point pt = args.GetPosition(mainViewport);
            if (args.ChangedButton == MouseButton.Left)
            {
                m_transformMatrix.OnLBtnUp();
            }
            else if (args.ChangedButton == MouseButton.Right)
            {
                if (m_nChartModelIndex == -1) return;
                // 1. get the mesh structure related to the selection rect
                MeshGeometry3D meshGeometry = Model3D.GetGeometry(mainViewport, m_nChartModelIndex);
                if (meshGeometry == null) return;
              
                // 2. set selection in 3d chart
                m_3dChart.Select(m_selectRect, m_transformMatrix, mainViewport);

                // 3. update selection display
                m_3dChart.HighlightSelection(meshGeometry, Color.FromRgb(200, 200, 200));
            }
        }

        // zoom in 3d display
        public void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs args)
        {
            m_transformMatrix.OnKeyDown(args);
            TransformChart();
        }

        public void ProcessKeyDown(Key theKey)
        {
            m_transformMatrix.ProcessKey(theKey);
        }

         private void UpdateModelSizeInfo(ArrayList meshs)
        {
            int nMeshNo = meshs.Count;
            int nChartVertNo = 0;
            int nChartTriangelNo = 0;
            for (int i = 0; i < nMeshNo; i++)
            {
                nChartVertNo += ((Mesh3D)meshs[i]).GetVertexNo();
                nChartTriangelNo += ((Mesh3D)meshs[i]).GetTriangleNo();
            }
            //labelVertNo.Content = String.Format("Vertex No: {0:d}", nChartVertNo);
            //labelTriNo.Content = String.Format("Triangle No: {0:d}", nChartTriangelNo);
        }

        // this function is used to rotate, drag and zoom the 3d chart
        private void TransformChart()
        {
            if (m_nChartModelIndex == -1) return;
            ModelVisual3D visual3d = (ModelVisual3D)(this.mainViewport.Children[m_nChartModelIndex]);
            if (visual3d.Content == null) return;
            Transform3DGroup group1 = visual3d.Content.Transform as Transform3DGroup;
            group1.Children.Clear();
            group1.Children.Add(new MatrixTransform3D(m_transformMatrix.m_totalMatrix));
        }

        private void Zoom_in_Click(object sender, RoutedEventArgs e)
        {
            m_transformMatrix.ZoomIn();
            TransformChart();
        }
        private void Zoom_out_Click(object sender, RoutedEventArgs e)
        {
            m_transformMatrix.ZoomOut();
            TransformChart();
        }
        private void Zoom_reset_Click(object sender, RoutedEventArgs e)
        {
            m_transformMatrix.ZoomReset();
            TransformChart();
        }

    }
}
