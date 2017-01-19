namespace ACESim
{
    partial class Window
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.settingsFileLabel = new System.Windows.Forms.Label();
            this.browseButton = new System.Windows.Forms.Button();
            this.runButton = new System.Windows.Forms.Button();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.progressLabel = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.pauseOrContinueButton = new System.Windows.Forms.Button();
            this.stopButton = new System.Windows.Forms.Button();
            this.openFileDialog1 = new System.Windows.Forms.OpenFileDialog();
            this.settingsFileNameTextBox = new System.Windows.Forms.TextBox();
            this.outputTextBox = new System.Windows.Forms.TextBox();
            this.stopSoonButton = new System.Windows.Forms.Button();
            this.resumeProgress = new System.Windows.Forms.CheckBox();
            this.saveProgress = new System.Windows.Forms.CheckBox();
            this.SuspendLayout();
            // 
            // settingsFileLabel
            // 
            this.settingsFileLabel.AutoSize = true;
            this.settingsFileLabel.Location = new System.Drawing.Point(12, 17);
            this.settingsFileLabel.Name = "settingsFileLabel";
            this.settingsFileLabel.Size = new System.Drawing.Size(67, 13);
            this.settingsFileLabel.TabIndex = 0;
            this.settingsFileLabel.Text = "Settings File:";
            // 
            // browseButton
            // 
            this.browseButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.browseButton.Location = new System.Drawing.Point(669, 12);
            this.browseButton.Name = "browseButton";
            this.browseButton.Size = new System.Drawing.Size(75, 23);
            this.browseButton.TabIndex = 1;
            this.browseButton.Text = "Browse...";
            this.browseButton.UseVisualStyleBackColor = true;
            this.browseButton.Click += new System.EventHandler(this.browseButton_Click);
            // 
            // runButton
            // 
            this.runButton.Location = new System.Drawing.Point(12, 55);
            this.runButton.Name = "runButton";
            this.runButton.Size = new System.Drawing.Size(75, 23);
            this.runButton.TabIndex = 4;
            this.runButton.Text = "Run Settings File";
            this.runButton.UseVisualStyleBackColor = true;
            this.runButton.Click += new System.EventHandler(this.runButton_Click);
            // 
            // progressBar
            // 
            this.progressBar.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.progressBar.Location = new System.Drawing.Point(78, 96);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(666, 23);
            this.progressBar.TabIndex = 5;
            // 
            // progressLabel
            // 
            this.progressLabel.AutoSize = true;
            this.progressLabel.Location = new System.Drawing.Point(18, 101);
            this.progressLabel.Name = "progressLabel";
            this.progressLabel.Size = new System.Drawing.Size(51, 13);
            this.progressLabel.TabIndex = 6;
            this.progressLabel.Text = "Progress:";
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(12, 129);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(42, 13);
            this.label6.TabIndex = 8;
            this.label6.Text = "Output:";
            // 
            // pauseOrContinueButton
            // 
            this.pauseOrContinueButton.Location = new System.Drawing.Point(605, 55);
            this.pauseOrContinueButton.Name = "pauseOrContinueButton";
            this.pauseOrContinueButton.Size = new System.Drawing.Size(139, 23);
            this.pauseOrContinueButton.TabIndex = 9;
            this.pauseOrContinueButton.Text = "Pause/Continue";
            this.pauseOrContinueButton.UseVisualStyleBackColor = true;
            this.pauseOrContinueButton.Visible = false;
            this.pauseOrContinueButton.Click += new System.EventHandler(this.pauseOrContinueButton_Click);
            // 
            // stopButton
            // 
            this.stopButton.Location = new System.Drawing.Point(310, 55);
            this.stopButton.Name = "stopButton";
            this.stopButton.Size = new System.Drawing.Size(75, 23);
            this.stopButton.TabIndex = 10;
            this.stopButton.Text = "Stop";
            this.stopButton.UseVisualStyleBackColor = true;
            this.stopButton.Click += new System.EventHandler(this.stopButton_Click);
            // 
            // openFileDialog1
            // 
            this.openFileDialog1.FileName = "openFileDialog1";
            this.openFileDialog1.Title = "Open XML file";
            // 
            // settingsFileNameTextBox
            // 
            this.settingsFileNameTextBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.settingsFileNameTextBox.Location = new System.Drawing.Point(78, 14);
            this.settingsFileNameTextBox.Name = "settingsFileNameTextBox";
            this.settingsFileNameTextBox.ReadOnly = true;
            this.settingsFileNameTextBox.Size = new System.Drawing.Size(585, 20);
            this.settingsFileNameTextBox.TabIndex = 11;
            // 
            // outputTextBox
            // 
            this.outputTextBox.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.outputTextBox.Location = new System.Drawing.Point(12, 145);
            this.outputTextBox.Multiline = true;
            this.outputTextBox.Name = "outputTextBox";
            this.outputTextBox.ReadOnly = true;
            this.outputTextBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.outputTextBox.Size = new System.Drawing.Size(732, 515);
            this.outputTextBox.TabIndex = 12;
            // 
            // stopSoonButton
            // 
            this.stopSoonButton.Location = new System.Drawing.Point(391, 55);
            this.stopSoonButton.Name = "stopSoonButton";
            this.stopSoonButton.Size = new System.Drawing.Size(187, 23);
            this.stopSoonButton.TabIndex = 13;
            this.stopSoonButton.Text = "Stop Optimizing After This Step";
            this.stopSoonButton.UseVisualStyleBackColor = true;
            this.stopSoonButton.Click += new System.EventHandler(this.stopSoonButton_Click);
            // 
            // resumeProgress
            // 
            this.resumeProgress.AutoSize = true;
            this.resumeProgress.Location = new System.Drawing.Point(194, 61);
            this.resumeProgress.Name = "resumeProgress";
            this.resumeProgress.Size = new System.Drawing.Size(109, 17);
            this.resumeProgress.TabIndex = 14;
            this.resumeProgress.Text = "Resume Progress";
            this.resumeProgress.UseVisualStyleBackColor = true;
            this.resumeProgress.Click += new System.EventHandler(this.resumeProgress_Click);
            // 
            // saveProgress
            // 
            this.saveProgress.AutoSize = true;
            this.saveProgress.Location = new System.Drawing.Point(93, 61);
            this.saveProgress.Name = "saveProgress";
            this.saveProgress.Size = new System.Drawing.Size(95, 17);
            this.saveProgress.TabIndex = 15;
            this.saveProgress.Text = "Save Progress";
            this.saveProgress.UseVisualStyleBackColor = true;
            // 
            // Window
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(756, 672);
            this.Controls.Add(this.saveProgress);
            this.Controls.Add(this.resumeProgress);
            this.Controls.Add(this.stopSoonButton);
            this.Controls.Add(this.outputTextBox);
            this.Controls.Add(this.settingsFileNameTextBox);
            this.Controls.Add(this.stopButton);
            this.Controls.Add(this.pauseOrContinueButton);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.progressLabel);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.runButton);
            this.Controls.Add(this.browseButton);
            this.Controls.Add(this.settingsFileLabel);
            this.Name = "Window";
            this.Text = "ACESim";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label settingsFileLabel;
        private System.Windows.Forms.Button browseButton;
        private System.Windows.Forms.Button runButton;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.Label progressLabel;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Button pauseOrContinueButton;
        private System.Windows.Forms.Button stopButton;
        private System.Windows.Forms.OpenFileDialog openFileDialog1;
        private System.Windows.Forms.TextBox settingsFileNameTextBox;
        private System.Windows.Forms.TextBox outputTextBox;
        private System.Windows.Forms.Button stopSoonButton;
        private System.Windows.Forms.CheckBox resumeProgress;
        private System.Windows.Forms.CheckBox saveProgress;
    }
}