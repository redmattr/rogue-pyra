using System;
using System.Drawing;
using System.Windows.Forms;
using RoguePyra.Net;

namespace RoguePyra
{
    public class GameForm : Form
    {
        private readonly System.Windows.Forms.Timer _render = new System.Windows.Forms.Timer { Interval = 16 }; // ~60 FPS
        private readonly UdpGameClient _cli;
        private bool _up, _left, _down, _right;

        public GameForm(UdpGameClient cli)
        {
            _cli = cli;
            Text = "RoguePyra UDP Visualizer";
            ClientSize = new Size(840, 480);
            DoubleBuffered = true;

            KeyPreview = true;
            KeyDown += (s, e) => { SetKey(e.KeyCode, true); };
            KeyUp   += (s, e) => { SetKey(e.KeyCode, false); };

            _render.Tick += (_, __) =>
            {
                _cli.SetKey(_up, _left, _down, _right);
                Invalidate();
            };
            _render.Start();
        }

        private void SetKey(Keys k, bool on)
        {
            switch (k)
            {
                case Keys.W:
                case Keys.Up: _up = on; break;
                case Keys.A:
                case Keys.Left: _left = on; break;
                case Keys.S:
                case Keys.Down: _down = on; break;
                case Keys.D:
                case Keys.Right: _right = on; break;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;

            g.Clear(Color.Black);

            // Entity from server snapshot
            float x = _cli.EntityX, y = _cli.EntityY;
            g.FillRectangle(Brushes.CornflowerBlue, x, y, 24, 24);

            // "Lava" stripe
            g.FillRectangle(Brushes.DarkRed, 0, ClientSize.Height - 30, ClientSize.Width, 30);

            using var font = new Font("Consolas", 10);
            g.DrawString("WASD to send INPUT (host logs it). Blue box moves via host SNAPSHOT.",
                         font, Brushes.White, 8, 8);
        }
    }
}