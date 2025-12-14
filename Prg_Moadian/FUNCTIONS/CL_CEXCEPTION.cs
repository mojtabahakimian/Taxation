namespace Prg_Moadian.FUNCTIONS
{
    public class CustomExceptErMsg
    {
        public string? ExpecMsgEr(Exception ex)
        {
            if (ex is HttpRequestException httpEx)
            {
                return "خطای شبکه ای , لطفا از خاموش بودن وی پی خودن و سپس اتصال به اینترنت اطمینان حاصل فرمایید";
            }
            else if (ex.Message is "اعتبار درخواست داده شده منقضی شده است")
            {
                return "اعتبار درخواست داده شده منقضی شده است . این خطا ممکن است به خاطر عدم صحیح بودن ساعت و تاریخ سیستن نیز رخ دهد.";
            }
            else if (ex is System.FormatException formatEx)
            {
                return "کلید خصوصی صحیح نیست";
            }
            else if (ex is Org.BouncyCastle.OpenSsl.PemException pemEx)
            {
                return "کلید خصوصی خالی است.";
            }
            else if (ex is Microsoft.Data.SqlClient.SqlException)
            {
                return "خطا در انجام عملیات با دیتابیس";
            }
            else if (ex is NullyExceptiony nullyEx)
            {
                if (nullyEx.Message == "TaxURL is NULL")
                {
                    return "آدرس مربوط به سایت اصلی یا سندباکس سامانه صحیح نیست !";
                }
                else if (nullyEx.Message == "MemoryTax is NULL")
                {
                    return "حافظه مالیاتی خالی است";
                }
                else if (nullyEx.Message == "PrivateKeyTax is NULL")
                {
                    return "کلید خصوصی خالی است.";
                }
                else if (nullyEx.Message == "Memory Tax is Incorrect")
                {
                    //این خطا رو هم ممکنه بده : 
                    //امضای بسته صحیح نمی باشد
                    return "حافظه مالیاتی صحیح نیست ! این خطا به دلایل زیر رخ میدهد : \n" +
                                "1- عدم صحیح بودن تاریخ سیستم" +
                                "2- عدم صحیح وارد کردن حافظه مالیاتی" +
                                "\n3- عدم تطابق حافظه مالیاتی با سامانه ارسالی , دقت کنید که حافظه مالیاتی سامانه آزمایشی فقط برای سامانه آزمایشی کار میکند. ";
                }
                else if (nullyEx.Message == "Authentication is not completed or incorrect entered")
                {
                    return "حافظه مالیاتی صحیح نیست ! این خطا به دلایل زیر رخ میدهد : " +
                        "\n1- عدم صحیح بودن تاریخ سیستم" +
                        "\n2- عدم صحیح وارد کردن حافظه مالیاتی" +
                        "\n3- عدم تطابق حافظه مالیاتی با سامانه ارسالی , دقت کنید که حافظه مالیاتی سامانه اصلی فقط برای سامانه اصلی کار میکند. " +
                        "\n4- وضعیت کارپوشه خود را بررسی کنید که غیر مجاز نباشد. " +
                        "\n5-  اگر از صحت حافظه مالیاتی خود اطمینان دارید پس احراز هویت شما کامل نیست و باید برای دریافت گواهی الکترونیکی از سایت gica.ir اقدام کنید.";
                }
                else if (nullyEx.Message == "The expiration time has already occurred.")
                {
                    return "The expiration time has already occurred.";
                }
                else if (nullyEx.Message == "MCODE is null")
                {
                    return "کدملی/شناسه ملی حساب مورد نظر خالی است.";
                }
                else if (nullyEx.Message is "HEAD_EXTENDED is null" || nullyEx.Message.Contains("HEAD_LST_EXTENDED not found"))
                {
                    return "سربرگ مودیان مربوط به فاکتور خالی است , ابتدا یکباره دیگر نوع صورت حساب را انتخاب کرده و سپس روی مانده حساب دابل کلیک کنید و دوباره امتحان کنید.";
                }
                else if (nullyEx.Message.Contains("امضای بسته صحیح نمی باشد"))
                {
                    return "تاریخ سیستم صحیح نمی باشد";
                }

                else if (nullyEx.Message == "This invoice has not been registered yet")
                {
                    return "این صورت حساب فروش , در سامانه ثبت نشده , بنا بر این نمی توان آنرا ابطال یا برگشتی ارسال کرد ! ";
                }
                else if (nullyEx.Message == "This invoice cancely still is PENDING")
                {
                    return "ابطالی این صورت حساب قبلا صادر شده اما هنوز در انتظار تایید می باشد , بنابر این نمی توان مجددا ابطالی آنرا ارسال کرد";
                }
                else if (nullyEx.Message == "duplicate cancely request cancel")
                {
                    return "این صورت حساب قبلا ابطال شده , و نمیتوان مجددا آنرا ابطال کرد";
                }
                else if (nullyEx.Message == "irtaxid is null")
                {
                    return "کد صورت حساب مرجع خالی است.";
                }
                else if (nullyEx.Message == "Over Length 11 Ecode for tob 2")
                {
                    return "کد اقتصادی اشتباه است , طول کد اقتصادی برای شخص حقوقی بیش از 11 رقم است.";
                }
                else if (nullyEx.Message == "Over Length 14 Ecode for tob 1")
                {
                    return "کد اقتصادی اشتباه است , طول کد اقتصادی برای شخص حقیقی بیش از 14 رقم است.";
                }
                else if (nullyEx.Message == "NO IMBAA BUT HAS VRA")
                {
                    return "یکی از کالا های این صورت حساب دارای درصد ارزش افزوده مودیان است (تعریف کالا F2) , لطفا بررسی کنید , درصورتی که مالیات ندارد , شناسه کالایی را وارد کنید که نرخ مالیات آن صفر باشد.";
                }
                //برگشتی
                else if (nullyEx.Message == "This invoice returny still is PENDING")
                {
                    return "برگشتی این صورت حساب قبلا صادر شده اما هنوز در انتظار تایید می باشد , بنابر این نم توان مجددا برگشتی آنرا ارسال کرد";
                }
                else if (nullyEx.Message == "duplicate returny request")
                {
                    return "این صورت حساب قبلا برگشت شده , و نمیتوان مجددا آنرا برگشتی آنرا ثبت کرد";
                }
                else if (nullyEx.Message == "Invoice Sent but could not save it to db")
                {
                    return "صورت حساب شما در صف ارسال قرار گرفت,  اما , به دلیل خطا در دیتابیس نتوانستم این رویداد را ثبت کنم , لطفا بررسی کنید";
                }
                else if (nullyEx.Message.Contains("#capInspError#"))
                {
                    return nullyEx.Message.Replace("#capInspError#", string.Empty);
                }
                else if (nullyEx.Message.Contains("#Tbill_insp_cap#"))
                {
                    return nullyEx.Message.Replace("#Tbill_insp_cap#", string.Empty);
                }
                else if (nullyEx.Message.Contains("HEAD_LST not found for invoice"))
                {
                    var FactNum = nullyEx.Message.Replace("HEAD_LST not found for invoice", "");
                    return ($"اطلاعات این فاکتور/حواله {FactNum} ناقص می باشد , لطفا از صحیح بودن سطر های فاکتور اطمینان حاصل فرمایید.");
                }
                else if (nullyEx.Message.Contains("ECODE is null or empty"))
                {
                    return "کد اقتصادی مشتری خالی است. کد اقتصادی را وارد کنید.";
                }
            }
            else if (ex.Message.Contains("Value cannot be null", StringComparison.InvariantCultureIgnoreCase))
            {
                return ($"خطا در انجام عملیات : محتوای تهی در هنگام ارسال وجود دارد و نمیتوان عملیات را ادامه داد");
            }
            else if (ex is NullReferenceException || ex.Message.Contains("Object reference not set to an instance of an object", StringComparison.InvariantCultureIgnoreCase))
            {
                return "خطا در پردازش اطلاعات (NullRef) : اطلاعات ناقص یا خالی وجود دارد.";
            }
            //else
            //{
            //    // Handle other types of exceptions
            //    // Log the error or show a generic error message to the user
            //    // It's generally a good practice to log the details of the exception for debugging purposes
            //    // You can use ex.Message and ex.StackTrace properties to get exception details
            //}
            return ex.Message;
        }
    }
    public class NullyExceptiony : Exception
    {
        public NullyExceptiony() : base()
        {
        }
        public NullyExceptiony(string message) : base(message)
        {
        }
        public NullyExceptiony(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
