using System;
using Avalonia;
using Avalonia.Media.Imaging;
using EncofradoCAD.App.Controls;

namespace EncofradoCAD.App.Services
{
    /// <summary>Exporta el plano 2D (el lienzo tal como se ve) a PNG usando el renderizador de Avalonia.</summary>
    public static class CanvasImageExporter
    {
        public static void ExportPng(DrawingCanvas canvas, string path)
        {
            double w = canvas.Bounds.Width;
            double h = canvas.Bounds.Height;
            if (w < 1 || h < 1)
            {
                w = 1200;
                h = 800;
            }

            var pixelSize = new PixelSize((int)Math.Ceiling(w), (int)Math.Ceiling(h));
            using var rtb = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
            rtb.Render(canvas);
            rtb.Save(path);
        }
    }
}
