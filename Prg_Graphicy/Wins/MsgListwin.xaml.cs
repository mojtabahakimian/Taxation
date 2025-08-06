using MaterialDesignThemes.Wpf;
using Prg_Graphicy.LMethods;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Prg_Graphicy.Wins
{
    public partial class MsgListwin : Window
    {
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
        #endregion
        public bool IsYesNo { get; set; }
        public IEnumerable<object> TxtMsg { get; set; }
        public string Rang { get; set; }
        public bool IsBigTxt { get; set; }
        /// <summary>
        /// Red :#FFFF0000   Black : #FF000000
        /// </summary>
        /// <param name="_isyesno">آیا به صورت بله یا خیر است ؟</param>
        /// <param name="_txtmsg">متن پیغام شما</param>
        /// <param name="_rang">رنگ دلخواه متن شما</param>
        /// <param name="_isbigtxt">آیا متن زیادی دارد ؟</param>

        public MsgListwin(bool _isyesno, IEnumerable<object> _txtmsg, string _rang = "")
        {
            IsYesNo = _isyesno;
            TxtMsg = _txtmsg;
            Rang = _rang;

            InitializeComponent();

            MSG_DGLISTBOX.ItemsSource = TxtMsg;

            //فقط تایید OK
            if (IsYesNo != true)
            {
                Btn_yes.Visibility = Visibility.Hidden;
                Btn_no.Visibility = Visibility.Hidden;

                Btn_SeeOK.Visibility = Visibility.Visible;

                if (!string.IsNullOrEmpty(Rang))
                {
                    var bc = new BrushConverter();//#FFFF0000
                    MSG_DGLISTBOX.Foreground = (Brush)bc.ConvertFrom(Rang);
                }
            }
            //بله یا خیر Yes and No
            if (IsYesNo == true)
            {
                //نمایش متن به صورت بزرگ اما مدیریت شده نشان داده شود
                Btn_SeeOK.Visibility = Visibility.Hidden;
                Btn_yes.Visibility = Visibility.Visible;
                Btn_no.Visibility = Visibility.Visible;

                if (!string.IsNullOrEmpty(Rang))
                {
                    var bc = new BrushConverter();//#FFFF0000
                    MSG_DGLISTBOX.Foreground = (Brush)bc.ConvertFrom(Rang);
                }
            }
        }

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
        //I Saw its OK
        private void Btn_SeeOK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
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
    }
}
