using System;
using Avalonia.Media;

namespace EncofradoCAD.App.Views.Preview3D
{
    /// <summary>Estilo de visualización del preview 3D (como los "estilos visuales" de AutoCAD).</summary>
    public enum Modo3D
    {
        Solido,
        Transparente,
        Alambrico
    }

    /// <summary>Tipo de pieza, para dar trato especial en modo alámbrico (columnas sólidas, tableros translúcidos).</summary>
    public enum PiezaTipo
    {
        Estructura,
        Columna,
        Tablero
    }

    /// <summary>
    /// Cotas vivas del ARMADO MANUAL (metros). <c>Izquierda/Derecha/Inferior/Superior</c> son la holgura
    /// entre la caja del armado y el borde de la losa: si son iguales dos a dos, el armado está centrado.
    /// Un valor negativo significa que el armado sobresale de la losa por ese lado.
    /// </summary>
    public readonly record struct MedidasArmado(
        double Ancho,
        double Largo,
        double Diagonal,
        double Izquierda,
        double Derecha,
        double Inferior,
        double Superior,
        int Puntales,
        int Piezas,
        int PuntalesSobreColumna);

    /// <summary>Vector/punto 3D en doble precisión (Z hacia arriba, metros). Neutral, sin dependencias de plataforma.</summary>
    public readonly struct Vec3
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public Vec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);

        public double Dot(Vec3 b) => (X * b.X) + (Y * b.Y) + (Z * b.Z);
        public static Vec3 Cross(Vec3 a, Vec3 b)
            => new((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));

        public double Length => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

        public Vec3 Normalized()
        {
            double l = Length;
            return l < 1e-9 ? this : new Vec3(X / l, Y / l, Z / l);
        }
    }

    /// <summary>Cara plana (quad) con su color ya resuelto y su normal en coordenadas de mundo.</summary>
    public sealed class Face3D
    {
        public Vec3 A, B, C, D;
        public Vec3 Normal;
        public Color Color;

        /// <summary>true = geometría FIJA (plano 2D, voladizo, columnas): NO se mueve con el arrastre de
        /// estructura. false (por defecto) = encofrado movible.</summary>
        public bool Fixed;

        public Face3D(Vec3 a, Vec3 b, Vec3 c, Vec3 d, Vec3 normal, Color color, bool fixedInWorld = false)
        {
            A = a; B = b; C = c; D = d; Normal = normal; Color = color; Fixed = fixedInWorld;
        }
    }

    /// <summary>Segmento de arista (para el modo alámbrico, el contorno de la losa y el punteado de tableros).</summary>
    public sealed class Edge3D
    {
        public Vec3 A, B;
        public Color Color;
        public double Thickness;
        public bool Dashed;

        /// <summary>true = arista FIJA (contorno de losa / voladizo): no se mueve con el arrastre de estructura.</summary>
        public bool Fixed;

        public Edge3D(Vec3 a, Vec3 b, Color color, double thickness, bool dashed, bool fixedInWorld = false)
        {
            A = a; B = b; Color = color; Thickness = thickness; Dashed = dashed; Fixed = fixedInWorld;
        }
    }

    /// <summary>Pieza medible: su etiqueta y la cara superior (4 vértices) para el hit-test del tooltip.</summary>
    public sealed class HoverItem
    {
        public string Label = string.Empty;
        public Vec3 T0, T1, T2, T3; // cara superior (para hit-test en pantalla)

        /// <summary>true = pieza FIJA (p. ej. columna): su hit-test no usa el offset de estructura.</summary>
        public bool Fixed;
    }
}
