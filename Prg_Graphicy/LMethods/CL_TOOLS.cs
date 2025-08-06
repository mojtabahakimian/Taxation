using System;
using System.Collections;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;

namespace Prg_Graphicy.LMethods
{
    public static class CL_TOOLS
    {
        public class CodeRunnerOnUI
        {
            private DispatcherTimer timer = new DispatcherTimer();
            public CodeRunnerOnUI()
            {
                timer.Interval = TimeSpan.FromSeconds(1);
                timer.Tick += Timer_Tick;
            }
            public void RunCodeWithoutBlockingUI(Action codeToExecute)
            {
                // Start the timer
                timer.Start();

                // Execute the provided code every tick of the timer
                timer.Tick += (sender, e) => codeToExecute.Invoke();
            }

            private void Timer_Tick(object sender, EventArgs e)
            {
                // Code to be executed every tick of the timer
            }
        }
        public static void Send(Key key)
        {
            if (Keyboard.PrimaryDevice != null)
            {
                if (Keyboard.PrimaryDevice.ActiveSource != null)
                {
                    var e = new KeyEventArgs(Keyboard.PrimaryDevice, Keyboard.PrimaryDevice.ActiveSource, 0, key)
                    {
                        RoutedEvent = Keyboard.KeyDownEvent
                    };
                    InputManager.Current.ProcessInput(e);
                    // Note: Based on your requirements you may also need to fire events for:
                    // RoutedEvent = Keyboard.PreviewKeyDownEvent
                    // RoutedEvent = Keyboard.KeyUpEvent
                    // RoutedEvent = Keyboard.PreviewKeyUpEvent
                }
            }
        }

        /// <summary>
        /// Execute each of the specified action, and if the action is failed, go and executes the next action.
        /// </summary>
        /// <param name="actions">The actions.</param>
        public static void OnErrorResumeNext(params Action[] actions)
        {
            OnErrorResumeNext(actions: actions, returnExceptions: false);
        }

        /// <summary>
        /// Execute each of the specified action, and if the action is failed go and executes the next action.
        /// </summary>
        /// <param name="returnExceptions">if set to <c>true</c> return list of exceptions that were thrown by the actions that were executed.</param>
        /// <param name="putNullWhenNoExceptionIsThrown">if set to <c>true</c> and <paramref name="returnExceptions"/> is also <c>true</c>, put <c>null</c> value in the returned list of exceptions for each action that did not threw an exception.</param>
        /// <param name="actions">The actions.</param>
        /// <returns>List of exceptions that were thrown when executing the actions.</returns>
        /// <remarks>
        /// If you set <paramref name="returnExceptions"/> to <c>true</c>, it is possible to get exception thrown when trying to add exception to the list. 
        /// Note that this exception is not handled!
        /// </remarks>
        public static Exception[] OnErrorResumeNext(bool returnExceptions = false, bool putNullWhenNoExceptionIsThrown = false, params Action[] actions)
        {
            var exceptions = returnExceptions ? new ArrayList() : null;
            foreach (var action in actions)
            {
                Exception exp = null;
                try { action.Invoke(); }
                catch (Exception ex) { if (returnExceptions) { exp = ex; } }

                if (exp != null || putNullWhenNoExceptionIsThrown) { exceptions.Add(exp); }
            }
            return (Exception[])(exceptions?.ToArray(typeof(Exception)));
        }
    }
}
