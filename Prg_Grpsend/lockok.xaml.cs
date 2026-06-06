using MaterialDesignThemes.Wpf;
using Prg_Graphicy.Wins;
using Prg_Grpsend.Utility;
using Prg_Moadian.CNNMANAGER;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace Prg_Grpsend
{
    public partial class lockok : Window
    {
        #region Header Window Begin
        //Header Window Begin
        private void Btn_Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
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
        private void TitleDrawBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
            if (e.ClickCount == 2)
            {
                Btn_Max_Click(null, null);
            }
        }
        //Header Window End;
        #endregion

        //public AxTINYLib.AxTiny axTiny1;
        public lockok()
        {
            InitializeComponent();
        }
        CL_CCNNMANAGER dbms = new CL_CCNNMANAGER();

        CL_LOCKWATCH Lockwatch = new CL_LOCKWATCH();
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var RST = dbms.DoGetDataSQL<string>("SELECT SERVERNAM FROM SAZMAN").FirstOrDefault();
            SERVERNAM.Text = RST;
        }
        private void Command3_Click(object sender, RoutedEventArgs e)
        {
            dbms.DoExecuteSQL("UPDATE dbo.SAZMAN SET SERVERNAM = '" + this.SERVERNAM.Text + "'");

            var RST = dbms.DoGetDataSQL<int?>("SELECT COUNT(N_S) AS CN_S FROM DEED_HED").FirstOrDefault();

            if (RST > 31)
            {
                // چک اتصال اولیه — بدون Initialize
                TINYLib.Tiny initTiny = new TINYLib.Tiny();
                initTiny.ServerIP = SERVERNAM.Text;
                initTiny.NetWorkINIT = true;
                // ← بدون Initialize

                if ((int)initTiny.TinyErrCode != 0)
                {
                    new Msgwin(false, Lockwatch.LockReasonError(((int)initTiny.TinyErrCode).ToString())).ShowDialog();
                    Close();
                    return;
                }

                // تطبیق کلید — هر بار instance جدید، UserPassWord قبل از ShowTinyInfo
                bool matched = false;
                foreach (var password in Lockwatch.TheKeys)
                {
                    TINYLib.Tiny tiny = new TINYLib.Tiny();
                    tiny.ServerIP = SERVERNAM.Text;
                    tiny.NetWorkINIT = true;
                    tiny.UserPassWord = password;
                    tiny.ShowTinyInfo = true;
                    // ← بدون Initialize

                    int err = (int)tiny.TinyErrCode;
                    string data = tiny.DataPartition as string ?? "";
                    bool dataValid = !string.IsNullOrEmpty(data)
                                     && data.Replace("0", "").Trim().Length > 0;

                    if (err == 0 && dataValid)
                    {
                        Baseknow.tindata = data;
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    new Msgwin(false, Lockwatch.LockReasonError("2")).ShowDialog();
                    Close();
                    return;
                }
            }

            this.Close();
            Application.Current.Shutdown();
        }

    }
}
