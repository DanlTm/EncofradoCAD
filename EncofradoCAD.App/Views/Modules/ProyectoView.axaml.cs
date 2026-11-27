using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using EncofradoCAD.App.Controls;
using EncofradoCAD.App.Infrastructure;
using EncofradoCAD.App.Services;
using EncofradoCAD.App.ViewModels;

namespace EncofradoCAD.App.Views.Modules
{
    /// <summary>Módulo Proyecto: acciones de archivo (Guardar / Abrir / Nuevo) sobre el proyecto actual.</summary>
    public partial class ProyectoView : UserControl
    {
        public ProyectoView()
        {
            InitializeComponent();
        }

        private MainViewModel? Vm => DataContext as MainViewModel;
        private DrawingCanvas? Canvas => Vm?.Canvas2D;
        private Window? Ventana => TopLevel.GetTopLevel(this) as Window;

        private async void OnGuardar(object? sender, RoutedEventArgs e)
        {
            DrawingCanvas? canvas = Canvas;
            if (canvas == null)
            {
                await MessageService.ShowAsync(Ventana, "CADStudio", "No se pudo acceder al lienzo 2D.");
                return;
            }
            string? path = await PickSaveAsync("Guardar proyecto", "CADStudio", "*.encproj", "proyecto.encproj");
            if (path == null)
            {
                return;
            }
            try
            {
                ProjectCanvasStore.CanvasProjectDto dto = ProjectCanvasStore.Capture(canvas);
                ProjectCanvasStore.Save(dto, path);
                await MessageService.ShowAsync(Ventana, "CADStudio", "Proyecto guardado.");
            }
            catch (Exception ex)
            {
                await MessageService.ShowAsync(Ventana, "CADStudio", "No se pudo guardar: " + ex.Message);
            }
        }

        private async void OnAbrir(object? sender, RoutedEventArgs e)
        {
            DrawingCanvas? canvas = Canvas;
            if (canvas == null)
            {
                await MessageService.ShowAsync(Ventana, "CADStudio", "No se pudo acceder al lienzo 2D.");
                return;
            }
            string? path = await PickOpenAsync("Abrir proyecto", "CADStudio", "*.encproj");
            if (path == null)
            {
                return;
            }
            try
            {
                ProjectCanvasStore.CanvasProjectDto dto = ProjectCanvasStore.Load(path);
                ProjectCanvasStore.Apply(dto, canvas);
            }
            catch (Exception ex)
            {
                await MessageService.ShowAsync(Ventana, "CADStudio", "No se pudo abrir: " + ex.Message);
            }
        }

        private async void OnNuevo(object? sender, RoutedEventArgs e)
        {
            DrawingCanvas? canvas = Canvas;
            if (canvas == null)
            {
                await MessageService.ShowAsync(Ventana, "CADStudio", "No se pudo acceder al lienzo 2D.");
                return;
            }
            canvas.Limpiar();
            await MessageService.ShowAsync(Ventana, "CADStudio", "Proyecto nuevo: losa y columnas limpiadas.");
        }

