using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace NumberRecognitionML
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private System.Windows.Point? _previousPoint;
        private NumberRecognition _charRecon;
        private Timer _timer;
        private bool _clear;

        public MainWindow()
        {
            InitializeComponent();
            _charRecon = new NumberRecognition();

            _timer = new Timer();
            _timer.Interval = 600;
            _timer.AutoReset = false;
            _timer.Elapsed += _timer_Elapsed;

            Trace.Listeners.Clear();
            Trace.Listeners.Add(new DelegateTraceListener((message, newline) =>
            {
                string s = message + (newline ? "\n" : "");
                Dispatcher.InvokeAsync(() =>
                {
                    Log.Text += s;
                    Log.ScrollToEnd();
                });
            }));
        }

        private void DrawCanvas_OnStylusDown(object sender, StylusDownEventArgs e)
        {
            var p = e.GetPosition(DrawCanvas);
            if (_clear)
                DrawCanvas.Children.Clear();
            _previousPoint = p;
        }

        private void DrawCanvas_OnStylusMove(object sender, StylusEventArgs e)
        {
            if (_previousPoint == null)
                return;

            var p = e.GetPosition(DrawCanvas);
            var sp = e.GetStylusPoints(DrawCanvas);

            foreach (var stylusPoint in sp)
            {
                p = stylusPoint.ToPoint();
                var line = new Line()
                {
                    StrokeThickness = 40 * stylusPoint.PressureFactor,
                    Stroke = new SolidColorBrush(Colors.Black),
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    X1 = _previousPoint.Value.X,
                    Y1 = _previousPoint.Value.Y,
                    X2 = p.X,
                    Y2 = p.Y
                };

                DrawCanvas.Children.Add(line);

                _previousPoint = p;
            }

            AdjustViewBox();

            _timer.Stop();
            _timer.Start();

            _clear = false;
        }

        private void AdjustViewBox()
        {
            var actualWidth = DrawCanvas.ActualWidth;
            var actualHeight = DrawCanvas.ActualHeight;

            var x1 = DrawCanvas.Children.OfType<Line>().Min(l => l.X1 - l.StrokeThickness / 2);
            var y1 = DrawCanvas.Children.OfType<Line>().Min(l => l.Y1 - l.StrokeThickness / 2);
            var x2 = DrawCanvas.Children.OfType<Line>().Max(l => l.X2 + l.StrokeThickness / 2);
            var y2 = DrawCanvas.Children.OfType<Line>().Max(l => l.Y2 + l.StrokeThickness / 2);


            // keep aspect ratio and center it
            var width = x2 - x1;
            var height = y2 - y1;

            if (width > height)
            {
                y1 -= (width - height) / 2;
                y2 += (width - height) / 2;
            }
            else
            {
                x1 -= (height - width) / 2;
                x2 += (height - width) / 2;
            }

            //var rectangle = DrawCanvas.Children.OfType<System.Windows.Shapes.Rectangle>().FirstOrDefault();
            //if (rectangle == null)
            //{
            //    rectangle = new System.Windows.Shapes.Rectangle() { Stroke = new SolidColorBrush(Colors.Blue), StrokeThickness = 1 };
            //    DrawCanvas.Children.Add(rectangle);
            //}
            //rectangle.Width = x2 - x1;
            //rectangle.Height = y2 - y1;
            //rectangle.Margin = new Thickness(x1, y1, 0, 0);

            ResizeVisualBrush.Viewbox = new Rect(new System.Windows.Point(x1 + 2, y1 + 2), new System.Windows.Point(x2 + 2, y2 + 2));
        }
        private void DrawCanvas_OnStylusLeave(object sender, StylusEventArgs e)
        {
            _previousPoint = null;
        }


        private void DrawCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _previousPoint = e.GetPosition(DrawCanvas);
            if (_clear)
                DrawCanvas.Children.Clear();
        }

        private void DrawCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_previousPoint != null && e.StylusDevice == null)
            {
                var p = e.GetPosition(DrawCanvas);

                var line = new Line()
                {
                    StrokeThickness = 20,
                    Stroke = new SolidColorBrush(Colors.Black),
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    X1 = _previousPoint.Value.X,
                    Y1 = _previousPoint.Value.Y,
                    X2 = p.X,
                    Y2 = p.Y
                };

                DrawCanvas.Children.Add(line);

                _previousPoint = p;

                AdjustViewBox();

                _timer.Stop();
                _timer.Start();

                _clear = false;
            }
        }

        private void DrawCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.StylusDevice != null)
                return;

            _previousPoint = null;
            try
            {
                PredictDigit();
            }
            catch
            {
                Trace.WriteLine("Prediction failed. Model loaded?");
            }
        }

        private void ClearButton_OnClick(object sender, RoutedEventArgs e)
        {
            DrawCanvas.Children.Clear();
        }

        private BitmapFrame ResizeDrawCanvas()
        {
            var visual = AdjustedDrawCanvas;

            var actualWidth = visual.ActualWidth;
            var actualHeight = visual.ActualHeight;

            RenderTargetBitmap bitmap = new RenderTargetBitmap((int)actualWidth, (int)actualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var frame = CreateResizedImage(bitmap, 28, 28, 3); // MNIST pictures 28x28 but character is 22x22 with a margin
            ScaledImage.Source = frame;

            //var encoder = new PngBitmapEncoder();
            //encoder.Frames.Add(frame);
            //using (var stream = File.Create($"{Guid.NewGuid()}.png"))
            //    encoder.Save(stream);

            return frame;
        }

        private void PredictDigit()
        {
            var pixels = GetPixelArray();
            var digit = new Digit() { PixelValues = pixels };

            var predict = _charRecon.PredictDigit(digit);
            for (int i = 0; i < predict.Score.Length; i++)
                Trace.WriteLine($"{i}: {predict.Score[i] * 100:00.0000}%");

            Trace.WriteLine($" It's a {predict.PredictedNumber}");
            PredictedNunbers.Text += predict.PredictedNumber.ToString();
            PredictedCharacter.Text = predict.PredictedNumber.ToString();
        }

        private float[] GetPixelArray()
        {
            var frame = ResizeDrawCanvas();

            int height = frame.PixelHeight;
            int width = frame.PixelWidth;
            int nStride = (frame.PixelWidth * frame.Format.BitsPerPixel + 7) / 8;
            byte[] pixelByteArray = new byte[frame.PixelHeight * nStride];
            frame.CopyPixels(pixelByteArray, nStride, 0);

            float[] pixelFix = new float[height * width];
            for (int i = 0; i < pixelByteArray.Length; i += 4)
            {
                var i2 = i / 4;
                pixelFix[i2] = pixelByteArray[i + 1];
                if (pixelFix[i2] < pixelByteArray[i + 2])
                    pixelFix[i2] = pixelByteArray[i + 2];
                if (pixelFix[i2] < pixelByteArray[i + 3])
                    pixelFix[i2] = pixelByteArray[i + 3];
            }

            return pixelFix;
        }

        private static BitmapFrame CreateResizedImage(ImageSource source, int width, int height, int margin)
        {
            var rect = new Rect(margin, margin, width - margin * 2, height - margin * 2);

            var group = new DrawingGroup();
            RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.HighQuality);
            group.Children.Add(new ImageDrawing(source, rect));

            var drawingVisual = new DrawingVisual();
            using (var drawingContext = drawingVisual.RenderOpen())
                drawingContext.DrawDrawing(group);

            var resizedImage = new RenderTargetBitmap(
                width, height,         // Resized dimensions
                96, 96,                // Default DPI values
                PixelFormats.Default); // Default pixel format
            resizedImage.Render(drawingVisual);

            return BitmapFrame.Create(resizedImage);
        }

        private void TrainButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog() { Filter = "MNIST Data Files|*.csv", DefaultExt = ".csv" };
            if (openFileDialog.ShowDialog() == true)
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog() { Filter = "Model|*.zip|All|*.*", DefaultExt = ".zip", FileName = "NumberRecognitionModel.zip" };
                if (saveFileDialog.ShowDialog() == true)
                {
                    Task.Run(() => _charRecon.Train(openFileDialog.FileName, saveFileDialog.FileName));
                }
            }
        }

        private void LoadModelButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog() { Filter = "Model|*.zip|All|*.*", DefaultExt = ".zip", FileName = "NumberRecognitionModel.zip" };
            if (openFileDialog.ShowDialog() == true)
            {
                _charRecon.LoadModel(openFileDialog.FileName);
            }
        }

        private void AppendData_Click(object sender, RoutedEventArgs e)
        {
            var pixels = GetPixelArray();
            var n = int.Parse(Character.Text);
            using (var fs = File.OpenWrite("training_data.csv"))
            {
                fs.Seek(0, SeekOrigin.End);
                using (var writer = new StreamWriter(fs))
                {
                    writer.Write(n);
                    foreach (var f in pixels)
                    {
                        writer.Write(',');
                        writer.Write(f.ToString());
                    }
                    writer.WriteLine();
                }
            }
            Trace.WriteLine($" {n} added to trainging_data.csv");
            ClearButton_OnClick(sender, e);
        }

        private void _timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            _timer.Stop();
            _clear = true;
            Dispatcher.Invoke(() =>
            {
                if (PredictsButton.IsChecked == true)
                    PredictDigit();
            });
        }
    }

    public class DelegateTraceListener : TraceListener
    {
        private readonly Action<string, bool> _func;

        public DelegateTraceListener(Action<string, bool> func)
        {
            _func = func;
        }

        public override void Write(string message)
        {
            _func.Invoke(message, false);
        }

        public override void WriteLine(string message)
        {
            _func.Invoke(message, true);
        }
    }
}
