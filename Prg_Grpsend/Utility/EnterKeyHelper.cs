using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;

namespace Prg_Grpsend.Utility
{
    public static class EnterKeyHelper
    {
        public static void SimulateTabKeyPress(this UIElement currentElement)
        {
            if (currentElement == null) return;

            // ایجاد یک درخواست برای حرکت به کنترل بعدی در ترتیب فوکوس
            var request = new TraversalRequest(FocusNavigationDirection.Next);

            // تلاش برای انتقال فوکوس
            // MoveFocus برمی‌گرداند که آیا فوکوس با موفقیت منتقل شده است یا خیر.
            currentElement.MoveFocus(request);
        }
    }
}
