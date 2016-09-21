using System;
using System.Threading.Tasks;
using ObjectTrackingDemo.Common;
using Windows.ApplicationModel.Core;
using Windows.Media.Capture;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Input;
using System.Drawing;
using AForge.Imaging.Filters;
using AForge.Imaging;
using AForge.Math.Geometry;
using AForge;
using System.Collections.Generic;
using RealtimeFramework.Messaging;

namespace ObjectTrackingDemo
{
    public sealed partial class CameraPage : Page
    {



        int mouta = 0;
        OrtcExample ortcExample;
        private OrtcClient ortcClient;
        MediaCapture mediaCapture;
        int NbrOfBullets = 10;
        Boolean canShoot = true;
        int score = 0;
        String triangle;
        int health = 100; double hurtOpa = 0;
        int min = 0;
        int sec = 0;
        private const int ColorPickFrameRequestId = 42;

        bool bAfterLoaded = false;

        private void BasicPage_LayoutUpdated(object sender, object e)
        {
            if (bAfterLoaded)
            {
                buttonShoot.Focus(FocusState.Programmatic);
                bAfterLoaded = !bAfterLoaded;
            }
        }

        private void BasicPage_Loaded(object sender, RoutedEventArgs e)
        {
            bAfterLoaded = !bAfterLoaded;
        }


        public Visibility ControlsVisibility
        {
            get
            {
                return (Visibility)GetValue(ControlsVisibilityProperty);
            }
            private set
            {
                SetValue(ControlsVisibilityProperty, value);
            }
        }
        public static readonly DependencyProperty ControlsVisibilityProperty =
            DependencyProperty.Register("ControlsVisibility", typeof(Visibility), typeof(CameraPage),
                new PropertyMetadata(Visibility.Visible));

        private VideoEngine _videoEngine;
        private ActionQueue _actionQueue;
        private Settings _settings;


        private Windows.Foundation.Point _viewFinderCanvasTappedPoint;
        private NavigationHelper _navigationHelper;

        /// <summary>
        /// Gets the <see cref="NavigationHelper"/> associated with this <see cref="Page"/>.
        /// </summary>
        public NavigationHelper NavigationHelper
        {
            get { return this._navigationHelper; }
        }