        // ── Imagen de plano de fondo ─────────────────────────────────────────
        private async void OnAgregarImagenPlano(object? sender, RoutedEventArgs e)
        {
            DrawingCanvas? canvas = Canvas;
            if (canvas == null)
            {
                await MessageService.ShowAsync(Ventana, "CADStudio", "No se pudo acceder al lienzo 2D.");
                return;
            }
            TopLevel? top = TopLevel.GetTopLevel(this);
            if (top == null)
            {
                return;
            }
            IReadOnlyList<IStorageFile> files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Agregar imagen de plano",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Imágenes")
                    {
                        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp" }
                    }
                }
            });
            if (files.Count == 0)
            {
                return;
            }
            try
            {
                using Stream stream = await files[0].OpenReadAsync();
                var bitmap = new Bitmap(stream);
                canvas.EstablecerImagenPlano(bitmap);
                await MessageService.ShowAsync(Ventana, "CADStudio", "Imagen de plano cargada en el módulo 2D.");
            }
            catch (Exception ex)
            {
                await MessageService.ShowAsync(Ventana, "CADStudio", "No se pudo cargar la imagen: " + ex.Message);
            }
        }

        private async void OnQuitarImagenPlano(object? sender, RoutedEventArgs e)
        {
            DrawingCanvas? canvas = Canvas;
            if (canvas == null || !canvas.TieneImagenPlano)
            {
                await MessageService.ShowAsync(Ventana, "CADStudio", "No hay imagen de plano para quitar.");
                return;
            }
            canvas.QuitarImagenPlano();
            await MessageService.ShowAsync(Ventana, "CADStudio", "Imagen de plano quitada.");
        }

        // ── Convertir plano (imagen) en MODELO 2D funcional (losa + columnas) ──
        private async void OnConvertirPlanoAModelo(object? sender, RoutedEventArgs e)
        {
            DrawingCanvas? canvas = Canvas;
            if (canvas == null)
            {
                await MessageService.ShowAsync(Ventana, "CADStudio", "No se pudo acceder al lienzo 2D.");
                return;
            }
            TopLevel? top = TopLevel.GetTopLevel(this);
            if (top == null)
            {
                return;
            }

            IReadOnlyList<IStorageFile> files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Convertir plano a modelo 2D",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Imágenes")
                    {
                        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp" }
                    }
                }
            });
            if (files.Count == 0)
            {
                return;
            }

            Bitmap bitmap;
            try
            {
                using Stream stream = await files[0].OpenReadAsync();
                bitmap = new Bitmap(stream);
            }
            catch (Exception ex)
            {
                await MessageService.ShowAsync(Ventana, "CADStudio", "No se pudo abrir la imagen: " + ex.Message);
                return;
            }

            if (!PlanImageVectorizer.TryVectorizarPlano(bitmap, out List<Point> poligono,
                    out List<PlanImageVectorizer.ColumnaPlano> columnas, out string? error))
            {
                await MessageService.ShowAsync(Ventana, "CADStudio", error ?? "No se pudo procesar la imagen.");
                return;
            }

            double minX = poligono.Min(p => p.X);
            double maxX = poligono.Max(p => p.X);
            double minY = poligono.Min(p => p.Y);
            double anchoPx = maxX - minX;
            if (anchoPx < 1)
            {
                await MessageService.ShowAsync(Ventana, "CADStudio", "El contorno detectado no es válido.");
                return;
            }

            double? anchoRealM = await InputService.AskNumberAsync(
                Ventana,
                "Escala del plano",
                "¿Cuánto mide el ANCHO total del plano en metros? (para escalar el dibujo a la medida real)",
                "6.98");
            if (anchoRealM == null || anchoRealM <= 0)
            {
                return; // cancelado
            }

            double metrosPorPx = anchoRealM.Value / anchoPx;
            double ppm = canvas.PixelesPorMetro;
            const double margen = 60;

            // 1) Losa funcional (editable, lista para 3D y cálculo).
            List<Point> puntos = poligono
                .Select(p => new Point(
                    ((p.X - minX) * metrosPorPx * ppm) + margen,
                    ((p.Y - minY) * metrosPorPx * ppm) + margen))
                .ToList();
            canvas.CargarPoligonoLosa(puntos);

            // 2) Columnas funcionales detectadas en el plano (si las hay).
            int nCol = 0;
            foreach (PlanImageVectorizer.ColumnaPlano c in columnas)
            {
                double anchoMetros = c.Ancho * metrosPorPx;
                double largoMetros = c.Largo * metrosPorPx;
                if (anchoMetros <= 0.01 || largoMetros <= 0.01)
                {
                    continue;
                }
                var centroPx = new Point(
                    ((c.CentroX - minX) * metrosPorPx * ppm) + margen,
                    ((c.CentroY - minY) * metrosPorPx * ppm) + margen);
                nCol++;
                canvas.AgregarColumnaDirecta(centroPx, anchoMetros, largoMetros, $"C{nCol}");
            }

            // 3) Saltar al módulo 2D para que el usuario vea e interactúe con el modelo.
            if (Vm != null)
            {
                Vm.ModuloActivo = ModuloApp.Modelado2D;
            }

            string resumen = nCol > 0
                ? $"Plano convertido a modelo 2D funcional: losa + {nCol} columna(s). Ya puedes editarlo en 2D, verlo en 3D y calcular materiales."
                : "Plano convertido a losa 2D funcional. No se detectaron columnas en la imagen; puedes agregarlas en el módulo 2D. Ya puedes verlo en 3D y calcular materiales.";
            await MessageService.ShowAsync(Ventana, "CADStudio", resumen);
        }

        // ── Selectores de archivo ────────────────────────────────────────────
        private async Task<string?> PickSaveAsync(string titulo, string tipoNombre, string patron, string sugerido)
        {
            TopLevel? top = TopLevel.GetTopLevel(this);
            if (top == null)
            {
                return null;
            }
            IStorageFile? file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = titulo,
                SuggestedFileName = sugerido,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType(tipoNombre) { Patterns = new[] { patron } }
                }
            });
            return file?.Path.LocalPath;
        }

        private async Task<string?> PickOpenAsync(string titulo, string tipoNombre, string patron)
        {
            TopLevel? top = TopLevel.GetTopLevel(this);
            if (top == null)
            {
                return null;
            }
            IReadOnlyList<IStorageFile> files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = titulo,
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType(tipoNombre) { Patterns = new[] { patron } }
                }
            });
            return files.Count > 0 ? files[0].Path.LocalPath : null;
        }
    }
}
