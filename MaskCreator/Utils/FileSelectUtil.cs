using Microsoft.WindowsAPICodePack.Dialogs;

namespace MaskCreator.Utils
{
    public class FileSelectUtil
    {
        public static string PickFolder(string initDir = "")
        {
            var dlg = new CommonOpenFileDialog();
            dlg.Title = "Select an image folder";
            dlg.IsFolderPicker = true;
            dlg.InitialDirectory = initDir;

            dlg.AddToMostRecentlyUsedList = false;
            dlg.AllowNonFileSystemItems = false;
            dlg.DefaultDirectory = initDir;
            dlg.EnsureFileExists = true;
            dlg.EnsurePathExists = true;
            dlg.EnsureReadOnly = false;
            dlg.EnsureValidNames = true;
            dlg.Multiselect = false;
            dlg.ShowPlacesList = true;

            if (dlg.ShowDialog() == CommonFileDialogResult.Ok)
            {
                return dlg.FileName;
            }

            return string.Empty;
        }
    }
}
