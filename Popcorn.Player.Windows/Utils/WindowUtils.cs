using System;
using System.Reflection.Metadata;
using System.Windows;
using System.Windows.Media;

namespace Popcorn.Player.Windows.Utils;

public static class WindowUtils
{
    public static void SetWindowPopupStyle(bool setPopup, nint handle)
    {
        var windowLong = Win32Functions.GetWindowLong(handle, Win32Functions.GWL_STYLE);
        Win32Functions.SetWindowLong(handle, Win32Functions.GWL_STYLE,
            setPopup ? windowLong | Win32Functions.WS_POPUP : windowLong & ~Win32Functions.WS_POPUP);
    }

    public static WindowRect GetScreenWorkingArea(Visual visual)
    {
        var rect = new WindowRect();
        var workArea = Win32Functions.GetScreenWorkingArea();

        var source = PresentationSource.FromVisual(visual);
        if (source?.CompositionTarget != null)
        {
            var dpiX = source.CompositionTarget.TransformToDevice.M11;
            var dpiY = source.CompositionTarget.TransformToDevice.M22;

            rect.Width = workArea.Right / dpiX;
            rect.Height = workArea.Bottom / dpiY;
            rect.Top = workArea.Top;
            rect.Left = workArea.Left;
        }
        return rect;
    }

    public static WindowRect GetCurrentWindowRect(Window window) => new ()
    {
        Width = window.Width,
        Height = window.Height,
        Top = window.Top,
        Left = window.Left
    };

    public class WindowRect
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public double Top { get; set; }
        public double Left { get; set; }

        public override bool Equals(object obj)
        {
            if(obj != null &&  obj is WindowRect inputRect)
            {
                return Math.Abs(inputRect.Width - Width) < 0.01 &&
                        Math.Abs(inputRect.Height - Height) < 0.01 &&
                        Math.Abs(inputRect.Top - Top) < 0.01 &&
                        Math.Abs(inputRect.Left - Left) < 0.01;
            }
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + Width.GetHashCode();
                hash = hash * 23 + Height.GetHashCode();
                hash = hash * 23 + Top.GetHashCode();
                hash = hash * 23 + Left.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(WindowRect rect1, WindowRect rect2)
        {
            if (ReferenceEquals(rect1, rect2)) return true;
            if (rect1 is null || rect2 is null) return false;
            return rect1.Equals(rect2);
        }

        public static bool operator !=(WindowRect rect1, WindowRect rect2)
        {
            return !(rect1 == rect2);
        }
    }
}

