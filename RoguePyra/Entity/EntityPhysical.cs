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
        public Vector2 PositionPast = Vector2.Zero;
        public Vector2 Position { get; set; } //Getter | Setter for entity position
        public Vector2 Acceleration { get; set; } //Vector Acceleration for approximate movement
        public Vector2 Force { get; set; } //For physics calculations
        public float mass { get; set; } //Mass of entity
        public float radius { get; set; } //If object is circle
        public float height { get; set; } //If object is square/rectangle
        public float width { get; set; } //If object is square/rectangle
        public bool _IsGrav { get; set; } //Is entity affected by grav?
        public enum Shape { RECTANGLE, CIRCLE };
        public Shape EntityShape { get; set; } 

        //Friction Coefficients
        public float DynamicFriction { get; set; }
        public float StaticFriction { get; set; }

        public float Elasticity { get; set; } //Bounciness (when colliding)

        public EntityPhysical()
        {
            Position = Vector2.Zero;
            Acceleration = Vector2.Zero;
            Force = Vector2.Zero;
            mass = 0f;
            _IsGrav = false;
            DynamicFriction = 0f;
            StaticFriction = 0f;
            Elasticity = 0f;
            PositionPast = Position;
        }

        public EntityPhysical(Vector2 pos, Vector2 acc, float mass, bool hasGrav, float dynamicFriction, float staticFriction, float elasticity)
        {
            Position = pos;
            Acceleration = acc;
            this.mass = mass;
            _IsGrav = hasGrav;
            DynamicFriction = dynamicFriction;
            StaticFriction = staticFriction;
            Elasticity = elasticity;
            PositionPast = Position;
        }

        //Rectangle
        public EntityPhysical(Vector2 pos, float mass, float width, float height, bool hasGrav)
        {
            Position = pos;
            this.mass = mass;
            this.width = width;
            this.height = height;
            _IsGrav = hasGrav;
            DynamicFriction = 1f;
            StaticFriction = 1f;
            Elasticity = 0f;
            PositionPast = Position;
            EntityShape = Shape.RECTANGLE;
        }

        //Circle
        public EntityPhysical(Vector2 pos, float mass, float rad, bool hasGrav)
        {
            Position = pos;
            this.mass = mass;
            _IsGrav = hasGrav;
            radius = rad;
            DynamicFriction = 1f;
            StaticFriction = 1f;
            Elasticity = 0f;
            PositionPast = Position;
            EntityShape = Shape.CIRCLE;
        }

        public void Accelerate(Vector2 acc)
        {
            Acceleration += acc;
        }

        public void SetVelocity(Vector2 a, float dt)
        {
            PositionPast = Position - (a * dt);
        }

        public void AddVelocity(Vector2 a, float dt)
        {
            PositionPast -= (a * dt);
        }

        public Vector2 GetVelocity(float dt)
        {
            return (Position - PositionPast) / dt;
        }

        public void Update(float dt)
        {
            //See how much the object has moved
            Vector2 disp = Position - PositionPast;
            PositionPast = Position;

            //Verlet integration
            Position = Position + disp + Acceleration * (dt * dt);

            //Reset acceleration
            Acceleration = Vector2.Zero;
        }
    }
}
