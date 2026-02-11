
using Avalonia.Media.Imaging;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Finn.Model
{
    public class OtherData : INotifyPropertyChanged
    {

        private string name = string.Empty;
        public string Name
        {
            get { return name; }
            set { name = value; RaisePropertyChanged("Name"); }
        }

        private string filepath = string.Empty;
        public string Filepath
        {
            get { return filepath; }
            set { filepath = value; RaisePropertyChanged("Filepath"); }
        }

        private string type;
        public string Type
        {
            get { return type; }
            set { type = value; RaisePropertyChanged("Type"); }
        }


        private bool isFromFolder = false;
        public bool IsFromFolder
        {
            get { return isFromFolder; }
            set { isFromFolder = value; RaisePropertyChanged("IsFromFolder"); RaisePropertyChanged("NameWithAttributes"); }
        }

        private string fromFolder = string.Empty;
        public string FromFolder
        {
            get { return fromFolder; }
            set { fromFolder = value; RaisePropertyChanged("FromFolder"); }
        }

        private string? syncFolder = string.Empty;
        public string? SyncFolder
        {
            get { return syncFolder; }
            set { syncFolder = value; RaisePropertyChanged("SyncFolder"); }
        }

        private byte[] iconBytes;
        public byte[] IconBytes
        {
            get { return iconBytes; }
            set { iconBytes = value; RaisePropertyChanged("IconBytes"); }
        }

        public Avalonia.Media.Imaging.Bitmap? Icon
        {
            get { return GetAvaloniaBitmap(); }
        }


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
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propName));
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
