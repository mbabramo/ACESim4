using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

using System.Windows.Forms.Integration;
using System.Windows.Input;

namespace ACESim
{
    public partial class ScatterplotHostForm : Form
    {
        WPFChart3D.UserControl1 uc;

        public ScatterplotHostForm()
        {
            InitializeComponent();

            ElementHost host = new ElementHost();
            host.Dock = DockStyle.Fill;

            // Create the WPF UserControl.
            uc = new WPFChart3D.UserControl1();

            // Assign the WPF UserControl to the ElementHost control's
            // Child property.
            host.Child = uc;

            // Add the ElementHost control to the form's
            // collection of child controls.
            this.Controls.Add(host);
        }

        public void SetName(string name)
        {
            this.Text = name;
        }

        public void SetPoints(List<double[]> points, List<System.Windows.Media.Color> colors)
        {
            uc.CreateScatterPlot(points, colors);
        }

        private void Form1_KeyDown(object sender, System.Windows.Forms.KeyEventArgs e) {
            Key wpfKey = KeyInterop.KeyFromVirtualKey((int) e.KeyCode);
            uc.ProcessKeyDown(wpfKey);
        }


        //private void Form1_Load(object sender, EventArgs e)
        //{
        //    // Create the ElementHost control for hosting the
        //    // WPF UserControl.
        //    ElementHost host = new ElementHost();
        //    host.Dock = DockStyle.Fill;

        //    // Create the WPF UserControl.
        //    WPFChart3D.UserControl1 uc =
        //        new WPFChart3D.UserControl1();

        //    // Assign the WPF UserControl to the ElementHost control's
        //    // Child property.
        //    host.Child = uc;

        //    // Add the ElementHost control to the form's
        //    // collection of child controls.
        //    this.Controls.Add(host);
        //}
    }
}
