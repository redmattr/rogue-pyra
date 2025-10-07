using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace RoguePyra.Physics
{
    public sealed interface CollisionObj
    {
        protected Transforms Transform { get; set; }
        protected CollideHelper Collision { get; set; }
        protected bool _IsTriggerObj { get; set; }
        protected bool _IsDynamicObj;


    }

    public struct PointCollisions
    {
        public Vector2 PointA { get; set; }
        public Vector2 PointB { get; set; }
        public Vector2 Normal { get; set; }
        private float Depth;
        public bool IsColliding;
    }

    public struct Collisions
    {
        Entities ob1;
        Entities ob2;
        PointCollisions points;
    }

    public enum CollisionType
    {
        CIRCLE, SQUARE
    };

    public struct CollideHelper
    {
        CollisionType Type;
    }

    public struct CircleCollision
    {
        Vector2 Middle;
        float Radius;
    }
}
