﻿//-----------------------------------------------------------------------
// <copyright file="ScreenCapture.cs" company="Gavin Kendall">
//     Copyright (c) 2020 Gavin Kendall
// </copyright>
// <author>Gavin Kendall</author>
// <summary></summary>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//-----------------------------------------------------------------------
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace AutoScreenCapture
{
    /// <summary>
    /// This class is responsible for getting bitmap images of the screen area. It also saves screenshots to the file system.
    /// </summary>
    public class ScreenCapture
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct CURSORINFO
        {
            public Int32 cbSize;
            public Int32 flags;
            public IntPtr hCursor;
            public POINTAPI ptScreenPos;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINTAPI
        {
            public int x;
            public int y;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern void GetWindowRect(IntPtr hWnd, out Rectangle rect);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowThreadProcessId(IntPtr hWnd, out uint ProcessId);

        [DllImport("user32.dll")]
        private static extern bool GetCursorInfo(out CURSORINFO pci);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyHeight, int istepIfAniCur, IntPtr hbrFlickerFreeDraw, int diFlags);

        private const Int32 CURSOR_SHOWING = 0x0001;
        private const Int32 DI_NORMAL = 0x0003;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private static WINDOWPLACEMENT GetPlacement(IntPtr hwnd)
        {
            WINDOWPLACEMENT placement = new WINDOWPLACEMENT();
            placement.length = Marshal.SizeOf(placement);
            GetWindowPlacement(hwnd, ref placement);
            return placement;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        internal enum ShowWindowCommands : int
        {
            Hide = 0,
            Normal = 1,
            Minimized = 2,
            Maximized = 3,
        }

        [Serializable]
        [StructLayout(LayoutKind.Sequential)]
        internal struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public ShowWindowCommands showCmd;
            public System.Drawing.Point ptMinPosition;
            public System.Drawing.Point ptMaxPosition;
            public System.Drawing.Rectangle rcNormalPosition;
        }

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        public const int ENUM_CURRENT_SETTINGS = -1;

        [DllImport("user32.dll")]
        public static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        [StructLayout(LayoutKind.Sequential)]
        public struct DEVMODE
        {
            private const int CCHDEVICENAME = 0x20;
            private const int CCHFORMNAME = 0x20;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 0x20)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public ScreenOrientation dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 0x20)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        /// <summary>
        /// A struct containing a Windows Forms screen object, display device width, and display device height.
        /// </summary>
        public struct DeviceResolution
        {
            /// <summary>
            /// A Windows Forms screen object.
            /// </summary>
            public System.Windows.Forms.Screen screen;

            /// <summary>
            /// The width of the display device.
            /// </summary>
            public int width;

            /// <summary>
            /// The height of the display device.
            /// </summary>
            public int height;
        }

        /// <summary>
        /// The minimum capture limit.
        /// </summary>
        public const int CAPTURE_LIMIT_MIN = 1;

        /// <summary>
        /// The maximum capture limit.
        /// </summary>
        public const int CAPTURE_LIMIT_MAX = 9999;

        /// <summary>
        /// The default image format.
        /// </summary>
        public const string DefaultImageFormat = ImageFormatSpec.NAME_JPEG;

        private const int MAX_CHARS = 48000;
        private const int IMAGE_RESOLUTION_RATIO_MIN = 1;

        /// <summary>
        /// The maximum image resolution ratio.
        /// </summary>
        public const int IMAGE_RESOLUTION_RATIO_MAX = 100;

        /// <summary>
        /// The interval delay for the timer when a screen capture session is running.
        /// </summary>
        public int Interval { get; set; }

        /// <summary>
        /// The limit on how many screen capture cycles we go through during a screen capture session.
        /// </summary>
        public int Limit { get; set; }

        /// <summary>
        /// The number of screen capture cycles we've gone through during a screen capture session.
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        /// Determines if the screen capture session is locked.
        /// </summary>
        public static bool LockScreenCaptureSession { get; set; }

        /// <summary>
        /// Determines if we automatically start a screen capture session immediately when command line arguments are used.
        /// </summary>
        public static bool AutoStartFromCommandLine { get; set; }

        /// <summary>
        /// Determines if a screen capture session is currently running.
        /// </summary>
        public bool Running { get; set; }

        /// <summary>
        ///  Determines if the application encountered an error.
        /// </summary>
        public bool ApplicationError { get; set; }

        /// <summary>
        /// Determines if the application encountered an event that isn't an error but we still need to inform the user about.
        /// </summary>
        public bool ApplicationWarning { get; set; }

        /// <summary>
        /// Determines if we had an error during screen capture.
        /// </summary>
        public bool CaptureError { get; set; }

        /// <summary>
        /// The date/time when the user started a screen capture session.
        /// </summary>
        public DateTime DateTimeStartCapture { get; set; }

        /// <summary>
        /// The date/time when screenshots are taken.
        /// </summary>
        public DateTime DateTimeScreenshotsTaken { get; set; }

        /// <summary>
        /// The date/time when the previous cycle of screenshots were taken.
        /// </summary>
        public DateTime DateTimePreviousCycle { get; set; }

        /// <summary>
        /// The date/time of the next screen capture cycle.
        /// If we're still waiting for the very first screenshots to be taken then calculate from the date/time when the user started a screen capture session
        /// otherwise calculate from the date/time when the previous screenshots were taken.
        /// </summary>
        public DateTime DateTimeNextCycle { get { return DateTimePreviousCycle.Ticks == 0 ? DateTimeStartCapture.AddMilliseconds(Interval) : DateTimePreviousCycle.AddMilliseconds(Interval); } }

        /// <summary>
        /// The time remaining between now and the next screenshot that will be taken.
        /// </summary>
        public TimeSpan TimeRemainingForNextScreenshot { get { return DateTimeNextCycle.Subtract(DateTime.Now).Duration(); } }

        /// <summary>
        /// The title of the active window.
        /// </summary>
        public string ActiveWindowTitle { get; set; }

        /// <summary>
        /// The process name associated with the active window.
        /// </summary>
        public string ActiveWindowProcessName { get; set; }

        /// <summary>
        /// Determines if we're capturing the screen now or let a scheduled capture occur.
        /// </summary>
        public bool CaptureNow { get; set; }

        private ImageCodecInfo GetEncoderInfo(string mimeType)
        {
            var encoders = ImageCodecInfo.GetImageEncoders();
            return encoders.FirstOrDefault(t => t.MimeType == mimeType);
        }

        private static string GetMD5Hash(Bitmap bitmap, ImageFormat format)
        {
            byte[] bytes = null;

            using (MemoryStream ms = new MemoryStream())
            {
                bitmap.Save(ms, format.Format);
                bytes = ms.ToArray();
            }

            MD5CryptoServiceProvider md5 = new MD5CryptoServiceProvider();

            byte[] hash = md5.ComputeHash(bytes);

            StringBuilder sb = new StringBuilder();

            foreach (byte b in hash)
            {
                sb.Append(b.ToString("x2").ToLower());
            }

            return sb.ToString();
        }

        private void SaveToFile(string path, ImageFormat format, int jpegQuality, Bitmap bitmap)
        {
            try
            {
                if (bitmap != null && format != null && !string.IsNullOrEmpty(path))
                {
                    if (format.Name.Equals(ImageFormatSpec.NAME_JPEG))
                    {
                        var encoderParams = new EncoderParameters(1);
                        encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, jpegQuality);

                        var encoderInfo = GetEncoderInfo("image/jpeg");

                        bitmap.Save(path, encoderInfo, encoderParams);
                    }
                    else
                    {
                        bitmap.Save(path, format.Format);
                    }

                    Log.WriteMessage("Screenshot saved to file at path \"" + path + "\"");
                }
            }
            catch (Exception ex)
            {
                Log.WriteExceptionMessage("ScreenCapture::SaveToFile", ex);
            }
        }

        /// <summary>
        /// Gets the resolution of the display device associated with the screen.
        /// </summary>
        /// <param name="screen">The Windows Forms screen object associated with the display device.</param>
        /// <returns>A struct having the screen, display device width, and display device height.</returns>
        public static DeviceResolution GetDeviceResolution(System.Windows.Forms.Screen screen)
        {
            DEVMODE dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            EnumDisplaySettings(screen.DeviceName, ENUM_CURRENT_SETTINGS, ref dm);

            DeviceResolution deviceResolution = new DeviceResolution()
            {
                screen = screen,
                width = dm.dmPelsWidth,
                height = dm.dmPelsHeight
            };

            return deviceResolution;
        }

        /// <summary>
        /// Gets the screenshot image from an image file. This is used when we want to show screenshot images.
        /// </summary>
        /// <param name="path">The path of an image file (such as a JPEG or PNG file).</param>
        /// <returns>The image of the file.</returns>
        public Image GetImageByPath(string path)
        {
            try
            {
                Image image = null;

                if (!string.IsNullOrEmpty(path) && FileSystem.FileExists(path))
                {
                    image = FileSystem.GetImage(path);
                }

                CaptureError = false;

                return image;
            }
            catch (Exception ex)
            {
                Log.WriteExceptionMessage("ScreenCapture::GetImageByPath", ex);

                CaptureError = true;

                return null;
            }
            finally
            {
                GC.Collect();
            }
        }

        /// <summary>
        /// Gets the bitmap image of the screen based on X, Y, Width, and Height. This is used by Screens and Regions.
        /// </summary>
        /// <param name="x">The X value of the bitmap.</param>
        /// <param name="y">The Y value of the bitmap.</param>
        /// <param name="width">The Width value of the bitmap.</param>
        /// <param name="height">The Height value of the bitmap.</param>
        /// <param name="resolutionRatio">The resolution ratio to apply to the bitmap.</param>
        /// <param name="mouse">Determines if the mouse pointer should be included in the bitmap.</param>
        /// <returns>A bitmap image representing what we captured.</returns>
        public Bitmap GetScreenBitmap(int x, int y, int width, int height, int resolutionRatio, bool mouse)
        {
            try
            {
                if (width > 0 && height > 0)
                {
                    Size blockRegionSize = new Size(width, height);

                    if (resolutionRatio < IMAGE_RESOLUTION_RATIO_MIN || resolutionRatio > IMAGE_RESOLUTION_RATIO_MAX)
                    {
                        resolutionRatio = 100;
                    }

                    float imageResolutionRatio = (float)resolutionRatio / 100;

                    int destinationWidth = (int)(width * imageResolutionRatio);
                    int destinationHeight = (int)(height * imageResolutionRatio);

                    Bitmap bitmapSource = new Bitmap(width, height);
                    Graphics graphicsSource = Graphics.FromImage(bitmapSource);

                    graphicsSource.CopyFromScreen(x, y, 0, 0, blockRegionSize, CopyPixelOperation.SourceCopy);

                    if (mouse)
                    {
                        CURSORINFO pci;
                        pci.cbSize = Marshal.SizeOf(typeof(CURSORINFO));

                        if (GetCursorInfo(out pci))
                        {
                            if (pci.flags == CURSOR_SHOWING)
                            {
                                var hdc = graphicsSource.GetHdc();
                                DrawIconEx(hdc, pci.ptScreenPos.x - x, pci.ptScreenPos.y - y, pci.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL);
                                graphicsSource.ReleaseHdc();
                            }
                        }
                    }

                    Bitmap bitmapDestination = new Bitmap(destinationWidth, destinationHeight);

                    Graphics graphicsDestination = Graphics.FromImage(bitmapDestination);
                    graphicsDestination.DrawImage(bitmapSource, 0, 0, destinationWidth, destinationHeight);

                    graphicsSource.Flush();
                    graphicsDestination.Flush();

                    CaptureError = false;
                    
                    return bitmapDestination;
                }

                CaptureError = true;

                return null;
            }
            catch (Exception ex)
            {
                // Don't log an error if Windows is locked at the time a screenshot was taken.
                if (!ex.Message.Equals("The handle is invalid"))
                {
                    Log.WriteExceptionMessage("ScreenCapture::GetScreenBitmap", ex);
                }

                CaptureError = true;

                return null;
            }
            finally
            {
                GC.Collect();
            }
        }

        /// <summary>
        /// Gets the bitmap image of the active window.
        /// </summary>
        /// <returns>A bitmap image representing the active window.</returns>
        public Bitmap GetActiveWindowBitmap()
        {
            try
            {
                GetWindowRect(GetForegroundWindow(), out Rectangle rect);

                int width = rect.Width - rect.X;
                int height = rect.Height - rect.Y;

                if (width > 0 && height > 0)
                {
                    Bitmap bitmap = new Bitmap(width, height);

                    Graphics graphics = Graphics.FromImage(bitmap);

                    graphics.CopyFromScreen(new Point(rect.X, rect.Y), new Point(0, 0), new Size(width, height));

                    CaptureError = false;

                    return bitmap;
                }

                CaptureError = true;

                return null;
            }
            catch (Exception ex)
            {
                // Don't log an error if Windows is locked at the time a screenshot was taken.
                if (!ex.Message.Equals("The handle is invalid"))
                {
                    Log.WriteExceptionMessage("ScreenCapture::GetScreenBitmap", ex);
                }

                CaptureError = true;

                return null;
            }
            finally
            {
                GC.Collect();
            }
        }

        /// <summary>
        /// Gets the title of the active window.
        /// </summary>
        /// <returns>The title of the active window.</returns>
        public string GetActiveWindowTitle()
        {
            try
            {
                IntPtr handle;
                int chars = MAX_CHARS;

                StringBuilder buffer = new StringBuilder(chars);

                handle = GetForegroundWindow();

                if (GetWindowText(handle, buffer, chars) > 0)
                {
                    CaptureError = false;

                    // Make sure to strip out the backslash if it's in the window title.
                    return buffer.ToString().Replace(@"\", string.Empty);
                }

                CaptureError = false;

                return "(system)";
            }
            catch (Exception ex)
            {
                Log.WriteExceptionMessage("ScreenCapture::GetActiveWindowTitle", ex);

                CaptureError = true;

                return null;
            }
        }

        /// <summary>
        /// Gets the process name of the application associated with the active window.
        /// </summary>
        /// <returns>The process name of the application associated with the active window.</returns>
        public string GetActiveWindowProcessName()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                GetWindowThreadProcessId(hwnd, out uint pid);
                Process p = Process.GetProcessById((int)pid);
                return p.ProcessName;
            }
            catch (Exception ex)
            {
                Log.WriteExceptionMessage("ScreenCapture::GetActiveWindowProcessName", ex);

                return null;
            }
        }

        /// <summary>
        /// Gets the bitmap images for the avaialble screens.
        /// </summary>
        /// <param name="component">The component to capture. This could be the active window or a screen.</param>
        /// <param name="x">The X value of the bitmap.</param>
        /// <param name="y">The Y value of the bitmap.</param>
        /// <param name="width">The Width value of the bitmap.</param>
        /// <param name="height">The Height value of the bitmap.</param>
        /// <param name="mouse">Determines if we include the mouse pointer in the captured bitmap.</param>
        /// <param name="resolutionRatio">The resolution ratio of the bitmap. A lower value makes the bitmap more blurry.</param>
        /// <param name="bitmap">The bitmap to operate on.</param>
        /// <returns>A boolean to indicate if we were successful in getting a bitmap.</returns>
        public bool GetScreenImages(int component, int x, int y, int width, int height, bool mouse, int resolutionRatio, out Bitmap bitmap)
        {
            try
            {
                bitmap = component == 0
                    ? GetActiveWindowBitmap()
                    : GetScreenBitmap(x, y, width, height, resolutionRatio, mouse);

                if (bitmap != null)
                {
                    CaptureError = false;

                    return true;
                }

                CaptureError = true;

                return false;
            }
            catch (Exception ex)
            {
                Log.WriteExceptionMessage("ScreenCapture::GetScreenImages", ex);

                bitmap = null;

                CaptureError = true;

                return false;
            }
            finally
            {
                GC.Collect();
            }
        }

        /// <summary>
        /// Saves the captured bitmap image as a screenshot to an image file.
        /// </summary>
        /// <param name="path">The filepath of the image file to write to.</param>
        /// <param name="format">The format of the image file.</param>
        /// <param name="component">The component of the screenshot to be saved. This could be the active window or a screen.</param>
        /// <param name="screenshotType">The type of screenshot to save. This could be the active window, a region, or a screen.</param>
        /// <param name="jpegQuality">The JPEG quality setting for JPEG images being saved.</param>
        /// <param name="viewId">The unique identifier to identify a particular region or screen.</param>
        /// <param name="bitmap">The bitmap image to write to the image file.</param>
        /// <param name="label">The current label being used at the time of capture which we will apply to the screenshot object.</param>
        /// <param name="windowTitle">The title of the window being captured.</param>
        /// <param name="processName">The process name of the application being captured.</param>
        /// <param name="screenshotCollection">A collection of screenshot objects.</param>
        /// <returns>A boolean to determine if we successfully saved the screenshot.</returns>
        public bool SaveScreenshot(string path, ImageFormat format, int component, ScreenshotType screenshotType, int jpegQuality,
            Guid viewId, Bitmap bitmap, string label, string windowTitle, string processName, ScreenshotCollection screenshotCollection)
        {
            try
            {
                int filepathLengthLimit = Convert.ToInt32(Settings.Application.GetByKey("FilepathLengthLimit", DefaultSettings.FilepathLengthLimit).Value);
                bool optimizeScreenCapture = Convert.ToBoolean(Settings.Application.GetByKey("OptimizeScreenCapture", DefaultSettings.OptimizeScreenCapture).Value);

                if (!string.IsNullOrEmpty(path))
                {
                    if (path.Length > filepathLengthLimit)
                    {
                        Log.WriteMessage($"File path length exceeds the configured length of {filepathLengthLimit} characters so value was truncated. Correct the value for the FilepathLengthLimit application setting to prevent truncation");
                        path = path.Substring(0, filepathLengthLimit);
                    }

                    Log.WriteMessage("Attempting to write image to file at path \"" + path + "\"");

                    // This is a normal path used in Windows (such as "C:\screenshots\").
                    if (!path.StartsWith(FileSystem.PathDelimiter))
                    {
                        if (FileSystem.DriveReady(path))
                        {
                            int lowDiskSpacePercentageThreshold = Convert.ToInt32(Settings.Application.GetByKey("LowDiskPercentageThreshold", DefaultSettings.LowDiskPercentageThreshold).Value);
                            double freeDiskSpacePercentage = FileSystem.FreeDiskSpacePercentage(path);

                            Log.WriteDebugMessage("Percentage of free disk space on drive for \"" + path + "\" is " + (int)freeDiskSpacePercentage + "% and low disk percentage threshold is set to " + lowDiskSpacePercentageThreshold + "%");

                            if (freeDiskSpacePercentage > lowDiskSpacePercentageThreshold)
                            {
                                string dirName = FileSystem.GetDirectoryName(path);

                                if (!string.IsNullOrEmpty(dirName))
                                {
                                    if (!FileSystem.DirectoryExists(dirName))
                                    {
                                        FileSystem.CreateDirectory(dirName);

                                        Log.WriteDebugMessage("Directory \"" + dirName + "\" did not exist so it was created");
                                    }

                                    Screenshot lastScreenshotOfThisView = screenshotCollection.GetLastScreenshotOfView(viewId);

                                    Screenshot newScreenshotOfThisView = new Screenshot(windowTitle, DateTimeScreenshotsTaken)
                                    {
                                        ViewId = viewId,
                                        Path = path,
                                        Format = format,
                                        Component = component,
                                        ScreenshotType = screenshotType,
                                        ProcessName = processName + ".exe",
                                        Label = label
                                    };

                                    if (optimizeScreenCapture)
                                    {
                                        newScreenshotOfThisView.Hash = GetMD5Hash(bitmap, format);

                                        if (lastScreenshotOfThisView == null || string.IsNullOrEmpty(lastScreenshotOfThisView.Hash) ||
                                            !lastScreenshotOfThisView.Hash.Equals(newScreenshotOfThisView.Hash))
                                        {
                                            screenshotCollection.Add(newScreenshotOfThisView);

                                            SaveToFile(path, format, jpegQuality, bitmap);
                                        }
                                    }
                                    else
                                    {
                                        screenshotCollection.Add(newScreenshotOfThisView);

                                        SaveToFile(path, format, jpegQuality, bitmap);
                                    }
                                }
                            }
                            else
                            {
                                // There is not enough disk space on the drive so log an error message and change the system tray icon's colour to yellow.
                                Log.WriteErrorMessage($"Unable to save screenshot due to lack of available disk space on drive for {path} (at " + freeDiskSpacePercentage + "%) which is lower than the LowDiskPercentageThreshold setting that is currently set to " + lowDiskSpacePercentageThreshold + "%");

                                bool stopOnLowDiskError = Convert.ToBoolean(Settings.Application.GetByKey("StopOnLowDiskError", DefaultSettings.StopOnLowDiskError).Value);

                                if (stopOnLowDiskError)
                                {
                                    Log.WriteErrorMessage("Running screen capture session has stopped because application setting StopOnLowDiskError was set to True when the available disk space on any drive was lower than the value of LowDiskPercentageThreshold");

                                    ApplicationError = true;

                                    return false;
                                }
                                
                                ApplicationWarning = true;
                            }
                        }
                        else
                        {
                            // Drive isn't ready so log an error message.
                            Log.WriteErrorMessage($"Unable to save screenshot for \"{path}\" because the drive is not found or not ready");
                        }
                    }
                    else
                    {
                        // This is UNC network share path (such as "\\SERVER\screenshots\").
                        string dirName = FileSystem.GetDirectoryName(path);

                        if (!string.IsNullOrEmpty(dirName))
                        {
                            try
                            {
                                if (!FileSystem.DirectoryExists(dirName))
                                {
                                    FileSystem.CreateDirectory(dirName);

                                    Log.WriteDebugMessage("Directory \"" + dirName + "\" did not exist so it was created");
                                }

                                Screenshot lastScreenshotOfThisView = screenshotCollection.GetLastScreenshotOfView(viewId);

                                Screenshot newScreenshotOfThisView = new Screenshot(windowTitle, DateTimeScreenshotsTaken)
                                {
                                    ViewId = viewId,
                                    Path = path,
                                    Format = format,
                                    Component = component,
                                    ScreenshotType = screenshotType,
                                    ProcessName = processName + ".exe",
                                    Label = label
                                };

                                if (optimizeScreenCapture)
                                {
                                    newScreenshotOfThisView.Hash = GetMD5Hash(bitmap, format);

                                    if (lastScreenshotOfThisView == null || string.IsNullOrEmpty(lastScreenshotOfThisView.Hash) ||
                                        !lastScreenshotOfThisView.Hash.Equals(newScreenshotOfThisView.Hash))
                                    {
                                        screenshotCollection.Add(newScreenshotOfThisView);

                                        SaveToFile(path, format, jpegQuality, bitmap);
                                    }
                                }
                                else
                                {
                                    screenshotCollection.Add(newScreenshotOfThisView);

                                    SaveToFile(path, format, jpegQuality, bitmap);
                                }
                            }
                            catch (Exception)
                            {
                                // We don't want to stop the screen capture session at this point because there may be other components that
                                // can write to their given paths. If this is a misconfigured path for a particular component then just log an error.
                                Log.WriteErrorMessage($"Cannot write to \"{path}\" because the user may not have the appropriate permissions to access the path");
                            }
                        }
                    }
                }

                return true;
            }
            catch (System.IO.PathTooLongException ex)
            {
                Log.WriteErrorMessage($"The path is too long. I see the path is \"{path}\" but the length exceeds what Windows can handle so the file could not be saved. There is probably an exception error from Windows explaining why");
                Log.WriteExceptionMessage("ScreenCapture::SaveScreenshot", ex);

                // This shouldn't be an error that should stop a screen capture session.
                return true;
            }
            catch (Exception ex)
            {
                Log.WriteExceptionMessage("ScreenCapture::SaveScreenshot", ex);

                return false;
            }
        }

        /// <summary>
        /// Sets the application focus to the defined application using a given process name.
        /// </summary>
        /// <param name="applicationFocus">The name of the process of the application to focus.</param>
        public static void SetApplicationFocus(string applicationFocus)
        {
            if (string.IsNullOrEmpty(applicationFocus)) return;

            Process[] process = Process.GetProcessesByName(applicationFocus);

            foreach (var item in process)
            {
                var proc = Process.GetProcessById(item.Id);

                IntPtr handle = proc.MainWindowHandle;
                SetForegroundWindow(handle);

                var placement = GetPlacement(proc.MainWindowHandle);

                if (placement.showCmd == ShowWindowCommands.Minimized)
                {
                    ShowWindowAsync(proc.MainWindowHandle, (int)ShowWindowCommands.Normal);
                }
            }
        }
    }
}