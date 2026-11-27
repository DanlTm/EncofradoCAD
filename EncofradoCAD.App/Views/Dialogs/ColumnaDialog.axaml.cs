using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EncofradoCAD.App.Views.Dialogs
{
    public class DatosColumna
    {
        public string Nombre { get; set; } = string.Empty;
        public double PosX { get; set; }
        public double PosY { get; set; }
        public double Ancho { get; set; }
        public double Largo { get; set; }
    }

    public partial class ColumnaDialog : Window
    {
        private const int IndicePersonalizado = 6;

        public DatosColumna? Resultado { get; private set; }

        public ColumnaDialog()
        {
            InitializeComponent();
        }

        public ColumnaDialog(double xIncial = 0, double yInicial = 0, double anchoCm = 30, double largoCm = 30, string? nombreSugerido = null) : this()
        {
            if (!string.IsNullOrWhiteSpace(nombreSugerido))
            {
                txtNombre.Text = nombreSugerido;
            }

            txtPosX.Text = xIncial.ToString("0.00");
            txtPosY.Text = yInicial.ToString("0.00");

            // Prellenar siempre los campos personalizados con el tamaño recibido, para que
            // un tamaño custom no se pierda ni se reinicie a 30x30.
            txtAncho.Text = anchoCm.ToString("0.##", CultureInfo.CurrentCulture);
            txtLargo.Text = largoCm.ToString("0.##", CultureInfo.CurrentCulture);

            cmbTamanio.SelectedIndex = IndiceTamanio(anchoCm, largoCm);
            ActualizarPanelPersonalizado();

            txtNombre.Focus();
            txtNombre.SelectAll();
        }

        private static int IndiceTamanio(double anchoCm, double largoCm)
        {
            return (anchoCm, largoCm) switch
            {
                (20, 20) => 0,
                (25, 25) => 1,
                (30, 30) => 2,
                (35, 35) => 3,
                (40, 40) => 4,
                (45, 45) => 5,
                _ => IndicePersonalizado
            };
        }

        private void CmbTamanio_SelectionChanged(object? sender, SelectionChangedEventArgs e)
            => ActualizarPanelPersonalizado();

        private void ActualizarPanelPersonalizado()
        {
            if (PanelPersonalizado != null)
            {
                PanelPersonalizado.IsVisible = cmbTamanio.SelectedIndex == IndicePersonalizado;
            }
        }

        private void BtnAceptar_Click(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtNombre.Text))
            {
                MostrarError("El nombre es requerido.");
                return;
            }

            if (!double.TryParse(txtPosX.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out double posX) ||
                !double.TryParse(txtPosY.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out double posY))
            {
                MostrarError("Revise que la posicion sea valida.");
                return;
            }

            if (cmbTamanio.SelectedIndex < 0)
            {
                MostrarError("Seleccione un tamano de columna.");
                return;
            }

            if (!TryGetTamanioColumna(out double anchoCm, out double largoCm))
            {
                MostrarError("Ingrese un ancho y un largo validos (numeros mayores a 0).");
                return;
            }

            Resultado = new DatosColumna
            {
                Nombre = txtNombre.Text!,
                PosX = posX,
                PosY = posY,
                Ancho = anchoCm / 100.0,
                Largo = largoCm / 100.0,
            };

            Close(true);
        }

        private bool TryGetTamanioColumna(out double anchoCm, out double largoCm)
        {
            if (cmbTamanio.SelectedIndex == IndicePersonalizado)
            {
                bool okAncho = double.TryParse(txtAncho.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out anchoCm);
                bool okLargo = double.TryParse(txtLargo.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out largoCm);
                return okAncho && okLargo && anchoCm > 0 && largoCm > 0 && anchoCm <= 500 && largoCm <= 500;
            }

            (anchoCm, largoCm) = cmbTamanio.SelectedIndex switch
            {
                0 => (20, 20),
                1 => (25, 25),
                2 => (30, 30),
                3 => (35, 35),
                4 => (40, 40),
                5 => (45, 45),
                _ => (30, 30)
            };
            return true;
        }

        private void MostrarError(string mensaje)
        {
            txtError.Text = mensaje;
            ErrorBorder.IsVisible = true;
        }

        private void BtnCancelar_Click(object? sender, RoutedEventArgs e)
        {
            Close(false);
        }
    }
}
