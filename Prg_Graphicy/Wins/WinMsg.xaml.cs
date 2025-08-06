using Prg_Graphicy.LMethods;
using System.Runtime.InteropServices;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Prg_Graphicy.Wins
{
    public partial class WinMsg : Window
    {
        #region SPECAIL_WIN
        private void HideMinimizeAndMaximizeButtons()
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var currentStyle = GetWindowLong(hwnd, GWL_STYLE);

            SetWindowLong(hwnd, GWL_STYLE, (currentStyle & ~WS_MINIMIZEBOX & ~WS_MAXIMIZEBOX));
        }

        private const int GWL_STYLE = -16;
        private const int WS_MINIMIZEBOX = 0x20000;
        private const int WS_MAXIMIZEBOX = 0x10000;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        #endregion

        public bool IsYesNo { get; set; }
        public string TxtMsg { get; set; }
        public string Rang { get; set; }
        public bool IsBigTxt { get; set; }
        /// <summary>
        /// Red :#FFFF0000   Black : #FF000000
        /// </summary>
        /// <param name="_isyesno">آیا به صورت بله یا خیر است ؟</param>
        /// <param name="_txtmsg">متن پیغام شما</param>
        /// <param name="_rang">رنگ دلخواه متن شما</param>
        /// <param name="_isbigtxt">آیا متن زیادی دارد ؟</param>
        public WinMsg(bool _isyesno, string _txtmsg, string _rang = "", bool _isbigtxt = false)
        {
            IsYesNo = _isyesno;
            TxtMsg = _txtmsg;
            Rang = _rang;
            IsBigTxt = _isbigtxt;

            InitializeComponent();
            //فقط تایید OK
            if (IsYesNo != true)
            {
                if (IsBigTxt == true)
                {
                    Btn_yes.Visibility = Visibility.Hidden;
                    Btn_no.Visibility = Visibility.Hidden;
                    MsgTextNote.Visibility = Visibility.Hidden;

                    Btn_SeeOK.Visibility = Visibility.Visible;
                    MsgTextBig.Visibility = Visibility.Visible;
                    MsgTextBig.Text = TxtMsg;
                }
                else
                {
                    Btn_yes.Visibility = Visibility.Hidden;
                    Btn_no.Visibility = Visibility.Hidden;
                    MsgTextBig.Visibility = Visibility.Hidden;

                    Btn_SeeOK.Visibility = Visibility.Visible;
                    MsgTextNote.Visibility = Visibility.Visible;
                    MsgTextNote.Text = TxtMsg;
                }

                if (!string.IsNullOrEmpty(Rang))
                {
                    var bc = new BrushConverter();//#FFFF0000
                    MsgTextNote.Foreground = (Brush)bc.ConvertFrom(Rang);
                }
            }
            //بله یا خیر Yes and No
            if (IsYesNo == true)
            {
                if (IsBigTxt == true)
                {
                    //بله یا خیر با متن بزرگ که از سمت راست شروع میشه
                    Btn_SeeOK.Visibility = Visibility.Hidden;
                    MsgTextNote.Visibility = Visibility.Hidden;

                    MsgTextBig.Visibility = Visibility.Visible;
                    Btn_yes.Visibility = Visibility.Visible;
                    Btn_no.Visibility = Visibility.Visible;
                    MsgTextBig.Text = TxtMsg;
                }
                else
                {
                    //نمایش متن به صورت بزرگ اما مدیریت شده نشان داده شود
                    Btn_SeeOK.Visibility = Visibility.Hidden;
                    MsgTextBig.Visibility = Visibility.Hidden;

                    Btn_yes.Visibility = Visibility.Visible;
                    Btn_no.Visibility = Visibility.Visible;
                    MsgTextNote.Visibility = Visibility.Visible;
                    MsgTextNote.Text = TxtMsg;
                }
                if (!string.IsNullOrEmpty(Rang))
                {
                    var bc = new BrushConverter();//#FFFF0000
                    MsgTextBig.Foreground = (Brush)bc.ConvertFrom(Rang);
                }
            }

            SourceInitialized += (x, y) => this.HideMinimizeAndMaximizeButtons();
        }

        //For YesorNO {
        private void Btn_yes_Click(object sender, RoutedEventArgs e)
        {
            //Say Yes
            DialogResult = true; // YES
            Close();
        }
        private void Btn_no_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false; // NO
            Close();
        }
        //For YesorNO }

        //I Saw its OK
        private void Btn_SeeOK_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this.Topmost = true;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            this.Topmost = false;
        }
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Btn_no.IsFocused || Btn_SeeOK.IsFocused || Btn_yes.IsFocused) { return; }

            UIElement uie = e.OriginalSource as UIElement;
            if (e.Key is Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (((FrameworkElement)uie).Parent is DataGridCell || uie is DataGridCell) //Is Foucs really inside the DataGrid
                { }
                e.Handled = true;
                CL_TOOLS.Send(Key.Tab);
            }
        }

        private void Window_StateChanged(object sender, System.EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                this.WindowState = WindowState.Normal;
            }
        }
    }
}
