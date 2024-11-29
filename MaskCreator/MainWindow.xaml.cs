using MaskCreator.Masks;
using MaskCreator.Network;
using MaskCreator.Utils;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MaskCreator
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        /// <summary>
        /// Accepted image extension names in lowercase.
        /// </summary>
        private static readonly HashSet<string> BASE_IMAGE_EXTENSIONS = [ ".png", ".jpg", ".jpeg", ".webp" ];

        public event PropertyChangedEventHandler? PropertyChanged = delegate { };

        // See https://learn.microsoft.com/en-us/windows/apps/develop/data-binding/data-binding-in-depth
        private void NotifyPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string processDirectory = string.Empty;
        private string[] baseImageArray = [];
        private int baseImageIndex = 0;

        private byte[]? baseImageBytes = null;
        private int baseImageWidth = 0;
        private int baseImageHeight = 0;

        public readonly ObservableCollection<MaskLayerData> MaskLayerData = [];
        private MaskLayerData? focusedMaskLayer = null;
        private Color maskColor = Color.FromRgb(0, 0, 255);

        private int cursorPixelX = -1;
        private int cursorPixelY = -1;
        private readonly LineGeometry guidelineVer = new();
        private readonly LineGeometry guidelineHor = new();

        public MainWindow()
        {
            InitializeComponent();

            // Create guideline objects
            var PathA = new Path { Stroke = Brushes.White, StrokeThickness = 3, Data = guidelineVer };
            var PathB = new Path { Stroke = Brushes.White, StrokeThickness = 3, Data = guidelineHor };
            GuidelineCanvas.Children.Add(PathA);
            GuidelineCanvas.Children.Add(PathB);

            // Initialize control status
            MaskLayerListView.ItemsSource = MaskLayerData;
            EditNone();
            MaskImage.Opacity = MaskOpacitySlider.Value / 100;

            var initTask = ClientSAM.GetStartupArgs(msg => MessageLabel.Text = msg);

            var windowTask = initTask.ContinueWith((prevTask) =>
            {
                // Load received directory
                LoadDirectory(prevTask.Result.procDir);

                // Assign received prompt
                DinoPromptTextBox.Text = prevTask.Result.dinoPrompt;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void LoadDirectory(string directory)
        {
            if (System.IO.Directory.Exists(directory))
            {
                processDirectory = directory.Replace('/', System.IO.Path.DirectorySeparatorChar);

                // Load base image list
                baseImageArray = [.. System.IO.Directory.GetFiles(directory)
                    .Select(x => x[(x.LastIndexOf(System.IO.Path.DirectorySeparatorChar) + 1)..])
                    .Where(x =>
                    {
                        var extPos = x.LastIndexOf('.');
                        return BASE_IMAGE_EXTENSIONS.Contains(x[extPos..].ToLower()) && !x[..extPos].EndsWith("_mask");
                    })
                    .Order(new NaturalStringComparer())];

                if (baseImageArray.Length > 0)
                {
                    // Load first in directory as base image
                    LoadBaseImageInProcessingDirectory(0);

                    // Update panel visibility
                    OpenFolderPanel.Visibility = Visibility.Collapsed;
                    ControlPanel.Visibility = Visibility.Visible;
                    return;
                }
            }

            Title = $"Mask Creator - No image loaded";
            MessageLabel.Text = "No base image found in directory";

            // Update panel visibility
            OpenFolderPanel.Visibility = Visibility.Visible;
            ControlPanel.Visibility = Visibility.Collapsed;
        }

        private void LoadBaseImageInProcessingDirectory(int index)
        {
            if (focusedMaskLayer is not null)
            {
                MessageLabel.Text = $"Cannot load base image in edit mode!";
                return;
            }

            if (index < 0 || index >= baseImageArray.Length)
            {
                MessageLabel.Text = $"Cannot load base image #{index + 1}";
                return;
            }

            baseImageIndex = index;
            MaskLayerData.Clear();

            var baseImageName = baseImageArray[baseImageIndex];
            var baseImagePath = System.IO.Path.Combine(processDirectory, baseImageName);
            baseImageBytes = System.IO.File.ReadAllBytes(baseImagePath);
            UpdateBaseImageFromBytes(baseImageBytes);

            // Update window title
            Title = $"Mask Creator - {baseImagePath} ({index + 1} / {baseImageArray.Length})";

            // Try load previously saved mask for base image
            var savedMaskImageName = baseImageName[..baseImageName.LastIndexOf('.')] + "_mask.png";
            var savedMaskImagePath = System.IO.Path.Combine(processDirectory, savedMaskImageName);

            if (System.IO.File.Exists(savedMaskImagePath))
            {
                var savedMaskImageBytes = System.IO.File.ReadAllBytes(savedMaskImagePath);
                UpdateSavedMaskImageFromBytes(savedMaskImageBytes);
            }
            else
            {
                UpdateMaskImage(null);
            }
        }

        private void UpdateBaseImageFromBytes(byte[] baseImageBytes)
        {
            var src = ImageProcessUtil.ByteArrayToBitmapSource(baseImageBytes);
            UpdateBaseImage(src);
        }

        private void UpdateSavedMaskImageFromBytes(byte[] savedMaskImageBytes)
        {
            var existingMaskAsImageLayer = new ImageMaskLayerData("Saved Mask", baseImageWidth, baseImageHeight);
            MaskLayerData.Add(existingMaskAsImageLayer);

            // Assign saved mask data
            existingMaskAsImageLayer.UpdateMaskDataSingle(savedMaskImageBytes, x => { });

            CompositeMaskImage();
        }

        private void UpdateBaseImage(BitmapSource baseImageSource)
        {
            BaseImage.Source = baseImageSource;

            baseImageWidth = baseImageSource.PixelWidth;
            baseImageHeight = baseImageSource.PixelHeight;

            cursorPixelX = cursorPixelY = -1;
            MessageLabel.Text = string.Empty;

            // Also update overlay image
            OverlayImage.Source = null;
        }

        private void UpdateMaskImageFromBytes(byte[]? maskImageBytes)
        {
            if (maskImageBytes is not null)
            {
                var src = ImageProcessUtil.LoadMaskAsBitmapSource(
                        maskImageBytes, maskColor.R, maskColor.G, maskColor.B);
                UpdateMaskImage(src);
            }
            else
            {
                UpdateMaskImage(null);
            }
        }

        private void UpdateMaskImage(BitmapSource? maskImageSource)
        {
            MaskImage.Source = maskImageSource;
        }

        private void UpdateOverlayImage()
        {
            if (focusedMaskLayer != null)
            {
                var maskImageSource = focusedMaskLayer.RenderOverlayImage();
                OverlayImage.Source = maskImageSource;
            }
            else
            {
                OverlayImage.Source = null;
            }
        }

        private void EditLayer(MaskLayerData selectedMaskLayer)
        {
            focusedMaskLayer?.Deactivate();

            selectedMaskLayer.Activate(UpdateMaskImageFromBytes);
            focusedMaskLayer = selectedMaskLayer;

            UpdateOverlayImage();

            // Update panel status
            MaskLayerControlPanel.Visibility = Visibility.Visible;
            MaskCompositionPanel.Visibility = Visibility.Collapsed;
            SegmentButton.IsEnabled = selectedMaskLayer is not ImageMaskLayerData;
            ConvertButton.IsEnabled = selectedMaskLayer is not ImageMaskLayerData;

            // Set control object list view
            ControlObjectListView.ItemsSource = focusedMaskLayer.ControlObjects;
        }

        private void EditNone()
        {
            if (focusedMaskLayer is not null)
            {
                focusedMaskLayer.Deactivate();
                focusedMaskLayer = null;
            }

            // Clear overlay
            UpdateOverlayImage();

            // Reset guideline coordinates
            UpdateGuideline(new(-100, -100), 0, 0, new(0, 0));

            // Clear message label
            MessageLabel.Text = string.Empty;

            // Update panel status
            MaskLayerControlPanel.Visibility = Visibility.Collapsed;
            MaskCompositionPanel.Visibility = Visibility.Visible;

            // Reset list views
            MaskLayerListView.UnselectAll();
            ControlObjectListView.ItemsSource = null;
        }

        private void CompositeMaskImage()
        {
            // CompositeMaskImage masks from all layers
            var composition = ImageProcessUtil.CompositeMaskLayersAsBitmapSource(
                    baseImageWidth, baseImageHeight, [.. MaskLayerData], maskColor.R, maskColor.G, maskColor.B);
            UpdateMaskImage(composition);
        }

        private void UpdateGuideline(Point relativePos, double width, double height, Point mousePos)
        {
            guidelineHor.StartPoint = new Point(relativePos.X,          relativePos.Y + mousePos.Y);
            guidelineHor.EndPoint   = new Point(relativePos.X + width,  relativePos.Y + mousePos.Y);
            guidelineVer.StartPoint = new Point(relativePos.X + mousePos.X, relativePos.Y         );
            guidelineVer.EndPoint   = new Point(relativePos.X + mousePos.X, relativePos.Y + height);
        }

        private void OverlayImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.OriginalSource is Image image)
            {
                Point mousePosition = e.GetPosition(image);

                var relativePos = image.TranslatePoint(new Point(0, 0), MainView);
                UpdateGuideline(relativePos, image.ActualWidth, image.ActualHeight, mousePosition);

                double relativeWidth = mousePosition.X / image.ActualWidth;
                double relativeHeight = mousePosition.Y / image.ActualHeight;

                if (relativeWidth < 0 || relativeHeight < 0 || relativeWidth > 1 || relativeHeight > 1)
                {
                    cursorPixelX = cursorPixelY = -1;
                    MessageLabel.Text = string.Empty;
                    return;
                }

                cursorPixelX = (int)(baseImageWidth * relativeWidth);
                cursorPixelY = (int)(baseImageHeight * relativeHeight);

                MessageLabel.Text = $"Cursor Pixel: {cursorPixelX}, {cursorPixelY}";
            }

            if (focusedMaskLayer is not null && cursorPixelX != -1 && cursorPixelY != -1)
            {
                focusedMaskLayer.CreateControlObject_MouseMove(cursorPixelX, cursorPixelY, UpdateOverlayImage);
            }
        }

        private void OverlayImage_MouseLeave(object sender, MouseEventArgs e)
        {
            cursorPixelX = cursorPixelY = -1;
            MessageLabel.Text = string.Empty;

            // Reset guideline coordinates
            UpdateGuideline(new(-100, -100), 0, 0, new(0, 0));
        }

        private void OverlayImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (focusedMaskLayer is not null && cursorPixelX != -1 && cursorPixelY != -1)
            {
                focusedMaskLayer.CreateControlObject_MouseDown(cursorPixelX, cursorPixelY, e.ChangedButton, UpdateOverlayImage);
            }
        }

        private void OverlayImage_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (focusedMaskLayer is not null && cursorPixelX != -1 && cursorPixelY != -1)
            {
                focusedMaskLayer.CreateControlObject_MouseUp(cursorPixelX, cursorPixelY, e.ChangedButton, UpdateOverlayImage);
            }
        }

        private void AddPointLayerButton_Click(object sender, RoutedEventArgs e)
        {
            var newLayer = new PointMaskLayerData("Point MaskLayer", baseImageWidth, baseImageHeight);

            MaskLayerData.Add(newLayer);
            //EditLayer(newLayer); This will be triggered by the following line
            MaskLayerListView.SelectedItem = newLayer;
        }

        private void AddBoxLayerButton_Click(object sender, RoutedEventArgs e)
        {
            var newLayer = new BoxMaskLayerData("Box MaskLayer", baseImageWidth, baseImageHeight);

            MaskLayerData.Add(newLayer);
            //EditLayer(newLayer); This will be triggered by the following line
            MaskLayerListView.SelectedItem = newLayer;
        }

        private void SelectPrevLayerMaskButton_Click(object sender, RoutedEventArgs e)
        {
            if (((Button) sender).DataContext is MaskLayerData maskLayer && maskLayer == focusedMaskLayer)
            {
                maskLayer.SelectPrevMask(UpdateMaskImageFromBytes);
            }
        }

        private void SelectNextLayerMaskButton_Click(object sender, RoutedEventArgs e)
        {
            if (((Button) sender).DataContext is MaskLayerData maskLayer && maskLayer == focusedMaskLayer)
            {
                maskLayer.SelectNextMask(UpdateMaskImageFromBytes);
            }
        }

        private void RemoveLayerButton_Click(object sender, RoutedEventArgs e)
        {
            if (((Button) sender).DataContext is MaskLayerData maskLayer)
            {
                if (maskLayer == focusedMaskLayer)
                    EditNone();

                MaskLayerData.Remove(maskLayer);

                CompositeMaskImage();
            }
        }

        private void MaskLayerListView_SelectionChanged(object sender, RoutedEventArgs e)
        {
            var selectedMaskLayer = MaskLayerListView.SelectedItem as MaskLayerData;

            if (selectedMaskLayer is not null)
            {
                EditLayer(selectedMaskLayer);
            }
        }

        private void MaskOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var newOpacity = e.NewValue / 100;
            MaskImage.Opacity = newOpacity;
        }

        private void ControlObjectListItem_MouseEnter(object sender, MouseEventArgs e)
        {
            var controlObj = ((FrameworkElement)sender).DataContext as ControlObject;

            if (controlObj is not null)
            {
                var relativePos = OverlayImage.TranslatePoint(new Point(0, 0), MainView);

                if (controlObj is ControlPoint point)
                {
                    var uiX = (point.X / (double) baseImageWidth) * OverlayImage.ActualWidth;
                    var uiY = (point.Y / (double) baseImageHeight) * OverlayImage.ActualHeight;
                    UpdateGuideline(relativePos, OverlayImage.ActualWidth, OverlayImage.ActualHeight, new(uiX, uiY));
                }
                else if (controlObj is ControlBox box)
                {
                    var uiX = (box.X1 / (double) baseImageWidth) * OverlayImage.ActualWidth;
                    var uiY = (box.Y1 / (double) baseImageHeight) * OverlayImage.ActualHeight;
                    UpdateGuideline(relativePos, OverlayImage.ActualWidth, OverlayImage.ActualHeight, new(uiX, uiY));
                }
            }
        }

        private void ControlObjectListItem_MouseLeave(object sender, MouseEventArgs e)
        {
            // Reset guideline coordinates
            UpdateGuideline(new(-100, -100), 0, 0, new(0, 0));
        }

        private void RemoveControlObjectButton_Click(object sender, RoutedEventArgs e)
        {
            var controlObj = ((Button)sender).DataContext as ControlObject;

            if (controlObj is not null && focusedMaskLayer is not null)
            {
                focusedMaskLayer.RemoveControlObject(controlObj, UpdateOverlayImage);
            }
        }

        private void ConvertButton_Click(object sender, RoutedEventArgs e)
        {
            if (focusedMaskLayer is not null)
            {
                var converted = focusedMaskLayer.ConvertToImageLayer();

                if (converted is not null)
                {
                    // Insert this converted layer at the source position
                    var sourcePos = MaskLayerData.IndexOf(focusedMaskLayer);
                    MaskLayerData.Insert(sourcePos, converted);

                    // Remove the source mask layer
                    MaskLayerData.Remove(focusedMaskLayer);

                    //EditLayer(converted); This will be triggered by the following line
                    MaskLayerListView.SelectedItem = converted;
                }
                else
                {
                    MessageLabel.Text = "Cannot convert empty Layer!";
                }
            }
        }

        private async void SegmentButton_Click(object sender, RoutedEventArgs e)
        {
            if (focusedMaskLayer is not null && baseImageBytes is not null)
            {
                if (focusedMaskLayer is PointMaskLayerData pointLayer)
                {
                    var masks = await ClientSAM.GenerateMasks(baseImageBytes, pointLayer.GetControlPoints(), null, msg => MessageLabel.Text = msg);

                    focusedMaskLayer.UpdateMaskData(masks, UpdateMaskImageFromBytes);
                }
                else if (focusedMaskLayer is BoxMaskLayerData boxLayer)
                {
                    var masks = await ClientSAM.GenerateMasks(baseImageBytes, boxLayer.GetControlPoints(), boxLayer.GetControlBox(), msg => MessageLabel.Text = msg);

                    focusedMaskLayer.UpdateMaskData(masks, UpdateMaskImageFromBytes);
                }
            }
        }

        private void EditNoneButton_Click(object sender, RoutedEventArgs e)
        {
            EditNone();
            CompositeMaskImage();
        }

        private async void GenerateBoxLayersButton_Click(object sender, RoutedEventArgs e)
        {
            if (baseImageBytes is null) return;

            var boxLayers = await ClientSAM.GenerateBoxLayers(baseImageBytes, DinoPromptTextBox.Text, baseImageWidth, baseImageHeight, msg => MessageLabel.Text = msg);

            if (boxLayers.Length > 0)
            {
                foreach (var boxLayer in boxLayers)
                {
                    MaskLayerData.Add(boxLayer);
                }

                CompositeMaskImage();
            }
        }

        private void LoadPrevImageButton_Click(object sender, RoutedEventArgs e)
        {
            var select = (baseImageIndex + baseImageArray.Length - 1) % baseImageArray.Length;

            LoadBaseImageInProcessingDirectory(select);
        }

        private void LoadNextImageButton_Click(object sender, RoutedEventArgs e)
        {
            var select = (baseImageIndex + 1) % baseImageArray.Length;

            LoadBaseImageInProcessingDirectory(select);
        }

        public async Task SaveMaskImage(byte[]? maskPngBytes, string savedMaskImagePath)
        {
            if (maskPngBytes is null)
            {
                MessageLabel.Text = "Cannot save empty mask";
                return;
            }

            try
            {
                await System.IO.File.WriteAllBytesAsync(savedMaskImagePath, maskPngBytes);

                // Update message label
                MessageLabel.Text = $"Mask saved to {savedMaskImagePath}";
            }
            catch (System.IO.IOException ex)
            {
                MessageLabel.Text = $"Error: {ex.Message}";
            }
        }

        private async void SaveMaskButton_Click(object sender, RoutedEventArgs e)
        {
            var maskPngBytes = await ImageProcessUtil.CompositeMaskLayersAsPngBytes(baseImageWidth, baseImageHeight, [.. MaskLayerData]);

            var baseImageName = baseImageArray[baseImageIndex];

            var savedMaskImageName = baseImageName[..baseImageName.LastIndexOf('.')] + "_mask.png";
            var savedMaskImagePath = System.IO.Path.Combine(processDirectory, savedMaskImageName);

            await SaveMaskImage(maskPngBytes, savedMaskImagePath);
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedDir = FileSelectUtil.PickFolder(processDirectory);

            if (!string.IsNullOrEmpty(selectedDir))
            {
                LoadDirectory(selectedDir);
            }
        }
    }
}