using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using FanSelector.Core;

namespace FanSelector.UI
{
    internal static class WindowSupport
    {
        /// <summary>
        /// Make Revit the owner of a WPF dialog, so it can never end up behind
        /// the main window.
        /// </summary>
        public static void OwnedByRevit(Window window, IntPtr revitHandle)
        {
            if (window == null || revitHandle == IntPtr.Zero) return;
            try { new WindowInteropHelper(window).Owner = revitHandle; }
            catch { /* an unowned dialog is still usable */ }
        }

        /// <summary>
        /// An image embedded under a brand-neutral resource name. The brand's own
        /// artwork (logo.png, web_icon.png) is contributed at build time by
        /// _branding\Branding.targets, so nothing here knows which brand it is.
        /// </summary>
        public static BitmapImage Image(string fileName)
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                Stream stream = assembly.GetManifestResourceStream("FanSelector.Resources." + fileName);
                if (stream == null)
                {
                    foreach (string name in assembly.GetManifestResourceNames())
                        if (name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                        {
                            stream = assembly.GetManifestResourceStream(name);
                            break;
                        }
                }
                if (stream == null) return null;

                using (stream)
                {
                    BitmapImage image = new BitmapImage();
                    image.BeginInit();
                    image.StreamSource = stream;
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }
            catch { return null; }
        }

        /// <summary>
        /// A picture from disk, loaded so the file is NOT left locked — a fan photo
        /// sitting in the install folder must stay replaceable while Revit runs.
        /// Null when there is no such file or it is not an image.
        /// </summary>
        public static BitmapImage ImageFromFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                if (!File.Exists(path)) return null;
                using (FileStream stream = File.OpenRead(path))
                {
                    BitmapImage image = new BitmapImage();
                    image.BeginInit();
                    image.StreamSource = stream;
                    image.CacheOption = BitmapCacheOption.OnLoad;   // read now, release the file
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }
            catch { return null; }
        }

        /// <summary>
        /// Revit hands out type previews as GDI+ bitmaps. Converting one to
        /// something WPF can show means an HBITMAP, which has to be released by
        /// hand or the add-in leaks a GDI object per preview.
        /// </summary>
        public static BitmapSource FromBitmap(System.Drawing.Bitmap bitmap)
        {
            if (bitmap == null) return null;
            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = bitmap.GetHbitmap();
                BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                    handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            catch { return null; }
            finally
            {
                if (handle != IntPtr.Zero) DeleteObject(handle);
                bitmap.Dispose();
            }
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        /// <summary>
        /// Banner text for a newer published version, or null when there is none
        /// (which is always the case for brands with update checking switched off).
        /// </summary>
        public static string UpdateBannerText()
        {
            UpdateInfo update = UpdateChecker.AvailableUpdate;
            if (update == null) return null;
            string text = "Version " + update.Version + " is available — click to download.";
            if (!string.IsNullOrEmpty(update.ReleaseNotes)) text += "  (" + update.ReleaseNotes + ")";
            return text;
        }

        public static void OpenUpdatePage()
        {
            UpdateInfo update = UpdateChecker.AvailableUpdate;
            if (update == null || string.IsNullOrEmpty(update.DownloadUrl)) return;
            try { Process.Start(new ProcessStartInfo(update.DownloadUrl) { UseShellExecute = true }); }
            catch { /* no browser / bad URL - nothing useful to say */ }
        }
    }
}
