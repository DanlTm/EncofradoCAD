using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using EncofradoCAD.App.Views.Dialogs;

namespace EncofradoCAD.App.Controls
{
    public class EstadoColumnaUI
    {
        public Border Visual { get; set; } = null!;
        public DatosColumna Datos { get; set; } = null!;
        public Line GuiaX    { get; set; } = null!; // izq: borde tramo → borde izq columna
        public Line GuiaXDer { get; set; } = null!; // der: borde der columna → borde tramo
        public Line GuiaY    { get; set; } = null!; // arr: borde tramo → borde sup columna
        public Line GuiaYAbj { get; set; } = null!; // abj: borde inf columna → borde tramo
        public TextBlock TextoXIzq { get; set; } = null!;
        public TextBlock TextoXDer { get; set; } = null!;
        public TextBlock TextoYArr { get; set; } = null!;
        public TextBlock TextoYAbj { get; set; } = null!;
        public Button BtnLock { get; set; } = null!;
        public bool IsLocked { get; set; }
        public bool IsSelected { get; set; }

        public void OcultarGuias()
        {
            if (IsLocked) return;
            if (GuiaX != null) GuiaX.IsVisible = false;
            if (GuiaXDer != null) GuiaXDer.IsVisible = false;
            if (GuiaY != null) GuiaY.IsVisible = false;
            if (GuiaYAbj != null) GuiaYAbj.IsVisible = false;
            if (TextoXIzq != null) TextoXIzq.IsVisible = false;
            if (TextoXDer != null) TextoXDer.IsVisible = false;
            if (TextoYArr != null) TextoYArr.IsVisible = false;
            if (TextoYAbj != null) TextoYAbj.IsVisible = false;
        }
    }
}
