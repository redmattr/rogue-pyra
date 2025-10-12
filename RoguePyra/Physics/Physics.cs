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
using RoguePyra.Entity;

namespace RoguePyra.Physics
{
    //Placeholder class for entity
    /*
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
    */

    public sealed class Physics
    {
        //List that holds all entity objects
        private List<EntityPhysical> entObj = new List<EntityPhysical>();
        //Vector definition for gravity
        private Vector2 _grav = new Vector2(0, 9.81f);
        private int SubSteps { get; set; } = 2;
        private float Time { get; set; } = 0f;
        private float Frame = 0f;

        //Take step in physics renderer
        public void PhyStep()
        {
            Time += Frame;
            float GetStep = GetStepDt();
            for (int i = 0; i < SubSteps; i++)
            {
                AddGrav();
                CalcCollisions(GetStep);
                UpdateObjects(GetStep);
            }
        }

        private void CalcCollisions(float dt)
        {
            //List<Collisions> collisions = new List<Collisions>();

            float response = 0.75f;
            int count = GetObjectCount(entObj);
            /*
            foreach (EntityPhysical obj in entObj)
            {
                foreach (EntityPhysical obj2 in entObj)
                {
                    if (obj == obj2) continue;

                    if (EntityPhysical.Shape.CIRCLE == obj.EntityShape && EntityPhysical.Shape.CIRCLE == obj2.EntityShape)
                    {
                        Vector2 vec = obj.Position - obj2.Position;

                        float dist2 = (vec.X * vec.X) + (vec.Y * vec.Y);
                        Console.WriteLine("Dist2 " + dist2);
                        float minDist = obj.radius + obj2.radius;
                        Console.WriteLine("Mind " + minDist);

                        if (dist2 < (minDist * minDist))
                        {
                            float dist = (float)Math.Sqrt(dist2);
                            Console.WriteLine("Dist " + dist);
                            Vector2 a = vec / dist;
                            Console.WriteLine("VecA " + a);
                            float ratio1 = obj.radius / (obj.radius + obj2.radius);
                            float ratio2 = obj2.radius / (obj.radius + obj2.radius);
                            float delta = 0.5f * response * (dist - minDist);

                            obj.Position -= a * (ratio2 * delta);
                            obj2.Position += a * (ratio1 * delta);
                        }
                    }
                    else
                    {
                        //TODO
                    }
                }
            }
            */
            for (int i = 0; i < count; i++)
            {
                EntityPhysical obj = entObj[i];
                for (int k = i+1; k < count; k++)
                {
                    EntityPhysical obj2 = entObj[k];
                    if (obj == obj2) continue;

                    if (EntityPhysical.Shape.CIRCLE == obj.EntityShape && EntityPhysical.Shape.CIRCLE == obj2.EntityShape)
                    {
                        Vector2 vec = obj.Position - obj2.Position;

                        float dist2 = (vec.X * vec.X) + (vec.Y * vec.Y);
                        //Console.WriteLine("Dist2 " + dist2);
                        float minDist = obj.radius + obj2.radius;
                        //Console.WriteLine("Mind " + minDist);

                        if (dist2 < (minDist * minDist))
                        {
                            float dist = (float)Math.Sqrt(dist2);
                            //Console.WriteLine("Dist " + dist);
                            Vector2 a = vec / dist;
                            //Console.WriteLine("VecA " + a);
                            float ratio1 = obj.radius / (obj.radius + obj2.radius);
                            float ratio2 = obj2.radius / (obj.radius + obj2.radius);
                            float delta = 0.5f * response * (dist - minDist);

                            obj.Position -= a * (ratio2 * delta);
                            obj2.Position += a * (ratio1 * delta);
                        }
                    }
                    else
                    {
                        //TODO
                    }
                }
            }
        }

        private void AddGrav()
        {
            foreach (var ent in entObj)
            {
                if (!ent._IsGrav) continue;

                ent.Accelerate(_grav);
            }
        }

        private void UpdateObjects(float dt)
        {
            foreach (var ent in entObj)
            {
                ent.Update(dt);
            }
        }

        public void AddEntity(EntityPhysical obj)
        {
            entObj.Add(obj);
        }
        
        public void RemoveEntity(EntityPhysical obj)
        {
            entObj.Remove(obj);
        }

        public List<EntityPhysical> GetEntities()
        {
            return entObj;
        }

        public void SetSimRate(float simRate)
        {
            Frame = 1f / simRate;
        }

        public void SetSubSteps(int subSteps)
        {
            if (subSteps < 1)
            {
                subSteps = 1;
                return;
            }

            SubSteps = subSteps;
        }

        public void SetObjVelocity(EntityPhysical entObj, Vector2 a)
        {
            entObj.SetVelocity(a, GetStepDt());
        }

        public int GetObjectCount(List<EntityPhysical> entObj)
        {
            return entObj.Count;
        }

        public float GetStepDt()
        {
            return Frame / (float)SubSteps;
        }
    }   
}
