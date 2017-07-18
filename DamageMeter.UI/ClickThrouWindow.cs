﻿using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using Data;
using System.Windows.Threading;
using DamageMeter.UI.HUD.Converters;

namespace DamageMeter.UI
{
    public class ClickThrouWindow : Window, INotifyPropertyChanged
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private double _scale = 1;

        private readonly Dispatcher _dispatcher;
        public event PropertyChangedEventHandler PropertyChanged;

        public double Scale
        {
            get => _scale;
            set
            {
                if (value == _scale) return;
                _scale = value;
                _dispatcher.InvokeIfRequired(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Scale")), DispatcherPriority.DataBind);
            }
        }

        private bool _snappedToBottom;
        public bool SnappedToBottom
        {
            get => _snappedToBottom;
            set
            {
                if (value == _snappedToBottom) return;
                _snappedToBottom = value;
                _dispatcher.InvokeIfRequired(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("SnappedToBottom")), DispatcherPriority.DataBind);
            }
        }

        public bool DontClose = false;
        public ClickThrouWindow()
        {
            SnapsToDevicePixels = true;
            AllowsTransparency = GetType().Name == "PopupNotification" || BasicTeraData.Instance.WindowData.AllowTransparency;
            MaxHeight = SystemParameters.MaximizedPrimaryScreenHeight - 200;
            Closing += ClickThrouWindow_Closing;
            WindowStyle = WindowStyle.None;
            Focusable = false;
            BorderThickness = new Thickness(0);
            Topmost = BasicTeraData.Instance.WindowData.Topmost;
            ShowInTaskbar = !BasicTeraData.Instance.WindowData.Topmost;
            Icon = BasicTeraData.Instance.ImageDatabase.Icon;
            SizeToContent = SizeToContent.WidthAndHeight;
            _margin = (Thickness)new TransparencyToMarginConverter().Convert(BasicTeraData.Instance.WindowData.AllowTransparency, typeof(Thickness), null, null);
            MouseLeftButtonDown += Move;
            Loaded += (s, a) =>
            {
                MinWidth = MinWidth * Scale;
                MinHeight = MinHeight * Scale;
                SnapToScreen();
                SizeChanged += (s1, a1) => SnapToScreen();
            };
            ShowActivated = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ResizeMode = ResizeMode.NoResize;
            _dispatcher = Dispatcher.CurrentDispatcher;
            _opacity = GetType().Name == "MainWindow" ? 1 : BasicTeraData.Instance.WindowData.OtherWindowOpacity;
            Scale = BasicTeraData.Instance.WindowData.Scale;
        }

        public Point? LastSnappedPoint = null;
        internal bool _dragged = false;
        private bool _dragging = false;
        private int _oldHeight;
        private int _oldWidth;
        private Thickness _margin;
        private double _opacity;
        public bool Visible;
        protected virtual bool Empty => false;

        public void SnapToScreen()
        {
            if (_dragging) return;
            if (Empty && Visible) { _oldHeight = 0; HideWindow(true); return; }
            var screen = Screen.FromHandle(new WindowInteropHelper(this).Handle);
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget == null) return;
            var m = source.CompositionTarget.TransformToDevice;
            var dx = m.M11;
            var dy = m.M22;
            var width = ActualWidth * dx;
            var height = ActualHeight * dy;
            if (!_dragged && (int)width==_oldWidth && (int)height==_oldHeight) return;
            _oldWidth = (int) width;
            _oldHeight = (int) height;
            var newLeft = (_dragged ? Left : LastSnappedPoint?.X ?? Left) * dx;
            var newTop = (_dragged ? Top : LastSnappedPoint?.Y ?? Top) * dy;
            var snapLeft = newLeft;
            var snapTop = newTop;
            var area = TeraWindow.IsTeraFullScreen() ? screen.Bounds : screen.WorkingArea;
            if (area.X + area.Width < newLeft + width + 40 * dx)
            {
                newLeft = area.X + area.Width - width + _margin.Right * dx;
                snapLeft = screen.Bounds.X + screen.Bounds.Width - MinWidth * dx;
            }
            else if (area.X > newLeft - 40 * dx)
            {
                newLeft = area.X - _margin.Left * dx;
                snapLeft = screen.Bounds.X;
            }
            if (area.Y + area.Height < newTop + height + 40 * dy)
            {
                newTop = area.Y + area.Height - height + _margin.Bottom * dy;
                snapTop = screen.Bounds.Y + screen.Bounds.Height - MinHeight * dy;
                SnappedToBottom = true;
            }
            else if (area.Y > newTop - 40 * dy)
            {
                newTop = area.Y - _margin.Top * dy;
                snapTop = screen.Bounds.Y;
                SnappedToBottom = false;
            }
            else SnappedToBottom = false;
            Left = newLeft / dx;
            Top = newTop / dy;
            if (_dragged) LastSnappedPoint = new Point(snapLeft / dx, snapTop / dy);
            _dragged = false;
            if (Visible & Visibility == Visibility.Hidden) ShowWindow();
        }

        public void SetClickThrou()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            WindowsServices.SetWindowExTransparent(hwnd);
        }

        public void UnsetClickThrou()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            WindowsServices.SetWindowExVisible(hwnd);
        }

        public void Move(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (e.LeftButton != MouseButtonState.Pressed) return;
                _dragging = true;
                DragMove();
                _dragged = true;
                _dragging = false;
                SnapToScreen();
            }
            catch { Console.WriteLine(@"Exception Move"); }
        }

        protected void ClickThrouWindow_Closing(object sender, CancelEventArgs e)
        {
            if (DontClose)
            {
                e.Cancel = true;
                if (Visibility == Visibility.Visible) HideWindow();
                return;
            }
            Closing -= ClickThrouWindow_Closing;
            foreach (ClickThrouWindow window in ((ClickThrouWindow)sender).OwnedWindows)
            {
                window.DontClose = false;
                window.Close();
            }
            if (BasicTeraData.Instance.WindowData.AllowTransparency)
            {
                _dispatcher.BeginInvoke(new Action(() =>
                {
                    var a = OpacityAnimation(0);
                    a.Completed += (o, args) => { Close(); };
                    BeginAnimation(OpacityProperty, a);
                }));
                e.Cancel = true;
            }
        }

        public void HideWindow(bool set = false)
        {
            Visible = set;
            if (BasicTeraData.Instance.WindowData.AllowTransparency)
            {
                _dispatcher.BeginInvoke(new Action(() =>
                {
                    var a = OpacityAnimation(0);
                    a.Completed += (o, args) => { Visibility = Visibility.Hidden; };
                    BeginAnimation(OpacityProperty, a);
                }));
            }
            else { Visibility = Visibility.Hidden; }
        }

        public void ShowWindow()
        {
            Visible = true;
            if (!Empty)
            {
                if (BasicTeraData.Instance.WindowData.AllowTransparency)
                {
                    //Opacity = 0;
                    _dispatcher.BeginInvoke(new Action(() => { BeginAnimation(OpacityProperty, OpacityAnimation(_opacity)); }));
                }
                Visibility = Visibility.Visible;
            }
            SnapToScreen();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source.AddHook(WindowsServices.ClickNoFocus);
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);

            //Set the window style to noactivate.
            var helper = new WindowInteropHelper(this);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, GetWindowLong(helper.Handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
        }

        private static DoubleAnimation OpacityAnimation(double to)
        {
            return new DoubleAnimation(to, TimeSpan.FromMilliseconds(300)) { EasingFunction = new QuadraticEase() };
        }

        [DllImport("user32.dll")]
        public static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}