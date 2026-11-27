using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using EncofradoCAD.App.Views.Dialogs;

namespace EncofradoCAD.App.Controls
{
    public class PolygonInteractionHandler
    {
        private readonly Canvas _canvas;
        private readonly PolygonManager _polygonManager;
        private readonly PolygonGeometryService _geometryService;
        private readonly InteractionManager _interactionManager;
        private readonly double _pixelesPorMetro;

        private static readonly IBrush VerdeLima = new SolidColorBrush(Color.FromRgb(0xB6, 0xFF, 0x00));
        private static readonly IBrush FondoTexto = new SolidColorBrush(Color.FromArgb(180, 30, 30, 30));

        public PolygonInteractionHandler(
            Canvas canvas,
            PolygonManager polygonManager,
            PolygonGeometryService geometryService,
            InteractionManager interactionManager,
            double pixelesPorMetro)
        {
            _canvas = canvas;
            _polygonManager = polygonManager;
            _geometryService = geometryService;
            _interactionManager = interactionManager;
            _pixelesPorMetro = pixelesPorMetro;
        }

        public void AdjuntarEventosSegmento(Shape pathItem, Point p1, Point p2, TipoSegmento tipo, int index)
        {
            pathItem.PointerPressed += (s, e) =>
            {
                if (!e.GetCurrentPoint(pathItem).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                if (!_polygonManager.IsDrawingMode && _interactionManager.VerticeEnEdicion == -1 && _interactionManager.SegmentoEnMovimiento == -1)
                {
                    _interactionManager.SegmentoEnMovimiento = index;
                    _interactionManager.PosicionInicialSegmento = e.GetPosition(_canvas);
                    e.Pointer.Capture(pathItem);
                }
            };

            pathItem.PointerEntered += (s, e) => pathItem.Stroke = Brushes.Yellow;
            pathItem.PointerExited += (s, e) => pathItem.Stroke = VerdeLima; // vuelve a verde lima neón
        }

        public void AdjuntarEventosTexto(TextBlock txt, Point p1, Point p2, TipoSegmento tipo, int index)
        {
            txt.PointerPressed += async (s, e) =>
            {
                if (!e.GetCurrentPoint(txt).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                if (!_polygonManager.IsDrawingMode)
                {
                    await AbrirDialogoMedida(p1, p2, tipo, index);
                }
            };

            txt.PointerEntered += (s, e) => txt.Background = Brushes.LightBlue;
            txt.PointerExited += (s, e) => txt.Background = FondoTexto;
        }

        private async Task AbrirDialogoMedida(Point p1, Point p2, TipoSegmento tipo, int index)
        {
            double distPx = (tipo == TipoSegmento.Recto) ? GeometryHelper.Distancia(p1, p2) : GeometryHelper.CalcularLargoArco(p1, p2, tipo);
            double distActual = distPx / _pixelesPorMetro;

            var dialog = new MedidaSegmentoDialog(distActual);
            var owner = TopLevel.GetTopLevel(_canvas) as Window;

            bool aceptado = owner != null && await dialog.ShowDialog<bool>(owner);
            if (aceptado)
            {
                double nuevaMedidaMetros = dialog.ValorIngresado;

                if (!_interactionManager.EscalaEstablecida)
                {
                    double factorEscala = nuevaMedidaMetros / distActual;
                    _geometryService.AplicarEscalaGlobal(factorEscala);
                    _interactionManager.EscalaEstablecida = true;
                }
                else
                {
                    _geometryService.AplicarDeformacion(index, tipo, nuevaMedidaMetros * _pixelesPorMetro);
                }

                _interactionManager.RedibujarPoligono();
            }
        }
    }
}
