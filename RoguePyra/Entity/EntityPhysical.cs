using RoguePyra.Physics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace RoguePyra.Entity
{
    public class EntityPhysical : CollisionObj
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
}
