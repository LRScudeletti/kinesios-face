using Microsoft.Azure.CognitiveServices.Vision.Face;
using Microsoft.Azure.CognitiveServices.Vision.Face.Models;
using Microsoft.Azure.Kinect.Sensor;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KinesiOSFace
{
    public partial class MainWindow : INotifyPropertyChanged
    {
        private readonly Device _device;
        private readonly WriteableBitmap _writeableBitmap;

        private readonly FaceClient _faceClient;
        private DetectedFace? _detectedFace;

        private string? _emotionText;

        // Get your key and url at https://azure.microsoft.com/en-us/services/cognitive-services/ 
        private const string? FaceClientKey = "INPUT YOUR KEY";
        private const string? CognitiveServicesUrl = "INPUT YOUR URL";

        private static readonly List<FaceAttributeType>? AttributesList = new() { FaceAttributeType.Emotion };

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool Start;

        public MainWindow()
        {
            InitializeComponent();

            _device = Device.Open();

            _device.StartCameras(new DeviceConfiguration
            {
                ColorFormat = ImageFormat.ColorBGRA32,
                ColorResolution = ColorResolution.R720p,
                DepthMode = DepthMode.NFOV_2x2Binned,
                SynchronizedImagesOnly = true
            });

            var colorWidth = _device.GetCalibration().ColorCameraCalibration.ResolutionWidth;
            var colorHeight = _device.GetCalibration().ColorCameraCalibration.ResolutionHeight;

            _writeableBitmap = new WriteableBitmap(colorWidth, colorHeight, 96.0, 96.0, PixelFormats.Bgra32, null);

            DataContext = this;

            _faceClient = new FaceClient(new ApiKeyServiceClientCredentials(FaceClientKey))
            {
                Endpoint = CognitiveServicesUrl
            };
        }

        private static Stream StreamFromBitmapSource(BitmapSource bitmap)
        {
            Stream memoryStream = new MemoryStream();

            BitmapEncoder bitmapEncoder = new JpegBitmapEncoder();
            bitmapEncoder.Frames.Add(BitmapFrame.Create(bitmap));
            bitmapEncoder.Save(memoryStream);
            memoryStream.Position = 0;

            return memoryStream;
        }

        public static bool IsWindowOpen<T>(string name = "") where T : Window
        {
            return string.IsNullOrEmpty(name)
                ? Application.Current.Windows.OfType<T>().Any()
                : Application.Current.Windows.OfType<T>().Any(w => w.Name.Equals(name));
        }

        public async void Loaded_StartAsync(object sender, RoutedEventArgs e)
        {
            var count = 0;
            DateTime? dateTime = null;

            while (IsWindowOpen<Window>("Main"))
            {
                {
                    using var capture = await Task.Run(() => _device.GetCapture());

                    count++;

                    _writeableBitmap.Lock();

                    var color = capture.Color;
                    var region = new Int32Rect(0, 0, color.WidthPixels, color.HeightPixels);

                    unsafe
                    {
                        using (var pin = color.Memory.Pin())
                            _writeableBitmap.WritePixels(region, (IntPtr)pin.Pointer, (int)color.Size, color.StrideBytes);

                        if (_detectedFace != null)
                        {
                            StatusText = GetEmotion(_detectedFace.FaceAttributes.Emotion).Emotion switch
                            {
                                "Anger" => "Raiva / Anger",
                                "Contempt" => "Desprezo / Contempt",
                                "Disgust" => "Nojo / Disgust",
                                "Fear" => "Medo / Fear",
                                "Happiness" => "Felicidade / Happiness",
                                "Neutral" => "Neutro / Neutral",
                                "Sadness" => "Tristeza / Sadness",
                                "Surprise" => "Surpresa / Surprise",
                                _ => StatusText
                            };
                        }

                        _writeableBitmap.AddDirtyRect(region);
                        _writeableBitmap.Unlock();

                        if (count % 30 != 0) continue;

                        var stream = StreamFromBitmapSource(_writeableBitmap);

                        dateTime ??= DateTime.Now;

                        // Hack to consume an API every 3 seconds, as the
                        // free version only processes 20 requests per minute
                        if (DateTime.Now < dateTime.Value.AddSeconds(3) || !Start) continue;

                        dateTime = DateTime.Now;

                        _ = _faceClient.Face.DetectWithStreamAsync(stream, true, false,
                                MainWindow.AttributesList)
                            .ContinueWith(responseTask =>
                        {
                            try
                            {
                                foreach (var face in responseTask.Result)
                                {
                                    _detectedFace = face;
                                }
                            }
                            catch (Exception ex)
                            {
                                StatusText = ex.ToString();
                            }
                        }, TaskScheduler.FromCurrentSynchronizationContext());
                    }
                }
            }
        }

        public ImageSource ImageSource => _writeableBitmap;

        public string? StatusText
        {
            get => _emotionText;

            set
            {
                if (_emotionText == value) return;

                _emotionText = value;

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("StatusText"));
            }
        }

        private static (string Emotion, double Value) GetEmotion(Emotion emotion)
        {
            var emotionProperties = emotion.GetType().GetProperties();
            (string Emotion, double Value) highestEmotion = ("Anger", emotion.Anger);

            foreach (var e in emotionProperties)
            {
                if (!(((double)e.GetValue(emotion, null)!) > highestEmotion.Value)) continue;
                highestEmotion.Emotion = e.Name;
                highestEmotion.Value = (double)e.GetValue(emotion, null)!;
            }
            return highestEmotion;
        }

        private void BStart_OnClick(object sender, RoutedEventArgs e)
        {
            Start = !Start;
            BStart.Content = Start ? "Stop" : "Start";
            SbEmotion.Visibility = Start ? Visibility.Visible : Visibility.Collapsed;
        }

        private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
        {
            _device.Dispose();
        }
    }
}
