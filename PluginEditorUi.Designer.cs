namespace FSPreview
{
    partial class PluginEditorUi
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be m_disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing) {
            if (disposing && (components != null)) {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent() {
            components = new System.ComponentModel.Container();
            panel1 = new Panel();
            lblTime = new Label();
            timer1 = new System.Windows.Forms.Timer(components);
            contextMenuStrip1 = new ContextMenuStrip(components);
            loadVideoToolStripMenuItem = new ToolStripMenuItem();
            fullScreenToolStripMenuItem = new ToolStripMenuItem();
            muteToolStripMenuItem = new ToolStripMenuItem();
            forceSyncToolStripMenuItem = new ToolStripMenuItem();
            openVideoFileToolStripMenuItem = new ToolStripMenuItem();
            contextMenuStrip2 = new ContextMenuStrip(components);
            contextMenuStrip2ToolStripMenuItem = new ToolStripMenuItem();
            contextMenuStrip1.SuspendLayout();
            contextMenuStrip2.SuspendLayout();
            SuspendLayout();
            // 
            // panel1
            // 
            panel1.AllowDrop = true;
            panel1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            panel1.BackColor = SystemColors.WindowText;
            panel1.Location = new Point(0, 0);
            panel1.Name = "panel1";
            panel1.Size = new Size(640, 360);
            panel1.TabIndex = 0;
            panel1.DragDrop += panel1_DragDrop;
            panel1.DragEnter += panel1_DragEnter;
            panel1.Paint += panel1_Paint;
            // 
            // lblTime
            // 
            lblTime.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            lblTime.BackColor = Color.Transparent;
            lblTime.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);
            lblTime.ForeColor = Color.White;
            lblTime.Location = new Point(3, 358);
            lblTime.Name = "lblTime";
            lblTime.Size = new Size(346, 22);
            lblTime.TabIndex = 2;
            lblTime.Text = "00:00:00 / 00:00:00 ";
            lblTime.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // timer1
            // 
            timer1.Interval = 1;
            timer1.Tick += timer1_Tick;
            // 
            // contextMenuStrip1
            // 
            contextMenuStrip1.Items.AddRange(new ToolStripItem[] { loadVideoToolStripMenuItem, fullScreenToolStripMenuItem, muteToolStripMenuItem, forceSyncToolStripMenuItem });
            contextMenuStrip1.Name = "contextMenuStrip1";
            contextMenuStrip1.Size = new Size(158, 92);
            // 
            // loadVideoToolStripMenuItem
            // 
            loadVideoToolStripMenuItem.Name = "loadVideoToolStripMenuItem";
            loadVideoToolStripMenuItem.Size = new Size(157, 22);
            loadVideoToolStripMenuItem.Text = "Open Video File";
            loadVideoToolStripMenuItem.Click += loadVideoToolStripMenuItem_Click;
            // 
            // fullScreenToolStripMenuItem
            // 
            fullScreenToolStripMenuItem.Name = "fullScreenToolStripMenuItem";
            fullScreenToolStripMenuItem.Size = new Size(157, 22);
            fullScreenToolStripMenuItem.Text = "Full Screen";
            fullScreenToolStripMenuItem.Click += fullScreenToolStripMenuItem_Click;
            // 
            // muteToolStripMenuItem
            // 
            muteToolStripMenuItem.Name = "muteToolStripMenuItem";
            muteToolStripMenuItem.Size = new Size(157, 22);
            muteToolStripMenuItem.Text = "Mute";
            muteToolStripMenuItem.Click += muteToolStripMenuItem_Click;
            // 
            // forceSyncToolStripMenuItem
            // 
            forceSyncToolStripMenuItem.Name = "forceSyncToolStripMenuItem";
            forceSyncToolStripMenuItem.Size = new Size(157, 22);
            forceSyncToolStripMenuItem.Text = "Force Sync";
            forceSyncToolStripMenuItem.Click += forceSyncToolStripMenuItem_Click;
            // 
            // openVideoFileToolStripMenuItem
            // 
            openVideoFileToolStripMenuItem.Name = "openVideoFileToolStripMenuItem";
            openVideoFileToolStripMenuItem.Size = new Size(180, 22);
            openVideoFileToolStripMenuItem.Text = "Open Video File";
            // 
            // contextMenuStrip2
            // 
            contextMenuStrip2.Items.AddRange(new ToolStripItem[] { contextMenuStrip2ToolStripMenuItem });
            contextMenuStrip2.Name = "contextMenuStrip2";
            contextMenuStrip2.Size = new Size(158, 26);
            // 
            // contextMenuStrip2ToolStripMenuItem
            // 
            contextMenuStrip2ToolStripMenuItem.Name = "contextMenuStrip2ToolStripMenuItem";
            contextMenuStrip2ToolStripMenuItem.Size = new Size(157, 22);
            contextMenuStrip2ToolStripMenuItem.Text = "Open Video File";
            // 
            // PluginEditorUi
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(40, 40, 40);
            Controls.Add(panel1);
            Controls.Add(lblTime);
            DoubleBuffered = true;
            Name = "PluginEditorUi";
            Size = new Size(640, 380);
            contextMenuStrip1.ResumeLayout(false);
            contextMenuStrip2.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private Panel panel1;
        private System.Windows.Forms.Timer timer1;
        private ContextMenuStrip contextMenuStrip1;
        private ToolStripMenuItem loadVideoToolStripMenuItem;
        private ToolStripMenuItem fullScreenToolStripMenuItem;
        private ToolStripMenuItem muteToolStripMenuItem;
        private ToolStripMenuItem forceSyncToolStripMenuItem;
        private Label lblDuration;
        private Label lblTime;
        private ToolStripMenuItem openVideoFileToolStripMenuItem;
        private ContextMenuStrip contextMenuStrip2;
        private ToolStripMenuItem contextMenuStrip2ToolStripMenuItem;
    }
}