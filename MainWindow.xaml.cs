using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PlantUMLLiveViewer {
    public partial class MainWindow : Window {
        public MainWindow() {
            InitializeComponent();
            Title = App.Name;
        }

        private bool IsLoadedLiveImage { get { return cLiveImage.Source != null; } }
        public void RefreshLiveImage(string filepath) {
            try {
                var os = (cLiveImage.Source as BitmapImage)?.StreamSource;
                var img = new BitmapImage();
                var ms = new MemoryStream();
                using (var fs = new FileStream(filepath, FileMode.Open, FileAccess.Read)) {
                    fs.CopyTo(ms);
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.None;
                    img.StreamSource = ms;
                    img.EndInit();
                }
                cLiveImage.Source = img;
                cScrollViewer.Visibility = IsLoadedLiveImage ? Visibility.Visible : Visibility.Collapsed;
                cBackImage.Visibility = !IsLoadedLiveImage ? Visibility.Visible : Visibility.Collapsed;
                ResizeFit();
                os?.Dispose();
            }
            catch {
            }
        }

        #region 상태 표시줄
        public enum StatusIcon {
            OFF,
            ON,
            NOT_CHANGE,
        }
        private readonly ImageSource imgOn = new BitmapImage(new Uri("/res/on.png", UriKind.Relative));
        private readonly ImageSource imgOff = new BitmapImage(new Uri("/res/off.png", UriKind.Relative));
        public void SetStatusMsg(string msg, StatusIcon icon = StatusIcon.NOT_CHANGE) {
            cMsg.Content = msg;
            switch (icon) {
                case StatusIcon.OFF: cOnOffImage.Source = imgOff; break;
                case StatusIcon.ON: cOnOffImage.Source = imgOn; break;
            }
        }
        public void SetStatusMsg(string msg, bool success) { SetStatusMsg(msg, success ? StatusIcon.ON : StatusIcon.OFF); }
        #endregion

        #region 이벤트 핸들러
        private void OnClickedFitWidth(object sender, RoutedEventArgs e) { FitToWidth(); }
        private void OnClickedFitHeight(object sender, RoutedEventArgs e) { FitToHeight(); }
        private void OnClickedFitPage(object sender, RoutedEventArgs e) { FitToPage(); }
        private void OnClickedResetZoom(object sender, RoutedEventArgs e) { SetZoomValue(100); }
        private void OnSizeChangedScrollViewer(object sender, SizeChangedEventArgs e) { ResizeFit(); }
        private void OnClickedSave(object sender, RoutedEventArgs e) { App.Inst.Save(); }

        private void OnTextChangedZoom(object sender, TextChangedEventArgs e) {
            if (!IsLoadedLiveImage)
                return;

            if (stopOnTextChangedZoom)
                return;

            var str = cZoom.Text;
            if (str.EndsWith("%"))
                str = str.Substring(0, str.Length - 1);
            if (double.TryParse(str, out double newZoomValue)) {

                cLiveImage.Width = cLiveImage.Source.Width * newZoomValue / 100.0f;
                cLiveImage.Height = cLiveImage.Source.Height * newZoomValue / 100.0f;
                SetZoomValue(FitMode.NONE, cLiveImage.Width * 100 / cLiveImage.Source.Width, updateZoomValue: false);
            }
        }

        private void OnClickedOnOff(object sender, RoutedEventArgs e) {
            new ShowLastResult { Owner = this }.ShowDialog();
        }

        private void OnLoadedMainWindow(object sender, RoutedEventArgs e) {
            App.Inst.OnLoadedMainWindow();
        }
        #endregion

        #region Zoom 관리
        private enum FitMode {
            NONE,
            WIDTH,
            HEIGHT,
            PAGE
        }

        private FitMode fitMode = FitMode.NONE;
        private double zoomValue = 100.0;
        private bool stopOnTextChangedZoom = false;

        private void FitToWidth() {
            if (!IsLoadedLiveImage)
                return;

            cLiveImage.Width = cScrollViewer.ActualWidth - SystemParameters.VerticalScrollBarWidth;
            cLiveImage.Height = double.NaN;
            SetZoomValue(FitMode.WIDTH, cLiveImage.Width * 100 / cLiveImage.Source.Width);
        }

        private void FitToHeight() {
            if (!IsLoadedLiveImage)
                return;

            cLiveImage.Width = double.NaN;
            cLiveImage.Height = cScrollViewer.ActualHeight - SystemParameters.HorizontalScrollBarHeight;
            SetZoomValue(FitMode.HEIGHT, cLiveImage.Height * 100 / cLiveImage.Source.Height);
        }

        private void FitToPage() {
            if (!IsLoadedLiveImage)
                return;

            var vw = cScrollViewer.ActualWidth - SystemParameters.VerticalScrollBarWidth;
            var vh = cScrollViewer.ActualHeight - SystemParameters.HorizontalScrollBarHeight;
            var iw = cLiveImage.Source.Width;
            var ih = cLiveImage.Source.Height;
            var vr = vw / vh;
            var ir = iw / ih;
            if (vr >= ir) {
                // h 기준 w 계산
                cLiveImage.Width = iw * vh / ih;
                cLiveImage.Height = vh;
            }
            else {
                // w 기준 h 계산
                cLiveImage.Width = vw;
                cLiveImage.Height = ih * vw / iw;
            }
            SetZoomValue(FitMode.PAGE, cLiveImage.Width * 100 / iw);
        }

        private void SetZoomValue(double newZoomValue) {
            if (!IsLoadedLiveImage)
                return;

            cLiveImage.Width = cLiveImage.Source.Width * newZoomValue / 100.0f;
            cLiveImage.Height = cLiveImage.Source.Height * newZoomValue / 100.0f;
            SetZoomValue(FitMode.NONE, cLiveImage.Width * 100 / cLiveImage.Source.Width);
        }

        private void SetZoomValue(FitMode newFitMode, double newZoomValue, bool updateZoomValue = true) {
            fitMode = newFitMode;
            zoomValue = newZoomValue;
            if (updateZoomValue) {
                stopOnTextChangedZoom = true;
                cZoom.Text = $"{zoomValue:G4}%";
                stopOnTextChangedZoom = false;
            }
        }

        private void ResizeFit() {
            switch (fitMode) {
                case FitMode.WIDTH:
                    FitToWidth();
                    break;
                case FitMode.HEIGHT:
                    FitToHeight();
                    break;
                case FitMode.PAGE:
                    FitToPage();
                    break;
                case FitMode.NONE:
                    SetZoomValue(zoomValue);
                    break;
            }
        }
        #endregion

        #region 스크롤
        Point scrollMousePoint = new Point();
        double viewOffsetH = 1;
        double viewOffsetV = 1;
        private void OnPreviewLBDownScrollViewer(object sender, MouseButtonEventArgs e) {
            scrollMousePoint = e.GetPosition(cScrollViewer);
            viewOffsetH = cScrollViewer.HorizontalOffset;
            viewOffsetV = cScrollViewer.VerticalOffset;
            cScrollViewer.CaptureMouse();
            cScrollViewer.Cursor = Cursors.SizeAll;
        }

        private void OnPreviewLBUpScrollViewer(object sender, MouseButtonEventArgs e) {
            cScrollViewer.ReleaseMouseCapture();
            cScrollViewer.Cursor = null;
        }

        private void OnPreviewMouseMoveScrollViewer(object sender, MouseEventArgs e) {
            if (cScrollViewer.IsMouseCaptured) {
                cScrollViewer.ScrollToHorizontalOffset(viewOffsetH + (scrollMousePoint.X - e.GetPosition(cScrollViewer).X));
                cScrollViewer.ScrollToVerticalOffset(viewOffsetV + (scrollMousePoint.Y - e.GetPosition(cScrollViewer).Y));
            }
        }

        private void OnPreviewMouseWheelScrollViewer(object sender, MouseWheelEventArgs e) {
            cScrollViewer.ScrollToHorizontalOffset(cScrollViewer.HorizontalOffset + e.Delta);
        }
        #endregion
    }
}
