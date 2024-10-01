using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace MaskCreator.Masks
{
    public abstract class MaskLayerData(string name, int width, int height) : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged = delegate { };

        // See https://learn.microsoft.com/en-us/windows/apps/develop/data-binding/data-binding-in-depth
        private void NotifyPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public string MaskLayerName { get; set; } = name;
        public int BaseImageWidth { get; } = width;
        public int BaseImageHeight { get; } = height;

        protected byte[][] maskBytesData = []; // MaskCount * MaskSize (MaskSize can be different)
        private int selectedMaskIndex = 0;
        public int SelectedMaskIndex
        {
            get => selectedMaskIndex;
            private set {
                selectedMaskIndex = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof (SelectedMaskText));
            }
        }

        public string SelectedMaskText => $"{selectedMaskIndex + 1} / {maskBytesData.Length}";

        public readonly ObservableCollection<ControlObject> ControlObjects = [];

        private bool active = false;
        public bool Active
        {
            get => active;
            set
            {
                active = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof (ListItemForeground));
                NotifyPropertyChanged(nameof (ListItemMaskDisplayVisibility));
                NotifyPropertyChanged(nameof (ListItemMaskControlsVisibility));
            }
        }

        private bool DisplayMaskSelection => maskBytesData.Length > 1;

        public string ListItemForeground => active ? "Orange" : "Black";
        public string ListItemMaskDisplayVisibility => DisplayMaskSelection ? "Visible" : "Collapsed";
        public string ListItemMaskControlsVisibility => (active && DisplayMaskSelection) ? "Visible" : "Collapsed";

        public virtual void Activate(Action<byte[]?> maskCallback)
        {
            Active = true;

            maskCallback.Invoke(maskBytesData.Length > 0 ? maskBytesData[SelectedMaskIndex] : null);
        }

        public virtual void Deactivate()
        {
            Active = false;

        }

        public void SelectPrevMask(Action<byte[]?> maskCallback)
        {
            if (maskBytesData.Length > 0)
            {
                SelectedMaskIndex = (SelectedMaskIndex + maskBytesData.Length - 1) % maskBytesData.Length;
            }
            else
            {
                SelectedMaskIndex = 0;
            }

            maskCallback.Invoke(maskBytesData.Length > 0 ? maskBytesData[SelectedMaskIndex] : null);
        }

        public void SelectNextMask(Action<byte[]?> maskCallback)
        {
            if (maskBytesData.Length > 0)
            {
                SelectedMaskIndex = (SelectedMaskIndex + 1) % maskBytesData.Length;
            }
            else
            {
                SelectedMaskIndex = 0;
            }

            maskCallback.Invoke(maskBytesData.Length > 0 ? maskBytesData[SelectedMaskIndex] : null);
        }

        public virtual void UpdateMaskData(MaskGenerationResult[] data, Action<byte[]?> maskCallback)
        {
            maskBytesData = data.Select(x => x.bytes).ToArray();

            if (maskBytesData.Length > 0)
            {
                var highestScore = data.Max(x => x.score);
                SelectedMaskIndex = data.Select(x => x.score).ToList().IndexOf(highestScore);
            }
            else
            {
                SelectedMaskIndex = 0;
            }

            NotifyPropertyChanged(nameof (ListItemMaskDisplayVisibility));
            NotifyPropertyChanged(nameof (ListItemMaskControlsVisibility));

            maskCallback.Invoke(maskBytesData.Length > 0 ? maskBytesData[SelectedMaskIndex] : null);
        }

        public virtual void UpdateMaskDataSingle(byte[] singleData, Action<byte[]?> maskCallback)
        {
            maskBytesData = [singleData];
            SelectedMaskIndex = 0;

            NotifyPropertyChanged(nameof(ListItemMaskDisplayVisibility));
            NotifyPropertyChanged(nameof(ListItemMaskControlsVisibility));

            maskCallback.Invoke(maskBytesData[SelectedMaskIndex]);
        }

        public virtual void CreateControlObject_MouseDown(int x, int y, MouseButton mb, Action overlayCallback)
        {

        }

        public virtual void CreateControlObject_MouseMove(int x, int y, Action overlayCallback)
        {

        }

        public virtual void CreateControlObject_MouseUp(int x, int y, MouseButton mb, Action overlayCallback)
        {

        }

        public abstract void RemoveControlObject(ControlObject obj, Action callback);

        public abstract BitmapSource RenderOverlayImage();

        public virtual byte[]? GetSelectedMaskBytes()
        {
            if (maskBytesData.Length > 0)
            {
                return maskBytesData[SelectedMaskIndex];
            }

            return null;
        }

        public abstract ImageMaskLayerData? ConvertToImageLayer();
    }
}
