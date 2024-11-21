using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MaterialDesignColors;
using MaterialDesignThemes.Wpf;
using Revit.SDK.Samples.AutoUpdaterPro.CS;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TIGUtility;

namespace AutoUpdaterPro
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        List<string> _angleList = new List<string>() { "5.00", "11.25", "15.00", "22.50", "30.00", "45.00", "60.00", "90.00" };
        #region UI
        bool _isCancel = false;
        bool _isStoped = false;

        public async void EscFunction(Window window)
        {
            bool isValid = await loop();
            if (isValid)
            {
                _isCancel = false;
                _isStoped = false;
                window.Close();
                ExternalApplication.window = null;
            }
        }

        public async Task<bool> loop()
        {
            await Task.Run(() =>
            {
                do
                {
                    try
                    {
                        _isStoped = _isCancel ? true : _isStoped;
                    }
                    catch (Exception)
                    {
                    }
                }
                while (!_isStoped);

            });
            return _isStoped;
        }
        private void InitializeMaterialDesign()
        {
            var card = new Card();
            var hue = new Hue("Dummy", Colors.Black, Colors.White);
        }
        private void InitializeWindowProperty()
        {
            //this.Title = Util.ApplicationWindowTitle;
            this.Height = 250;
            this.Topmost = true;
            this.Width = 250;
            this.ResizeMode = System.Windows.ResizeMode.NoResize;
            this.AllowsTransparency = true;
            this.WindowStyle = WindowStyle.None;
        }

        #endregion
        public static MainWindow Instance;
        public UIApplication _UIApp = null;
        public List<ExternalEvent> _externalEvents = new List<ExternalEvent>();

        //public double angleDegree;

        public double? angleDegree { get; set; }

        public bool isoffset = false;
        public string offsetvariable;
        public List<Element> firstElement = new List<Element>();
        public Autodesk.Revit.DB.Document _document = null;
        public UIDocument _uiDocument = null;
        public UIApplication _uiApplication = null;
        public System.Windows.Point Dragposition;//dragposition in mousemove
        public bool isDragging = false;
        public bool isStaticTool = false;
        public double _left;
        public double _top;
        private bool _IsPopupOpen;
        public event PropertyChangedEventHandler PropertyChanged;

        public List<Element> _bendElements = new List<Element>();
        private bool isDrag = false;
        private System.Windows.Point mouseOffset;
        private System.Windows.Point popupInitialOffset;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public MainWindow()
        {
            InitializeWindowProperty();
            InitializeMaterialDesign();
            InitializeComponent();
            InitializeHandlers();
            Instance = this;
            //start EscFunction
            _isCancel = false;
            HotkeysManager.SetupSystemHook();
            HotkeysManager.AddHotkey(new GlobalHotkey(ModifierKeys.None, Key.Escape, () => { _isCancel = true; }));
            EscFunction(this);
            // end EscFunction
        }
        private void InitializeHandlers()
        {
            _externalEvents.Add(ExternalEvent.Create(new AngleDrawHandler()));
            _externalEvents.Add(ExternalEvent.Create(new WindowCloseHandler()));
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (ExternalApplication.ToggleConPakToolsButton.ItemText == "AutoUpdate ON")
            {
                MoveBottomRightEdgeOfWindowToMousePosition();
            }
            else
            {
                double desktopWidth = SystemParameters.WorkArea.Width;
                double desktopHeight = SystemParameters.WorkArea.Height;
                double centerX = desktopWidth / 2;
                double centerY = desktopHeight / 2;
                Left = centerX - (ActualWidth / 2);
                Top = centerY - (ActualHeight / 2);
            }
        }
        private void MoveBottomRightEdgeOfWindowToMousePosition()
        {
            var transform = PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice;
            var mouse = transform.Transform(GetMousePosition());
            Left = mouse.X - (ActualWidth - 10);
            Top = mouse.Y - (ActualHeight - 10);
        }
        public System.Windows.Point GetMousePosition()
        {
            System.Drawing.Point point = System.Windows.Forms.Control.MousePosition;
            return new System.Windows.Point(point.X, point.Y);
        }
        private void angleBtn_Click(object sender, RoutedEventArgs e)
        {
            angleDegree = Convert.ToDouble(((System.Windows.Controls.ContentControl)sender).Content);
            _externalEvents[0].Raise();
            isoffset = true;
        }
        private void popupClose_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                string s = ExternalApplication.ToggleConPakToolsButton.ItemText;
                BitmapImage OffLargeImage = new BitmapImage(new Uri("pack://application:,,,/AutoUpdaterPro;component/Resources/Auto-update-32X32-red.png"));
                BitmapImage OnLargeImage = new BitmapImage(new Uri("pack://application:,,,/AutoUpdaterPro;component/Resources/Auto-update-32X32-green.png"));

                BitmapImage OnImage = new BitmapImage(new Uri("pack://application:,,,/AutoUpdaterPro;component/Resources/Auto-update-16X16-green.png"));
                BitmapImage OffImage = new BitmapImage(new Uri("pack://application:,,,/AutoUpdaterPro;component/Resources/Auto-update-16X16-red.png"));
                if (s == "AutoUpdate ON")
                {
                    ExternalApplication.ToggleConPakToolsButton.LargeImage = OffLargeImage;
                    ExternalApplication.ToggleConPakToolsButton.Image = OffImage;
                    ExternalApplication.ToggleConPakToolsButton.ItemText = "AutoUpdate OFF";
                }
                ExternalApplication.ToggleConPakToolsButtonSample.Enabled = true;
                _externalEvents[1].Raise();
            }
            catch (Exception)
            {
            }
        }
        private void popupBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (isDrag)
            {
                System.Windows.Point currentMousePos = e.GetPosition(null);
                this.Left = currentMousePos.X - mouseOffset.X + popupInitialOffset.X;
                this.Top = currentMousePos.Y - mouseOffset.Y + popupInitialOffset.Y;
                Mouse.UpdateCursor();
            }
        }
        private void popupBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            isDrag = true;
            mouseOffset = e.GetPosition(this);
            popupInitialOffset = new System.Windows.Point(this.Left, this.Top);
            Mouse.Capture(sender as IInputElement);
        }
        private void popupBox_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            isDrag = false;
            Mouse.Capture(null);
        }
    }
}



