using System.Numerics;
using RoguePyra.Entity;

namespace RoguePyra.Physics {
	internal class CollisionObj {
		protected Transforms Transform { get; set; }
		protected CollideHelper Collision { get; set; }
		protected bool IsTriggerObj { get; set; }
		protected bool IsDynamicObj;
	}

	internal struct PointCollisions {
		public Vector2 PointA { get; set; }
		public Vector2 PointB { get; set; }
		public Vector2 Normal { get; set; }
		private float Depth;
		public bool IsColliding;
	}

	internal struct Collisions {
		EntityPhysical ob1;
		EntityPhysical ob2;
		PointCollisions points;
	}

	internal enum CollisionType {
		CIRCLE, SQUARE
	};

	internal struct CollideHelper {
		CollisionType Type;
	}

	internal struct CircleCollision {
		Vector2 Middle;
		float Radius;
	}
}