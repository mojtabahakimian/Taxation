using MaterialDesignThemes.Wpf;
using Prg_Graphicy.LMethods;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Prg_Graphicy.Wins
{
    public partial class Msgwin : Window
    {
        public bool ClosedByUser { get; set; } = false;
        private bool CanUserClose { get; set; } = false;
        #region HEAD
        private void Btn_Max_Click(object sender, RoutedEventArgs e)
        {
            PackIcon packIcon = new PackIcon();
            switch (WindowState)
            {
                case WindowState.Maximized:
                    //🗖,🗗
                    WindowState = WindowState.Normal;
                    packIcon.Kind = PackIconKind.WindowMaximize;
                    Btn_Max.Content = packIcon;
                    break;
                case WindowState.Normal:
                    WindowState = WindowState.Maximized;
                    packIcon.Kind = PackIconKind.WindowRestore;
                    Btn_Max.Content = packIcon;
                    break;
            }
        }

        private void Btn_Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Btn_Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        #endregion
        //private static Msgwin activeInstance;
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
        public bool IsListy { get; set; }
        public bool IsReallyClosed { get; set; } = false;
        public Msgwin(bool _isyesno, string _txtmsg, string _rang = "", bool _isbigtxt = false, bool _canuserclose = false, string? YesBtnText = null, string? NoBtnText = null)
        {
            IsYesNo = _isyesno;
            TxtMsg = _txtmsg;
            Rang = _rang;
            IsBigTxt = _isbigtxt;

            CanUserClose = _canuserclose;

            //if (activeInstance != null)
            //    activeInstance.Close();

            //activeInstance = this;
            //activeInstance.Closed += (sender, e) => activeInstance = null;

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

            if (!string.IsNullOrEmpty(YesBtnText))
            {
                Btn_yes.Content = YesBtnText;
                Btn_yes.FontSize = 12;
            }
            if (!string.IsNullOrEmpty(NoBtnText))
            {
                Btn_no.Content = NoBtnText;
                Btn_no.FontSize = 12;
            }
        }


        // ...

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
            if (DialogResult != null)
            {
                DialogResult = true;
            }
            Close();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (CanUserClose is true)
                Btn_Closer.IsEnabled = true;
            else
                Btn_Closer.IsEnabled = false;

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
                //if (((FrameworkElement)uie).Parent is DataGridCell || uie is DataGridCell) //Is Foucs really inside the DataGrid
                //{ }
                e.Handled = true;
                CL_TOOLS.Send(Key.Tab);
            }
        }

        private void TitleDrawBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        private void Window_Closed(object sender, System.EventArgs e)
        {
            IsReallyClosed = true;
        }

        private void Btn_Closer_Click(object sender, RoutedEventArgs e)
        {
            ClosedByUser = true;
            this.Close();
        }

        private void Window_ContentRendered(object sender, System.EventArgs e)
        {
        }

        private void Window_Deactivated(object sender, System.EventArgs e)
        {
            this.Topmost = true;
        }
    }
}
