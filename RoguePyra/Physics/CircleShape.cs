using System.Numerics;

namespace RoguePyra.Physics {
	internal sealed class CircleShape(float radius) {
		public float Radius { get; set; } = radius;
		public System.Drawing.Color OutlineColor { get; set; } = System.Drawing.Color.White;
		public float OutlineThickness { get; set; } = 5f;
		public Vector2 Position { get; set; } = new Vector2(0f, 0f);
	}
}