        public CameraPage()
        {
            InitializeComponent();

            gameUpdate.Text= SharedInformation.myNumber;


            timing();
            message.Focus(FocusState.Keyboard);
            _navigationHelper = new NavigationHelper(this);
            controlBar.HideButtonClicked += OnHideButtonClicked;
            controlBar.ToggleFlashButtonClicked += OnToggleFlashButtonClicked;
            controlBar.SettingsButtonClicked += OnSettingsButtonClicked;
            this.Loaded += BasicPage_Loaded;
            this.LayoutUpdated += BasicPage_LayoutUpdated;

            _videoEngine = VideoEngine.Instance;
            
            _settings = App.Settings;

            NavigationCacheMode = NavigationCacheMode.Required;

            ortcClient = new RealtimeFramework.Messaging.OrtcClient();
            ortcExample = new OrtcExample();
          
            ortcExample.Channel = SharedInformation.savedRoom.name;
            ortcExample.AuthenticationToken = SharedInformation.savedRoom.password;

            if (ortcExample.Channel.Equals(SharedInformation.savedRoom.name))
            {

                message.DataContext = ortcExample;
                ortcExample.DoConnectDisconnect();

            }

        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            message.Focus(FocusState.Keyboard);
            //await Dispatcher.RunAsync(CoreDispatcherPriority.Normal,() => buttonShoot.Focus(FocusState.Pointer));
            buttonShoot.Focus(FocusState.Pointer);
            viseur.Visibility = Visibility.Visible;
            gameOver_StackPanel.Visibility = Visibility.Collapsed;
            //buttonHp.Content = health + "";
            dead.DataContext = 0;
            imghurt.DataContext = hurtOpa;
            await Windows.UI.ViewManagement.StatusBar.GetForCurrentView().HideAsync();
            _actionQueue = new ActionQueue();
            _actionQueue.ExecuteIntervalInMilliseconds = 500;

            await InitializeAndStartVideoEngineAsync();
            SetFlash(_settings.Flash, false);
            settingsPanelControl.ModeChanged += _videoEngine.OnModeChanged;
            settingsPanelControl.RemoveNoiseChanged += _videoEngine.OnRemoveNoiseChanged;
            settingsPanelControl.ApplyEffectOnlyChanged += _videoEngine.OnApplyEffectOnlyChanged;
            settingsPanelControl.IsoChanged += _videoEngine.OnIsoSettingsChangedAsync;
            settingsPanelControl.ExposureChanged += _videoEngine.OnExposureSettingsChangedAsync;
            _videoEngine.ShowMessageRequest += OnVideoEngineShowMessageRequestAsync;
            _videoEngine.Messenger.FrameCaptured += OnFrameCapturedAsync;
            _videoEngine.Messenger.PostProcessComplete += OnPostProcessCompleteAsync;

            Window.Current.VisibilityChanged += OnVisibilityChangedAsync;

            DataContext = this;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _videoEngine.Torch = false;
            _actionQueue.Dispose();
            _settings.Save();

            settingsPanelControl.ModeChanged -= _videoEngine.OnModeChanged;
            settingsPanelControl.RemoveNoiseChanged -= _videoEngine.OnRemoveNoiseChanged;
            settingsPanelControl.ApplyEffectOnlyChanged -= _videoEngine.OnApplyEffectOnlyChanged;
            settingsPanelControl.IsoChanged -= _videoEngine.OnIsoSettingsChangedAsync;
            settingsPanelControl.ExposureChanged -= _videoEngine.OnExposureSettingsChangedAsync;
            _videoEngine.ShowMessageRequest -= OnVideoEngineShowMessageRequestAsync;
            _videoEngine.Messenger.FrameCaptured -= OnFrameCapturedAsync;
            _videoEngine.Messenger.PostProcessComplete -= OnPostProcessCompleteAsync;

            Window.Current.VisibilityChanged -= OnVisibilityChangedAsync;

            captureElement.Source = null;
            _videoEngine.DisposeAsync();

            base.OnNavigatedFrom(e);
        }

        /// <summary>
        /// Initializes and starts the video engine.
        /// </summary>
        /// <returns>True, if successful.</returns>
        private async Task<bool> InitializeAndStartVideoEngineAsync()
        {
            bool success = await _videoEngine.InitializeAsync();

            if (success)
            {
                captureElement.Source = _videoEngine.MediaCapture;

                _settings.SupportedIsoSpeedPresets = _videoEngine.SupportedIsoSpeedPresets;
                await _videoEngine.SetIsoSpeedAsync(_settings.IsoSpeedPreset);
                await _videoEngine.SetExposureAsync(_settings.Exposure);

                success = await _videoEngine.StartAsync();
            }

            controlBar.FlashButtonVisibility = _videoEngine.IsTorchSupported
                ? Visibility.Visible : Visibility.Collapsed;

            return success;
        }

        /// <summary>
        /// Sets the flash on/off.
        /// </summary>
        /// <param name="enabled">If true, will try to set the flash on.</param>
        /// <param name="saveSettings">If true, will save the color to the local storage.</param>
        private void SetFlash(bool enabled, bool saveSettings = true)
        {
            _videoEngine.Flash = _videoEngine.Torch = enabled;
            _settings.Flash = _settings.Torch = _videoEngine.Torch;

            controlBar.IsFlashOn = _videoEngine.Torch;

            if (saveSettings)
            {
                _settings.Save();
            }
        }

        #region Event handlers

        private async void OnVisibilityChangedAsync(object sender, VisibilityChangedEventArgs e)
        {
            if (e.Visible == true)
            {
                _actionQueue = new ActionQueue();
                _actionQueue.ExecuteIntervalInMilliseconds = 500;

                await InitializeAndStartVideoEngineAsync();
                _videoEngine.Messenger.FrameCaptured += OnFrameCapturedAsync;
                _videoEngine.Messenger.PostProcessComplete += OnPostProcessCompleteAsync;
            }
            else
            {
                _actionQueue.Dispose();
                _actionQueue = null;

                _settings.Save();
                _videoEngine.Messenger.FrameCaptured -= OnFrameCapturedAsync;
                _videoEngine.Messenger.PostProcessComplete -= OnPostProcessCompleteAsync;

                captureElement.Source = null;
                _videoEngine.DisposeAsync();
            }
        }

