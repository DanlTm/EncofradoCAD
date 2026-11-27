using System;

namespace EncofradoCAD.Core.Geometry
{
    // ════════════════════════════════════════════════════════════════════════
    //  GEOMETRÍA NEUTRAL (independiente de plataforma)
    //
    //  Reemplaza a System.Windows.Point / System.Windows.Rect (WindowsBase) para
    //  que el motor de cálculo viva en el proyecto Core y viaje a Windows, Mac y
    //  Android sin depender de WPF. La superficie de API replica exactamente los
    //  miembros que usa el motor: X, Y, Width, Height, Left, Top, Right, Bottom,
    //  Contains(Point). La semántica es idéntica a la de WPF.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Punto 2D en metros. Struct inmutable equivalente a System.Windows.Point.</summary>
    public readonly struct Point : IEquatable<Point>
    {
        public double X { get; }
        public double Y { get; }

        public Point(double x, double y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(Point other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object? obj) => obj is Point p && Equals(p);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public override string ToString() => $"{X};{Y}";

        public static bool operator ==(Point a, Point b) => a.Equals(b);
        public static bool operator !=(Point a, Point b) => !a.Equals(b);
    }

    /// <summary>Rectángulo axis-aligned en metros. Equivalente a System.Windows.Rect.</summary>
    public readonly struct Rect : IEquatable<Rect>
    {
        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }

        public Rect(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        /// <summary>Rect a partir de dos esquinas (equivalente a Rect(Point, Point) de WPF).</summary>
        public Rect(Point a, Point b)
        {
            X = Math.Min(a.X, b.X);
            Y = Math.Min(a.Y, b.Y);
            Width = Math.Abs(a.X - b.X);
            Height = Math.Abs(a.Y - b.Y);
        }

        public double Left => X;
        public double Top => Y;
        public double Right => X + Width;
        public double Bottom => Y + Height;

        /// <summary>Inclusivo en los bordes, igual que System.Windows.Rect.Contains(Point).</summary>
        public bool Contains(Point p)
            => p.X >= Left && p.X <= Right && p.Y >= Top && p.Y <= Bottom;

        public bool Contains(double x, double y)
            => x >= Left && x <= Right && y >= Top && y <= Bottom;

        public bool Equals(Rect other)
            => X.Equals(other.X) && Y.Equals(other.Y)
            && Width.Equals(other.Width) && Height.Equals(other.Height);
        public override bool Equals(object? obj) => obj is Rect r && Equals(r);
        public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
        public override string ToString() => $"{X};{Y};{Width};{Height}";

        public static bool operator ==(Rect a, Rect b) => a.Equals(b);
        public static bool operator !=(Rect a, Rect b) => !a.Equals(b);
    }
}
