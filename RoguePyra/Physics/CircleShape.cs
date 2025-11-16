﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

namespace RoguePyra.Physics
{
    public sealed class CircleShape
    {
        public float Radius { get; set; } = 1f;
        public System.Drawing.Color OutlineColor { get; set; } = System.Drawing.Color.White;
        public float OutlineThickness { get; set; } = 5f;
        public Vector2 Position { get; set; } = new Vector2(0f, 0f);
        public CircleShape(float radius) 
        {
            Radius = radius;
        }
    }
}