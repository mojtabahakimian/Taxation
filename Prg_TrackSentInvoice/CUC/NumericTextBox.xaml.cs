using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Prg_TrackSentInvoice.CUC
{
    public partial class NumericTextBox : UserControl
    {
        public TextBox InnerTextBox { get { return TXB0; } }

        //public new void Focus()
        //{
        //    base.Focus();
        //}
        public void SetFocusToTextBox()
        {
            TXB0.Focus();
            Keyboard.Focus(TXB0);
        }

        private double lastValidValue;
        #region CustomProperties
        public static readonly RoutedEvent UserControlLostFocusEvent =
            EventManager.RegisterRoutedEvent("UserControlLostFocus",
                RoutingStrategy.Bubble,
                typeof(RoutedEventHandler),
                typeof(NumericTextBox));
        public event RoutedEventHandler UserControlLostFocus
        {
            add { AddHandler(UserControlLostFocusEvent, value); }
            remove { RemoveHandler(UserControlLostFocusEvent, value); }
        }

        private string UnformatText(string formattedText)
        {
            //return formattedText?.Replace(",", "") ?? string.Empty;
            return formattedText?.Replace(",", "") ?? "0";
        }

        public static readonly DependencyProperty IsDigitGroupActiveProperty = DependencyProperty.Register(
            "IsDigitGroupActive", typeof(bool), typeof(NumericTextBox), new PropertyMetadata(false));
        public bool IsDigitGroupActive
        {
            get { return (bool)GetValue(IsDigitGroupActiveProperty); }
            set { SetValue(IsDigitGroupActiveProperty, value); }
        }
        public static readonly DependencyProperty DigitGroupOnEnterProperty = DependencyProperty.Register(
            "DigitGroupOnEnter", typeof(bool), typeof(NumericTextBox), new PropertyMetadata(false));
        public bool DigitGroupOnEnter
        {
            get { return (bool)GetValue(DigitGroupOnEnterProperty); }
            set { SetValue(DigitGroupOnEnterProperty, value); }
        }
        public static readonly DependencyProperty ThreeTwoZeroProperty = DependencyProperty.Register(
            "ThreeTwoZero", typeof(bool), typeof(NumericTextBox), new PropertyMetadata(false));
        public bool ThreeTwoZero
        {
            get { return (bool)GetValue(ThreeTwoZeroProperty); }
            set { SetValue(ThreeTwoZeroProperty, value); }
        }
        public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        "IsReadOnly", typeof(bool), typeof(NumericTextBox), new PropertyMetadata(false, ReadOnlyChangedCallback));
        public bool IsReadOnly
        {
            get { return (bool)GetValue(IsReadOnlyProperty); }
            set { SetValue(IsReadOnlyProperty, value); }
        }
        private static void ReadOnlyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctrl = d as NumericTextBox;
            if (ctrl != null)
            {
                ctrl.TXB0.IsReadOnly = (bool)e.NewValue;
            }
        }
        public static readonly DependencyProperty DoesAcceptDoubleProperty = DependencyProperty.Register(
            "DoesAcceptDouble", typeof(bool), typeof(NumericTextBox), new PropertyMetadata(false));
        public bool DoesAcceptDouble
        {
            get { return (bool)GetValue(DoesAcceptDoubleProperty); }
            set { SetValue(DoesAcceptDoubleProperty, value); }
        }

        public static readonly DependencyProperty TextBoxStyleProperty = DependencyProperty.Register(
          "TextBoxStyle", typeof(Style), typeof(NumericTextBox), new PropertyMetadata(default(Style)));

        public Style TextBoxStyle
        {
            get { return (Style)GetValue(TextBoxStyleProperty); }
            set { SetValue(TextBoxStyleProperty, value); }
        }

        public static readonly DependencyProperty CustomUpdateSourceTriggerProperty = DependencyProperty.Register(
            "CustomUpdateSourceTrigger", typeof(UpdateSourceTrigger), typeof(NumericTextBox), new PropertyMetadata(UpdateSourceTrigger.Default));
        public UpdateSourceTrigger CustomUpdateSourceTrigger
        {
            get { return (UpdateSourceTrigger)GetValue(CustomUpdateSourceTriggerProperty); }
            set { SetValue(CustomUpdateSourceTriggerProperty, value); }
        }

        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            "Text", typeof(string), typeof(NumericTextBox), new FrameworkPropertyMetadata("0", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault/*, OnTextChanged*/));
        public string Text
        {
            get { return UnformatText((string)GetValue(TextProperty)); }
            set { SetValue(TextProperty, value); }
        }


        public static readonly DependencyProperty MaxLengthProperty = DependencyProperty.Register(
            "MaxLength", typeof(int), typeof(NumericTextBox), new PropertyMetadata(0, MaxLengthChangedCallback));
        public int MaxLength
        {
            get { return (int)GetValue(MaxLengthProperty); }
            set { SetValue(MaxLengthProperty, value); }
        }
        #endregion
        private static void MaxLengthChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctrl = d as NumericTextBox;
            if (ctrl != null)
            {
                ctrl.TXB0.MaxLength = (int)e.NewValue;
            }
        }
        public NumericTextBox()
        {
            InitializeComponent();
            lastValidValue = 0;


            //var defaultStyle = (Style)FindResource("MaterialDesignOutlinedTextBox");
            //var defaultStyle = (Style)FindResource("FuzzyOut");
            //if (TextBoxStyle == null)
            //{
            //    TextBoxStyle = defaultStyle;
            //}
            this.IsKeyboardFocusWithinChanged += UserControl1_IsKeyboardFocusWithinChanged;
        }
        void UserControl1_IsKeyboardFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.OldValue == true && (bool)e.NewValue == false)
            {
                RaiseEvent(new RoutedEventArgs(NumericTextBox.UserControlLostFocusEvent, this));
            }
        }
        private void TXB0_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (!IsValidInput(e.Text))
            {
                e.Handled = true;
            }
        }
        private void TXB0_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                string pastedText = (string)e.DataObject.GetData(typeof(string));
                if (!IsValidInput(pastedText))
                {
                    e.CancelCommand();
                }
            }
            else
            {
                e.CancelCommand();
            }
        }
        private void TXB0_LostFocus(object sender, RoutedEventArgs e)
        {
            if (CustomUpdateSourceTrigger == UpdateSourceTrigger.LostFocus || CustomUpdateSourceTrigger == UpdateSourceTrigger.Default)
            {
                Text = TXB0.Text;
            }
            if (string.IsNullOrEmpty(TXB0.Text))
            {
                if (string.IsNullOrEmpty(lastValidValue.ToString(CultureInfo.CurrentCulture)))
                    TXB0.Text = "0";
            }
            if (string.IsNullOrEmpty(TXB0.Text))
            {
                TXB0.Text = lastValidValue.ToString(CultureInfo.CurrentCulture);
                FormatNumericValue();
            }
            else if (!double.TryParse(TXB0.Text, out double newValue))
            {
                TXB0.Text = lastValidValue.ToString(CultureInfo.CurrentCulture);
                FormatNumericValue();
            }
            else
            {
                lastValidValue = newValue;
                FormatNumericValue();
            }
        }
        private void TXB0_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (ThreeTwoZero)
            {
                if (e.Key == Key.Add)
                {
                    e.Handled = true;
                    var text = "000";
                    var target = Keyboard.FocusedElement;
                    var routedEvent = TextCompositionManager.TextInputEvent;

                    target.RaiseEvent(
                        new TextCompositionEventArgs(InputManager.Current.PrimaryKeyboardDevice,
                        new TextComposition(InputManager.Current, target, text))
                        { RoutedEvent = routedEvent });
                }
                else if (e.Key == Key.Subtract)
                {
                    e.Handled = true;
                    var text = "00";
                    var target = Keyboard.FocusedElement;
                    var routedEvent = TextCompositionManager.TextInputEvent;

                    target.RaiseEvent(
                        new TextCompositionEventArgs(InputManager.Current.PrimaryKeyboardDevice,
                        new TextComposition(InputManager.Current, target, text))
                        { RoutedEvent = routedEvent });
                }
            }
        }
        private void TXB0_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (CustomUpdateSourceTrigger == UpdateSourceTrigger.PropertyChanged)
            {
                Text = TXB0.Text;
            }
            if (DigitGroupOnEnter is true)
            {
                FormatNumericValue();
            }
        }

        private bool IsValidInput(string input)
        {
            if (DoesAcceptDouble)
            {
                return double.TryParse(input, out _) || IsDecimalSeparator(input);
            }
            else
            {
                return long.TryParse(input, out _) || IsDecimalSeparator(input);
            }
        }
        private bool IsDecimalSeparator(string input)
        {
            return input.Equals(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);
        }
        private void FormatNumericValue()
        {
            if (IsDigitGroupActive && double.TryParse(TXB0.Text, out double numericValue))
            {
                TXB0.TextChanged -= TXB0_TextChanged;

                var text = TXB0;
                if (text.Text.Length == 0) { TXB0.TextChanged += TXB0_TextChanged; return; }
                double range;
                if (!Double.TryParse(text.Text, out range))
                {
                    text.Text = text.Text.Replace(text.Text.Substring(text.Text.Length - 1, 1), "");
                }
                if (text.Text != string.Empty)
                {
                    if (text.Text.Substring(text.Text.Length - 1, 1) == ".") { TXB0.TextChanged += TXB0_TextChanged; return; }

                    // Format with or without digit grouping based on DoesAcceptDouble
                    if (DoesAcceptDouble)
                    {
                        //text.Text = string.Format("{0:#,##0.#}", double.Parse(text.Text.Trim()));
                        text.Text = string.Format("{0:#,##0.##}", double.Parse(text.Text.Trim()));
                    }
                    else
                    {
                        text.Text = string.Format("{0:#,##0}", double.Parse(text.Text.Trim()));
                    }

                    if (text.Text.Length != 0)
                    {
                        text.SelectionStart = text.Text.Length;
                    }
                }

                TXB0.TextChanged += TXB0_TextChanged;
            }
        }
    }
}
