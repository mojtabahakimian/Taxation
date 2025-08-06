using Hardcodet.Wpf.TaskbarNotification;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prg_TrackSentInvoice.LMETHOD
{
    public class NotificationManager
    {
        private TaskbarIcon _taskbarIcon;

        public NotificationManager(TaskbarIcon taskbarIcon)
        {
            _taskbarIcon = taskbarIcon ?? throw new ArgumentNullException(nameof(taskbarIcon));
        }

        public async void ShowNotification(string message, string tooltip, TimeSpan displayDuration, string targetPath)
        {
            var notification = new Notification
            {
                Message = message,
                Tooltip = tooltip,
                TargetPath = targetPath
            };

            _taskbarIcon.ShowBalloonTip(notification.Message, notification.Tooltip, BalloonIcon.Info);

            await Task.Delay(displayDuration); // Delay the execution for the specified display duration

            // After the delay, close the notification if it's still active
            if (!notification.Closed)
            {
                _taskbarIcon.CloseBalloon();
                notification.Closed = true;
            }
        }
    }

    public class Notification
    {
        public string Message { get; set; }
        public string Tooltip { get; set; }
        public string TargetPath { get; set; }
        public bool Closed { get; set; }
    }
}
