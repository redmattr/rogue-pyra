using System.Numerics;

using RoguePyra.Physics;

namespace RoguePyra.Entity {
	internal class EntityPhysical : CollisionObj {
		public Vector2 PositionPast = Vector2.Zero;
		public Vector2 Position { get; set; } // Getter | Setter for entity position
		public Vector2 Acceleration { get; set; } // Vector Acceleration for approximate movement
		public Vector2 Force { get; set; } // For physics calculations
		public float Mass { get; set; } // Mass of entity
		public float Radius { get; set; } // If object is circle
		public float Height { get; set; } // If object is square/rectangle
		public float Width { get; set; } // If object is square/rectangle
		public bool IsGrav { get; set; } // Is entity affected by grav?
		public bool IsMov { get; set; } // Is entity rigid? E.g. can it be moved (think wall or floor)
		public enum Shape { RECTANGLE, CIRCLE };
		public Shape EntityShape { get; set; }

		//Friction Coefficients
		public float DynamicFriction { get; set; }
		public float StaticFriction { get; set; }

		public float Elasticity { get; set; } //Bounciness (when colliding)

		public EntityPhysical() {
			Position = Vector2.Zero;
			Acceleration = Vector2.Zero;
			Force = Vector2.Zero;
			Mass = 0f;
			IsGrav = false;
			DynamicFriction = 0f;
			StaticFriction = 0f;
			Elasticity = 0f;
			PositionPast = Position;
		}

		public EntityPhysical(Vector2 pos, Vector2 acc, float mass, bool hasGrav, float dynamicFriction, float staticFriction, float elasticity) {
			Position = pos;
			Acceleration = acc;
			this.Mass = mass;
			IsGrav = hasGrav;
			DynamicFriction = dynamicFriction;
			StaticFriction = staticFriction;
			Elasticity = elasticity;
			PositionPast = Position;
		}

		//Rectangle
		public EntityPhysical(Vector2 pos, float mass, float width, float height, bool hasGrav, bool isMov) {
			Position = pos;
			this.Mass = mass;
			this.Width = width;
			this.Height = height;
			IsGrav = hasGrav;
			IsMov = isMov;
			DynamicFriction = 1f;
			StaticFriction = 1f;
			Elasticity = 0f;
			PositionPast = Position;
			EntityShape = Shape.RECTANGLE;
		}

		//Circle
		public EntityPhysical(Vector2 pos, float mass, float rad, bool hasGrav, bool isMov) {
			Position = pos;
			this.Mass = mass;
			IsGrav = hasGrav;
			IsMov = isMov;
			Radius = rad;
			DynamicFriction = 1f;
			StaticFriction = 1f;
			Elasticity = 0f;
			PositionPast = Position;
			EntityShape = Shape.CIRCLE;
		}

		public void Accelerate(Vector2 acc) => Acceleration += acc;

		public void SetVelocity(Vector2 a, float dt) => PositionPast = Position - (a * dt);

		public void AddVelocity(Vector2 a, float dt) => PositionPast -= (a * dt);

		public Vector2 GetVelocity(float dt) => (Position - PositionPast) / dt;

		public void Update(float dt) {
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