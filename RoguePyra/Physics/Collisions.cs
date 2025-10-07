using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace RoguePyra.Physics
{
    public struct PointCollisions
    {
        Vector2 PointA;
        Vector2 PointB;
        Vector2 Normal;
        float Depth;
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
