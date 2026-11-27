using System;
using Avalonia;

namespace EncofradoCAD.App.Views.Preview3D
{
    /// <summary>
    /// Cámara ORBITAL ORTOGRÁFICA (equivalente a la OrthographicCamera de v1, pero proyectada por software).
    /// Órbita en torno a <see cref="Target"/> con azimut (Yaw) y elevación (Pitch). Es ligera: no usa GPU,
    /// solo álgebra vectorial para proyectar cada vértice a la pantalla.
    /// </summary>
    public sealed class Camera3D
    {
        /// <summary>Azimut (giro alrededor del eje Z), en radianes.</summary>
        public double Yaw = -2.43;   // ≈ -139°, reproduce el punto de vista por defecto de v1

        /// <summary>Elevación sobre el horizonte, en radianes (0 = de canto, π/2 = cenital).</summary>
        public double Pitch = 0.47;  // ≈ 27°

        /// <summary>Centro de la escena al que mira la cámara.</summary>
        public Vec3 Target;

        /// <summary>Ancho de mundo (m) que llena el lado corto del control. Menor = más "zoom".</summary>
        public double ViewWidthMeters = 10.0;

        /// <summary>Factor de zoom del usuario (rueda del ratón). 1 = encuadre por defecto.</summary>
        public double Zoom = 1.0;

        /// <summary>Desplazamiento de encuadre (pan), en píxeles de pantalla.</summary>
        public double PanX;
        public double PanY;

        private const double MinPitch = -1.55;
        private const double MaxPitch = 1.55;

        /// <summary>Encuadra la escena (como resetCamera=true en v1): centra y ajusta el zoom a la losa.</summary>
        public void Frame(double minX, double maxX, double minY, double maxY, double slabHeight)
        {
            double spanX = maxX - minX;
            double spanY = maxY - minY;
            double maxSpan = Math.Max(Math.Max(spanX, spanY), 6.0);
            double diagonal = Math.Sqrt((spanX * spanX) + (spanY * spanY));

            Target = new Vec3((minX + maxX) / 2, (minY + maxY) / 2, slabHeight / 2);
            ViewWidthMeters = Math.Max(diagonal * 1.05, maxSpan * 0.95) + (slabHeight * 0.35);
            Zoom = 1.0;
            PanX = 0;
            PanY = 0;
            Yaw = -2.43;
            Pitch = 0.47;
        }

        public void Orbit(double dYaw, double dPitch)
        {
            Yaw += dYaw;
            Pitch = Math.Clamp(Pitch + dPitch, MinPitch, MaxPitch);
        }

        public void ZoomBy(double factor)
        {
            Zoom = Math.Clamp(Zoom * factor, 0.15, 12.0);
        }

        public void Pan(double dxPixels, double dyPixels)
        {
            PanX += dxPixels;
            PanY += dyPixels;
        }

        /// <summary>Vector unitario que va del objetivo hacia la cámara (dirección "hacia el ojo").</summary>
        public Vec3 EyeDir => new(
            Math.Cos(Pitch) * Math.Cos(Yaw),
            Math.Cos(Pitch) * Math.Sin(Yaw),
            Math.Sin(Pitch));

        /// <summary>Base ortonormal de vista: (derecha, arriba, haciaOjo).</summary>
        public (Vec3 Right, Vec3 Up, Vec3 Fwd) Basis()
        {
            Vec3 fwd = EyeDir.Normalized();               // hacia el ojo
            Vec3 worldUp = new(0, 0, 1);
            Vec3 right = Vec3.Cross(worldUp, fwd).Normalized();
            if (right.Length < 1e-6)
            {
                right = new Vec3(1, 0, 0);
            }
            Vec3 up = Vec3.Cross(fwd, right).Normalized();
            return (right, up, fwd);
        }

        /// <summary>Píxeles por metro dado el tamaño del control (proyección ortográfica isométrica-libre).</summary>
        public double Scale(double controlWidth, double controlHeight)
        {
            double shortSide = Math.Min(controlWidth, controlHeight);
            double worldWidth = ViewWidthMeters / Math.Max(Zoom, 1e-6);
            return worldWidth < 1e-6 ? 1.0 : shortSide / worldWidth;
        }

        /// <summary>Proyecta un punto de mundo a pantalla. Devuelve también la profundidad (mayor = más cerca del ojo).</summary>
        public (Point Screen, double Depth) Project(Vec3 p, Vec3 right, Vec3 up, Vec3 fwd, double scale, double cw, double ch)
        {
            Vec3 rel = p - Target;
            double sx = rel.Dot(right);
            double sy = rel.Dot(up);
            double depth = rel.Dot(fwd);
            double screenX = (cw / 2) + PanX + (scale * sx);
            double screenY = (ch / 2) + PanY - (scale * sy);
            return (new Point(screenX, screenY), depth);
        }
    }
}
