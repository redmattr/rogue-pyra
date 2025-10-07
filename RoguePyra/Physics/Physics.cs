// -----------------------------------------------------------------------------
// Physics.cs  (place in: Physics/) WIP
// -----------------------------------------------------------------------------
// Purpose
// Comprehensive physics engine that all entities will use in the game.
// - Contains definitions for gravity, movement speed, and more.
// - Contains math and algorithms for different events (such as projectiles)
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using System.Reflection.Metadata.Ecma335;

namespace RoguePyra.Physics
{
   public class Entities
    {
        public Vector2 Position { get; set; }
        public Vector2 Velocity { get; set; }
        public Vector2 Force { get; set; }
        public float mass { get; set; }

        public CollideHelper Collide;
        public Transforms Transform;
    }

    public sealed class Physics
    {
        public List<Entities> entObj;
        Vector2 _grav = new Vector2(0, -9.81f);

        public Physics() 
        { 
            
        }

        public void PhyStep(float t)
        {

            CalcCollisions(t);

            foreach (Entities obj in entObj)
            {
                // F = m * a
                obj.Force += obj.mass * _grav;

                // V = V0 + F / m * t
                // X = X0 + v * t
                obj.Velocity += obj.Force / obj.mass * t;
                obj.Position += obj.Velocity * t;

                //Reset net force
                obj.Force = new Vector2(0, 0);
            }
        }

        private void CalcCollisions(float t)
        {
            List<Collisions> collisions = new List<Collisions>();

            foreach (Entities obj in entObj)
            {
                foreach (Entities obj2 in entObj)
                {
                    if (obj == obj2)
                    {
                        break;
                    }

                    if (!obj.Collide || !obj2.Collide)
                    {
                        continue;
                    }

                    PointCollisions Point = CollisionTest(obj.Collide, obj.Transform, obj2.Collide, obj2.Transform);

                    if (Point.IsColliding)
                    {
                        collisions;
                    }
                }
            }
        }

        public void AddEntity(Entities obj)
        {
            entObj.Add(obj);
        }
        
        public void RemoveEntity(Entities obj)
        {
            entObj.Remove(obj);
        }

    }   
}
