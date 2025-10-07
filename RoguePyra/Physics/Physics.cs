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
    //Placeholder class for entity
   public class Entities : CollisionObj
    {
        public Vector2 Position { get; set; } //Getter|Setter for entity position
        public Vector2 Velocity { get; set; } //Vector Velocity for approximate movement
        public Vector2 Force { get; set; } //For physics calculations
        public float mass { get; set; } //Mass of entity
        public bool _IsGrav { get; set; } //Is entity affected by grav?

        //Friction Coefficients
        public float DynamicFriction { get; set; }
        public float StaticFriction { get; set; }

        public float Elasticity { get; set; } //Bounciness (when colliding)
    }

    public sealed class Physics
    {
        //List that holds all entity objects
        private List<Entities> entObj;
        //Vector definition for gravity
        private Vector2 _grav = new Vector2(0, -9.81f);

        //Take step in physics renderer
        public void PhyStep(float t)
        {
            AddGrav(t);
            CalcCollisions(t);
            MoveEntities(t);
        }

        private void MoveEntities(float t)
        {
            
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

        private void AddGrav(float t)
        {
            foreach (var ent in entObj)
            {
                if (!ent._IsGrav) continue;

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
