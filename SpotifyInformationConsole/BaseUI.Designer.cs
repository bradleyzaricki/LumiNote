
namespace SpotifyInformationConsole
{
    partial class BaseUI
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
            this.currentlyPlayingTrackLabel = new System.Windows.Forms.Label();
            this.Button_PauseTrack = new System.Windows.Forms.Button();
            this.Button_NextTrack = new System.Windows.Forms.Button();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.Button_ResumeTrack = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // currentlyPlayingTrackLabel
            // 
            this.currentlyPlayingTrackLabel.BackColor = System.Drawing.SystemColors.ActiveBorder;
            this.currentlyPlayingTrackLabel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.currentlyPlayingTrackLabel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.currentlyPlayingTrackLabel.Font = new System.Drawing.Font("Yu Gothic UI", 26.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.currentlyPlayingTrackLabel.Location = new System.Drawing.Point(-1, 355);
            this.currentlyPlayingTrackLabel.MaximumSize = new System.Drawing.Size(850, 100);
            this.currentlyPlayingTrackLabel.Name = "currentlyPlayingTrackLabel";
            this.currentlyPlayingTrackLabel.Size = new System.Drawing.Size(677, 96);
            this.currentlyPlayingTrackLabel.TabIndex = 0;
            this.currentlyPlayingTrackLabel.Text = "Now playing Three bedrooms in a good neighborhood";
            // 
            // Button_PauseTrack
            // 
            this.Button_PauseTrack.Location = new System.Drawing.Point(12, 41);
            this.Button_PauseTrack.Name = "Button_PauseTrack";
            this.Button_PauseTrack.Size = new System.Drawing.Size(97, 23);
            this.Button_PauseTrack.TabIndex = 1;
            this.Button_PauseTrack.Text = "Pause Track";
            this.Button_PauseTrack.UseVisualStyleBackColor = true;
            this.Button_PauseTrack.Click += new System.EventHandler(this.Button_PauseTrack_Click);
            // 
            // Button_NextTrack
            // 
            this.Button_NextTrack.Location = new System.Drawing.Point(12, 12);
            this.Button_NextTrack.Name = "Button_NextTrack";
            this.Button_NextTrack.Size = new System.Drawing.Size(97, 23);
            this.Button_NextTrack.TabIndex = 2;
            this.Button_NextTrack.Text = "Next Track";
            this.Button_NextTrack.UseVisualStyleBackColor = true;
            this.Button_NextTrack.Click += new System.EventHandler(this.Button_NextTrack_Click);
            // 
            // pictureBox1
            // 
            this.pictureBox1.Location = new System.Drawing.Point(673, 323);
            this.pictureBox1.Margin = new System.Windows.Forms.Padding(0);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(128, 128);
            this.pictureBox1.TabIndex = 3;
            this.pictureBox1.TabStop = false;
            // 
            // Button_ResumeTrack
            // 
            this.Button_ResumeTrack.Location = new System.Drawing.Point(12, 70);
            this.Button_ResumeTrack.Name = "Button_ResumeTrack";
            this.Button_ResumeTrack.Size = new System.Drawing.Size(97, 23);
            this.Button_ResumeTrack.TabIndex = 4;
            this.Button_ResumeTrack.Text = "Resume Track";
            this.Button_ResumeTrack.UseVisualStyleBackColor = true;
            this.Button_ResumeTrack.Click += new System.EventHandler(this.Button_ResumeTrack_Click);
            // 
            // BaseUI
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.ControlBox = false;
            this.Controls.Add(this.Button_ResumeTrack);
            this.Controls.Add(this.pictureBox1);
            this.Controls.Add(this.Button_NextTrack);
            this.Controls.Add(this.Button_PauseTrack);
            this.Controls.Add(this.currentlyPlayingTrackLabel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Name = "BaseUI";
            this.Text = "BaseUI";
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        public System.Windows.Forms.Label currentlyPlayingTrackLabel;
        private System.Windows.Forms.Button Button_PauseTrack;
        private System.Windows.Forms.Button Button_NextTrack;
        public System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.Button Button_ResumeTrack;
    }
}