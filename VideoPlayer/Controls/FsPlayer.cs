﻿using FSPreview.MediaPlayer;

namespace FSPreview.Controls
{
    public partial class FsPlayer : UserControl
    {
        public Player Player { get; internal set; }

        public FsPlayer()
        {
            BackColor   = Color.Yellow;
        }

        Point oldLocation = Point.Empty;
        Size oldSize = Size.Empty;
        FormBorderStyle oldStyle = FormBorderStyle.None;
        Control oldParent;

        public bool FullScreen()
        {
            if (ParentForm == null) return false;

            oldStyle = ParentForm.FormBorderStyle;
            oldLocation = Location;
            oldSize = Size;
            oldParent = Parent;

            ParentForm.FormBorderStyle = FormBorderStyle.None;
            ParentForm.WindowState = FormWindowState.Maximized;
            Parent = ParentForm;
            Location = new Point(0, 0);
            Size = ParentForm.ClientSize;

            BringToFront();
            Focus();

            return true;
        }

        public bool NormalScreen()
        {
            if (ParentForm == null) {
                return false;
            }
            ParentForm.FormBorderStyle = oldStyle;
            ParentForm.WindowState = FormWindowState.Normal;
            Parent = oldParent;
            Location = oldLocation;
            Size = oldSize;
            Focus();
            return true;
        }

        protected override bool IsInputKey(Keys keyData) { return Player != null && Player.VideoView == null; } // Required to allow keybindings such as arrows etc.
        
        protected override void OnPaintBackground(PaintEventArgs pe)
        {
            if (Player != null && Player.renderer != null)
                Player?.renderer?.Present(); 
            else
                base.OnPaintBackground(pe);
        }  
        
        protected override void OnPaint(PaintEventArgs pe) { }
    }
}