        private async void OnFrameCapturedAsync(byte[] pixelArray, int frameWidth, int frameHeight, int frameId)
        {
            _videoEngine.Messenger.FrameCaptured -= OnFrameCapturedAsync;
            System.Diagnostics.Debug.WriteLine("OnFrameCapturedAsync");

            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                WriteableBitmap bitmap = await ImageProcessingUtils.PixelArrayToWriteableBitmapAsync(pixelArray, frameWidth, frameHeight);

                if (bitmap != null)
                {
                    CapturePhoto(bitmap);
                    //capturedPhotoImage.Source = bitmap;
                    //bitmap.Invalidate();
                    //capturedPhotoImage.Visibility = Visibility.Visible;
                }

                _videoEngine.Messenger.FrameCaptured += OnFrameCapturedAsync;
            });
        }

        private async void OnPostProcessCompleteAsync(
            byte[] pixelArray, int imageWidth, int imageHeight,
            ObjectDetails fromObjectDetails, ObjectDetails toObjectDetails)
        {
            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                WriteableBitmap bitmap = await ImageProcessingUtils.PixelArrayToWriteableBitmapAsync(pixelArray, imageWidth, imageHeight);

                if (bitmap != null)
                {
                   
                }
            });

        }

        private void OnHideButtonClicked(object sender, RoutedEventArgs e)
        {
            ControlsVisibility = Visibility.Collapsed;
        }

        private void OnToggleFlashButtonClicked(object sender, RoutedEventArgs e)
        {
            SetFlash(!_videoEngine.Torch);
        }

        private void OnSettingsButtonClicked(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            if (settingsPanelControl.Visibility == Visibility.Visible)
            {
                settingsPanelControl.Hide();
            }
            else
            {
                settingsPanelControl.Show();
            }
        }

        private void OnSwitchToPhotosButtonClicked(object sender, RoutedEventArgs e)
        {
            if (Frame.BackStack.Count == 0)
            {
                _settings.AppMode = AppMode.Photo;
                _settings.Save();
            }
            else
            {
                NavigationHelper.GoBack();
            }            
        }

        /// <summary>
        /// Picks and sets the color from the point, which was tapped.
        /// However, if controls were hidden, their visibility is restored but no color is picked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnViewfinderCanvasTapped(object sender, TappedRoutedEventArgs e)
        {
            if (ControlsVisibility == Visibility.Collapsed)
            {
                ControlsVisibility = Visibility.Visible;
                return;
            }

            if (settingsPanelControl.Visibility == Visibility.Visible)
            {
                settingsPanelControl.Hide();
                return;
            }
            _videoEngine.Messenger.FrameRequestId = ColorPickFrameRequestId;
        }

        private async void OnVideoEngineShowMessageRequestAsync(object sender, string message)
        {
            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
            });
        }

        #endregion


        /* public Task CapturePhoto(WriteableBitmap writeableBitmap)
         {
             processingResultImage.Source = writeableBitmap;

             NbrOfBullets--;
             if (NbrOfBullets >= 0)
             {

                 Bitmap bitmap = null;
                 Bitmap bitmapCopie = null;
                 try
                 {
                     //WriteableBitmap writeableBitmap = null;
                     bitmap = (Bitmap)(writeableBitmap);
                     bitmapCopie = (Bitmap)(writeableBitmap);
                 }
                 catch
                 {
                     text_exeption.Text = "Erreur convert to Bitmap1";
                     return null;
                 }
                 ColorFiltering filterWhite = new ColorFiltering();
                 filterWhite.Red = new IntRange(243, 255);
                 filterWhite.Green = new IntRange(243, 255);
                 filterWhite.Blue = new IntRange(223, 255);
                 try
                 {
                     filterWhite.ApplyInPlace(bitmap);
                     capturedPhotoAfterFilter.Source = (WriteableBitmap)bitmap;
                 }
                 catch
                 {
                     text_exeption.Text = "can't apply White filter";
                     return null;
                 }
                 BlobCounter blobCounter = new AForge.Imaging.BlobCounter();

                 blobCounter.FilterBlobs = true;
                 blobCounter.MinHeight = 3;
                 blobCounter.MinWidth = 3;
                 try
                 {
                     blobCounter.ProcessImage(bitmap);

                 }
                 catch
                 {
                     text_exeption.Text = "Can't process image";
                     return null;
                 }
                 Blob[] blobs = blobCounter.GetObjectsInformation();
                 SimpleShapeChecker shapeChecker = new SimpleShapeChecker();
                 AForge.Point center = new AForge.Point();
                 float radius = 0;
                 for (int i = 0, n = blobs.Length; i < n; i++)
                 {
                     List<IntPoint> edgePoints = blobCounter.GetBlobsEdgePoints(blobs[i]);
                     if (shapeChecker.IsCircle(edgePoints, out center, out radius))
                     {
                         if (radius > 3) score++;
                         break;
                     }
                 }

                 text_score.Text = score + "";
                 textBlock.Text = NbrOfBullets + " / 10";

             }

             if (NbrOfBullets == 0)
             {
                 canShoot = false;
                 buttonReload.DataContext = "Red";
             }
             return null;
         }*/


        public Task CapturePhoto(WriteableBitmap writeableBitmap)
        {
            //text_log.Text = "Wait analyse";
            NbrOfBullets--;
            if (NbrOfBullets >= 0)
            {
                textBlock.Text = NbrOfBullets + " / 10";

                Bitmap bitmap = null;
                Bitmap bitmapCopie = null;
                try
                {
                    bitmap = (Bitmap)(writeableBitmap);
                    bitmapCopie = (Bitmap)(writeableBitmap);
                    //processingResultImage.Source = writeableBitmap;
                }
                catch
                {
                    //text_exeption.Text = "Erreur convert to Bitmap1";
                    return null;
                }

                ColorFiltering filterWhite = new ColorFiltering();
                filterWhite.Red = new IntRange(243, 255);
                filterWhite.Green = new IntRange(243, 255);
                filterWhite.Blue = new IntRange(223, 255);
                try
                {
                    filterWhite.ApplyInPlace(bitmap);
                    //capturedPhotoAfterFilter.Source = (WriteableBitmap)bitmap;
                }
                catch
                {
                    //text_exeption.Text = "can't apply White filter";
                    return null;
                }

                BlobCounter blobCounter = new AForge.Imaging.BlobCounter();

                blobCounter.FilterBlobs = true;
                blobCounter.MinHeight = 3;
                blobCounter.MinWidth = 3;
                try
                {
                    blobCounter.ProcessImage(bitmap);

                }
                catch
                {
                    //text_exeption.Text = "Can't process image";
                    return null;
                }
                Blob[] blobs = blobCounter.GetObjectsInformation();
                //text_log.Text = "Blob OK";
                // create Graphics object to draw on the image and a pen
                SimpleShapeChecker shapeChecker = new SimpleShapeChecker();
                //text_exeption.Text = "";

                for (int i = 0, n = blobs.Length; i < n; i++)
                {
                    List<IntPoint> edgePoints = blobCounter.GetBlobsEdgePoints(blobs[i]);
                    List<IntPoint> corners = PointsCloud.FindQuadrilateralCorners(edgePoints);
                    if (corners.Count == 4 && (corners[1].X - corners[0].X > 15) && corners[3].Y - corners[1].Y > 15)
                    {
                        //text_couleur.Text = colorOfPlayer(bitmapCopie, corners);
                        break;
                    }
                }
                //text_log.Text = "End OK";
                // text_score.Text = score + "";

            }

            if (NbrOfBullets == 2)
            {
                buttonReload.DataContext = "Orange";
            }

            if (NbrOfBullets == 0)
            {
                canShoot = false;
                buttonReload.DataContext = "Red";
            }
            return null;
        }

        private String colorOfPlayer(Bitmap bitmap, List<AForge.IntPoint> corners)
        {
            //return corners.Count + "";
            AForge.IntPoint p1 = corners[0];
            AForge.IntPoint p2 = corners[1];
            AForge.IntPoint p3 = corners[2];
            AForge.IntPoint p4 = corners[3];
            if (p1.X > p4.X) p1.X = p4.X; else p4.X = p1.X;
            if (p2.X < p1.X) p2.X = p3.X; else p3.X = p2.X;
            if (p1.Y > p2.Y) p1.Y = p2.Y; else p2.Y = p1.Y;
            if (p3.Y > p4.Y) p4.Y = p3.Y; else p3.Y = p4.Y;
            List<AForge.IntPoint> corners2 = new List<AForge.IntPoint>();
            corners2.Add(p1);
            corners2.Add(p2);
            corners2.Add(p3);
            corners2.Add(p4);
            //text_couleur.Text = "Unrecognized";
            QuadrilateralTransformation quadrilateralTransformation =
                new QuadrilateralTransformation(corners, 100, 100);
            Bitmap workBitmap = null;
            Bitmap transformed = null;
            Bitmap grayBitmap = null;
            Bitmap purpleBitmap = null;
            try
            {
                transformed = quadrilateralTransformation.Apply(bitmap);
                //capturedPhotoAfterFilter1.Source = (WriteableBitmap)transformed;
                workBitmap = quadrilateralTransformation.Apply(bitmap);
                grayBitmap = quadrilateralTransformation.Apply(bitmap);
                purpleBitmap = quadrilateralTransformation.Apply(bitmap);
            }
            catch
            {
                //text_log.Text = "Can't quadrilaterate2";
            }

            //text_log.Text = "Cadrage ok";


            YCbCrFiltering purpleFilter = new YCbCrFiltering();

            purpleFilter.Cb = new Range(0, 0.4f);
            purpleFilter.Cr = new Range(0.1f, 0.3f);
            //purpleFilter.Y = new Range(-0.7f, -0.9f);
            purpleFilter.ApplyInPlace(purpleBitmap);

            AForge.Imaging.BlobCounter blobCounterPurple = new AForge.Imaging.BlobCounter();
            blobCounterPurple.FilterBlobs = true;
            blobCounterPurple.MinHeight = 5;
            blobCounterPurple.MinWidth = 5;
            blobCounterPurple.ProcessImage(purpleBitmap);
            AForge.Imaging.Blob[] blobsPurple = blobCounterPurple.GetObjectsInformation();
            // create Graphics object to draw on the image and a pen
            SimpleShapeChecker ShapeCheckerPurple = new SimpleShapeChecker();
            //text_log.Text = "testPurple";
            if (blobsPurple.Length > 0)
            {
                //capturedPhotoAfterAfterFilter.Source = (WriteableBitmap)purpleBitmap;

                ortcExample.DoSendMessage(SharedInformation.myNumber+ getPlayerNumberByColor("Purple"));
                //ortcExample.DoSendMessage("Purple");
                return "Purple";
            }
            else
            {
                // create filter
                //ColorFiltering greenFilter = new ColorFiltering();
                //greenFilter.Red = new IntRange(0, 100);
                //greenFilter.Green = new IntRange(100, 255);
                //greenFilter.Blue = new IntRange(0, 50);
                //greenFilter.ApplyInPlace(workBitmap);
                YCbCrFiltering greenFilter = new YCbCrFiltering();

                greenFilter.Cb = new Range(-1, 0);
                greenFilter.Cr = new Range(-1, 0);
                //greenFilter.Y = new Range(-0.7f, -0.9f);
                greenFilter.ApplyInPlace(workBitmap);

                WriteableBitmap wrbaf = (WriteableBitmap)workBitmap;
                //capturedPhotoAfterAfterFilter.Source = wrbaf;
                AForge.Imaging.BlobCounter blobCounterGreen = new AForge.Imaging.BlobCounter();

                blobCounterGreen.FilterBlobs = true;
                blobCounterGreen.MinHeight = 5;
                blobCounterGreen.MinWidth = 5;
                blobCounterGreen.ProcessImage(workBitmap);
                AForge.Imaging.Blob[] blobsGreen = blobCounterGreen.GetObjectsInformation();
                // create Graphics object to draw on the image and a pen
                SimpleShapeChecker shapeCheckerGreen = new SimpleShapeChecker();
                //text_log.Text = "test2";
                if (blobsGreen.Length > 0)
                {
                    //capturedPhotoAfterAfterFilter.Source = (WriteableBitmap)workBitmap;

                    ortcExample.DoSendMessage(SharedInformation.myNumber + getPlayerNumberByColor("Green"));
                    //ortcExample.DoSendMessage("Green");
                    return "Green";
                }
                else
                {

                    // create filter
                    //ColorFiltering GrayFilter = new ColorFiltering();
                    //// set color ranges to keep
                    //GrayFilter.Red = new IntRange(100, 150);
                    //GrayFilter.Green = new IntRange(100, 150);
                    //GrayFilter.Blue = new IntRange(100, 150);
                    //// apply the filter
                    //GrayFilter.ApplyInPlace(grayBitmap);
                    YCbCrFiltering GrayFilter = new YCbCrFiltering();

                    GrayFilter.Cb = new Range(-1, 0);
                    GrayFilter.Cr = new Range(0, 1);
                    //GrayFilter.Y = new Range(-0.7f, -0.9f);
                    GrayFilter.ApplyInPlace(grayBitmap);

                    AForge.Imaging.BlobCounter blobCounterGray = new AForge.Imaging.BlobCounter();

                    blobCounterGray.FilterBlobs = true;
                    blobCounterGray.MinHeight = 5;
                    blobCounterGray.MinWidth = 5;
                    try { blobCounterGray.ProcessImage(grayBitmap); } catch {
                        //text_log.Text = "Error while Processing Image";
                    }
                    AForge.Imaging.Blob[] blobsGray = blobCounterGray.GetObjectsInformation();
                    // create Graphics object to draw on the image and a pen
                    SimpleShapeChecker shapeCheckerGrey = new SimpleShapeChecker();
                    //text_log.Text = "test3";
                    if (blobsGray.Length > 0)
                    {
                        //capturedPhotoAfterAfterFilter.Source = (WriteableBitmap)grayBitmap;
                        ortcExample.DoSendMessage(SharedInformation.myNumber + getPlayerNumberByColor("Orange"));
                        //ortcExample.DoSendMessage("Orange");
                        return "Orange";
                    }
                    else
                    {
                        //ColorFiltering blueFilter = new ColorFiltering();
                        //blueFilter.Red = new IntRange(0, 70);
                        //blueFilter.Green = new IntRange(0, 70);
                        //blueFilter.Blue = new IntRange(100, 255);
                        //blueFilter.ApplyInPlace(transformed);
                        YCbCrFiltering blueFilter = new YCbCrFiltering();

                        blueFilter.Cb = new Range(0, 1);
                        blueFilter.Cr = new Range(-1, 0f);
                        blueFilter.Y = new Range(-0.7f, -0.9f);
                        blueFilter.ApplyInPlace(workBitmap);

                        AForge.Imaging.BlobCounter blobCounter2 = new AForge.Imaging.BlobCounter();
                        blobCounter2.FilterBlobs = true;
                        blobCounter2.MinHeight = 5;
                        blobCounter2.MinWidth = 5;
                        try { blobCounter2.ProcessImage(transformed); } catch {
                            //text_log.Text = "transformed not Ok";
                        }
                        //try { blobCounter2.ProcessImage(transformed); } catch { text_log.Text = "transformed not Ok"; }

                        AForge.Imaging.Blob[] blobs = blobCounter2.GetObjectsInformation();
                        // create Graphics object to draw on the image and a pen
                        SimpleShapeChecker shapeChecker = new SimpleShapeChecker();
                        if (blobs.Length > 0)
                        {
                            //capturedPhotoAfterAfterFilter.Source = (WriteableBitmap)transformed;
                            ortcExample.DoSendMessage(SharedInformation.myNumber + getPlayerNumberByColor("Blue"));
                            ///ortcExample.DoSendMessage("Blue");
                            return "blue";
                        }
                        //}
                        return "aa";
                    }
                }

            }
        }


        private async void button_Reload_Click(object sender, RoutedEventArgs e)
        {
            buttonReload.Visibility = Visibility.Collapsed;
            canShoot = false;
            await Task.Delay(TimeSpan.FromSeconds(0.45));
            textBlock.Text = "";
            await Task.Delay(TimeSpan.FromSeconds(0.45));
            textBlock.Text = ".";
            await Task.Delay(TimeSpan.FromSeconds(0.45));
            textBlock.Text = "..";
            await Task.Delay(TimeSpan.FromSeconds(0.45));
            textBlock.Text = "....";
            await Task.Delay(TimeSpan.FromSeconds(0.45));
            textBlock.Text = "......";
            await Task.Delay(TimeSpan.FromSeconds(0.45));
            textBlock.Text = "........";
            await Task.Delay(TimeSpan.FromSeconds(0.45));
            textBlock.Text = "..........";
            NbrOfBullets = 10;
            textBlock.Text = "10 / 10";
            buttonReload.Visibility = Visibility.Visible;
            canShoot = true;
        }

        /*private void buttonHp_Click(object sender, RoutedEventArgs e)
        {
            hurtOpa += 0.4;
            imghurt.DataContext = hurtOpa;

            if (hurtOpa >= 1)
            {
                hurtOpa += 1;
                viseur.Visibility = Visibility.Collapsed;
                nameOfKiller_text.DataContext = "Foulen";
                gameOver_StackPanel.Visibility = Visibility.Visible;
                canShoot = false;
                dead.DataContext = 0.7;
                buttonReload.Visibility = Visibility.Collapsed;
                //textBlock.Visibility = Visibility.Collapsed;
            }
        }*/

        private async void timing()
        {
            do
            {
                String zeroMin;
                String zeroSec;
                await Task.Delay(TimeSpan.FromSeconds(1));
                sec++;
                if (sec == 60) { min++; sec = 0; };
                if (min > 9) zeroMin = ""; else zeroMin = "0";
                if (sec > 9) zeroSec = ""; else zeroSec = "0";
                time_text.DataContext = zeroMin + min + ":" + zeroSec + sec;
            } while (min < 60);
        }

        private void button_Shoot_Click(object sender, RoutedEventArgs e)
        {
            if (canShoot)
            {
                try
                {
                    canShoot = false;
                    _videoEngine.Messenger.FrameRequestId = ColorPickFrameRequestId;                   
                    canShoot = true;
                }
                catch (Exception ex)
                {
                    //text_exeption.Text = "Erreur capture";
                }
            }
        }


        private void textchange(object sender, TextChangedEventArgs e)
        {
            if(message.Text != "") { 
            String test;
            test = message.Text;
            String numm = SharedInformation.myNumber;

            if (test[1].Equals(numm))
            {
                hurtOpa += 0.4;
                imghurt.DataContext = hurtOpa;

                if (hurtOpa >= 1)
                {
                    viseur.Visibility = Visibility.Collapsed;
                    nameOfKiller_text.DataContext = getPlayerNameByNumber(test[0]);
                    gameOver_StackPanel.Visibility = Visibility.Visible;

                    canShoot = false;
                    dead.DataContext = 0.7;
                    text_death.Text = "" + 1;
                 
                    //textBlock.Visibility = Visibility.Collapsed;
                }
                ortcExample.DoClearLog();
            }else if (test.Length == 2)
            {
                gameUpdate.Text = getPlayerNameByNumber(test[0]) + " killed " + getPlayerNameByNumber(test[1]);
                ortcExample.DoClearLog();
            }
            }
        }

        private async void shoot(object sender, KeyRoutedEventArgs e)
        {

            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                if (canShoot)
                {
                    try
                    {
                        canShoot = false;
                        _videoEngine.Messenger.FrameRequestId = ColorPickFrameRequestId;
                        canShoot = true;
                    }
                    catch (Exception ex)
                    {
                        //text_exeption.Text = "Erreur capture";
                    }
                }
            }
        }




        private Char getPlayerNumberByColor(String colorDetected)
        {
            Char cccc = ' ';
            foreach(Player ppp in SharedInformation.ListOfPlayers)
            {
                if (ppp.color.Equals(colorDetected))
                {
                    cccc= ppp.number;
                }
            }
            return cccc;
        }


        private String getPlayerNameByNumber(Char numberDetected)
        {
            foreach (Player ppp in SharedInformation.ListOfPlayers)
            {
                if (ppp.number.Equals(numberDetected))
                {
                    return ppp.name;
                }
            }
            return "";
        }

    }

}
