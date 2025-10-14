using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using RoguePyra.Entity;

using WinFormsTimer = System.Windows.Forms.Timer;

namespace RoguePyra.Physics
{
    //Test environment for physics engine testing
    public sealed class PhysicsBox : Form
    {
        private const float WorldW = 840f;
        private const float WorldH = 480f;
        private const float Box = 24f;

        // Input state
        private bool _up, _left, _down, _right;

        // Rendering timer
        private readonly WinFormsTimer _renderTimer;

        //Entity list
        public List<EntityPhysical> Entities;

        private Physics phy = new Physics();

        public PhysicsBox()
        {
            Text = "Rogue-Pyra - Physics Box";
            ClientSize = new Size((int)WorldW, (int)WorldH);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            phy.SetSimRate(10); //Measured in frames per second
            phy.SetSubSteps(8);

            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            FormClosing += OnFormClosing;

            phy.AddEntity(new EntityPhysical(new Vector2(WorldW / 2, WorldH / 2), 1f, 30f, true));
            phy.AddEntity(new EntityPhysical(new Vector2(WorldW / 3, WorldH - (WorldH / 4)), 1f, 30f, true));
            phy.AddEntity(new EntityPhysical(new Vector2(WorldW / 1.5, WorldH / 1.5), 1f, 30f, true));
            //phy.AddEntity(new EntityPhysical(new Vector2(0, WorldH - 50f), 1f, WorldW, 50f, false));

            _renderTimer = new WinFormsTimer { Interval = 1 };
            _renderTimer.Tick += new EventHandler(PhysicsSimulation);
            _renderTimer.Start();
        }

        private void PhysicsSimulation(Object sender, EventArgs e)
        {
            phy.PhyStep();
            Invalidate();
        }

        /*
        private EntityPhysical CreatePlayer()
        {
            EntityPhysical player = new EntityPhysical(new Vector2(10f, 10f), new Vector2(0f, 0f), 1, true);
            return player;
        }

        private EntityPhysical AddFloor()
        {
            EntityPhysical floor = new(new Vector2(WorldW, WorldH / 4), new Vector2(0f, (0f + (WorldH / 4))), 0f, false);
            return floor;
        }
        */

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            _renderTimer?.Stop();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;

            g.Clear(Color.Black);
            using (var pen = new Pen(Color.Black, 2f))
            {
                //g.DrawRectangle(pen, 1, 1, ClientSize.Width - 2, ClientSize.Height - 24 - 2);
            }

            //Print debugging information on screen
            /*
            using (var brush = new SolidBrush(Color.White))
            {
                Font font = new Font("Arial", 14f);
                g.DrawString(GetFPS(), font, brush, 2f, 2f);
            }
            */

            //Generate constraint circle
            using (var pen = new Pen(Color.Gray, 20f))
            {
                g.DrawEllipse(pen, 240f, 240f, 240f, 240f);

            }

            //Draw objects to screen
            using (var pen = new Pen(Color.White, 1f))
            {
                List<EntityPhysical> entObj = phy.GetEntities();
                foreach (var entity in entObj)
                {
                    if (entity.EntityShape == EntityPhysical.Shape.CIRCLE)
                    {
                        g.DrawEllipse(pen, entity.Position.X, entity.Position.Y, entity.radius, entity.radius);
                        using (var brush = new SolidBrush(Color.White))
                        {
                            g.FillEllipse(brush, entity.Position.X, entity.Position.Y, entity.radius, entity.radius);
                        }
                    }
                    else if (entity.EntityShape == EntityPhysical.Shape.RECTANGLE)
                    {
                        g.DrawRectangle(pen, entity.Position.X, entity.Position.Y, entity.width, entity.height);
                        using (var brush = new SolidBrush(Color.White))
                        {
                            g.FillRectangle(brush, entity.Position.X, entity.Position.Y, entity.width, entity.height);
                        }
                    }
                }
            }
        }

        private String GetFPS()
        {
            return phy.GetStepDt().ToString();
        }
    }
}
