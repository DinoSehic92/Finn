
using Avalonia.Media.Imaging;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Finn.Model
{
    /// <summary>
    /// Represents other file data, including icon and folder info.
    /// </summary>
    public class OtherData : INotifyPropertyChanged
    {
        private bool isLink;
        /// <summary>
        /// When true this entry is a hyperlink rather than a local file.
        /// <see cref="Filepath"/> holds the URL and no icon bytes are used.
        /// </summary>
        public bool IsLink
        {
            get => isLink;
            set { isLink = value; RaisePropertyChanged(nameof(IsLink)); RaisePropertyChanged(nameof(IsNotLink)); }
        }

        /// <summary>Convenience inverse of <see cref="IsLink"/> for XAML visibility bindings.</summary>
        public bool IsNotLink => !isLink;

        /// <summary>
        /// Configures this entry as a hyperlink.
        /// </summary>
        public void SetLink(string url)
        {
            Filepath = url;
            Name = url;
            Type = "Link";
            IsLink = true;
        }

        private string name = string.Empty;
        /// <summary>
        /// Gets or sets the name of the file.
        /// </summary>
        public string Name
        {
            get => name;
            set { name = value; RaisePropertyChanged(nameof(Name)); }
        }

        private string filepath = string.Empty;
        /// <summary>
        /// Gets or sets the file path.
        /// </summary>
        public string Filepath
        {
            get => filepath;
            set { filepath = value; RaisePropertyChanged(nameof(Filepath)); }
        }

        private string type = string.Empty;
        /// <summary>
        /// Gets or sets the file type.
        /// </summary>
        public string Type
        {
            get => type;
            set { type = value; RaisePropertyChanged(nameof(Type)); }
        }

        private bool isFromFolder;
        /// <summary>
        /// Gets or sets whether the file is from a folder.
        /// </summary>
        public bool IsFromFolder
        {
            get => isFromFolder;
            set { isFromFolder = value; RaisePropertyChanged(nameof(IsFromFolder)); }
        }

        private string fromFolder = string.Empty;
        /// <summary>
        /// Gets or sets the folder the file is from.
        /// </summary>
        public string FromFolder
        {
            get => fromFolder;
            set { fromFolder = value; RaisePropertyChanged(nameof(FromFolder)); }
        }

        private string? syncFolder = string.Empty;
        /// <summary>
        /// Gets or sets the sync folder.
        /// </summary>
        public string? SyncFolder
        {
            get => syncFolder;
            set { syncFolder = value; RaisePropertyChanged(nameof(SyncFolder)); }
        }

        private byte[] iconBytes = Array.Empty<byte>();
        /// <summary>
        /// Gets or sets the icon bytes.
        /// </summary>
        public byte[] IconBytes
        {
            get => iconBytes;
            set { iconBytes = value; RaisePropertyChanged(nameof(IconBytes)); }
        }

        /// <summary>
        /// Loads the icon from the file system if not already cached.
        /// </summary>
        public void LoadIconIfEmpty()
        {
            if ((iconBytes == null || iconBytes.Length == 0) && !string.IsNullOrEmpty(filepath) && File.Exists(filepath))
            {
                try
                {
                    System.Drawing.Bitmap bitmap = System.Drawing.Icon.ExtractAssociatedIcon(filepath).ToBitmap();
                    IconBytes = BitmapToByteArray(bitmap);
                }
                catch
                {
                    iconBytes = Array.Empty<byte>();
                }
            }
        }

        /// <summary>
        /// Gets the Avalonia bitmap for the icon.
        /// </summary>
        public Avalonia.Media.Imaging.Bitmap? Icon => GetAvaloniaBitmap();


        public void SetFile()
        {
            Type = Path.GetExtension(filepath);
            Name = Path.GetFileName(filepath).Replace(Type, "");
            System.Drawing.Bitmap bitmap = System.Drawing.Icon.ExtractAssociatedIcon(filepath).ToBitmap();
            IconBytes = BitmapToByteArray(bitmap);
        }


        public byte[] BitmapToByteArray(System.Drawing.Bitmap bitmap)
        {
            using (var memoryStream = new MemoryStream())
            {
                bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                return memoryStream.ToArray();
            }
        }


        private Avalonia.Media.Imaging.Bitmap GetAvaloniaBitmap()
        {
            if (IconBytes != null)
            {
                using (MemoryStream memory = new MemoryStream(IconBytes))
                {
                    memory.Position = 0;
                    return new Avalonia.Media.Imaging.Bitmap(memory);

                }
            }
            else
            {
                return null;
            }
        }



        private void RaisePropertyChanged(string propName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
