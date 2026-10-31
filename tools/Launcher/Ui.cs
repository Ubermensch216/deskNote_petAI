using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;

namespace DeskNote.Launcher
{
    /// <summary>
    /// The little there is to show: a progress window while the app is unpacked, and a message if it cannot be.
    /// </summary>
    /// <remarks>
    /// Kept apart from <c>Program</c> so that a start that finds the app already unpacked - every
    /// start but the first - never loads Windows Forms at all. It is the difference between a click
    /// that opens the app at once and one that waits on a UI toolkit it was never going to use.
    /// </remarks>
    internal static class Ui
    {
        private static ProgressForm _form;
        private static int _lastPermille = -1;

        private static bool Korean => string.Equals(
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "ko", StringComparison.OrdinalIgnoreCase);

        public static string Text(string key)
        {
            switch (key)
            {
                case "preparing":
                    return Korean
                        ? "DeskNote를 준비하는 중입니다.\n처음 한 번만 걸립니다."
                        : "Getting DeskNote ready.\nThis only happens the first time.";
                case "no-payload":
                    return Korean
                        ? "이 파일에는 앱이 들어 있지 않습니다. 배포용으로 만든 DeskNote.exe 를 받아 주세요."
                        : "This file does not contain the app. Please get the DeskNote.exe that was built for distribution.";
                default:
                    return Korean ? "DeskNote를 시작할 수 없습니다." : "DeskNote could not be started.";
            }
        }

        /// <summary>Runs <paramref name="work"/> on another thread behind a progress window, and rethrows what it threw.</summary>
        public static void Install(Action work, string message)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Exception failure = null;
            using (var form = new ProgressForm(message))
            {
                _form = form;
                form.Shown += (sender, args) => ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        work();
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                    finally
                    {
                        form.BeginInvoke(new Action(form.Close));
                    }
                });

                Application.Run(form);
                _form = null;
            }

            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        /// <summary>Moves the bar; called from the worker thread, and cheap enough to be called for every file.</summary>
        public static void Report(double fraction)
        {
            var form = _form;
            var permille = (int)(Math.Max(0, Math.Min(1, fraction)) * 1000);
            if (form == null || form.IsDisposed || permille == _lastPermille)
            {
                return;
            }

            _lastPermille = permille;
            form.BeginInvoke(new Action(() => form.Progress = permille));
        }

        public static void Fail(Exception exception)
        {
            Application.EnableVisualStyles();
            MessageBox.Show(
                Text("failed") + "\n\n" + exception.Message,
                "DeskNote",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        private sealed class ProgressForm : Form
        {
            private readonly ProgressBar _bar = new ProgressBar { Minimum = 0, Maximum = 1000, Dock = DockStyle.Bottom, Height = 18 };

            public ProgressForm(string message)
            {
                Text = "DeskNote";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterScreen;
                ControlBox = false;
                MaximizeBox = false;
                MinimizeBox = false;
                TopMost = true;
                ClientSize = new Size(380, 96);
                try
                {
                    Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                }
                catch (Exception)
                {
                    // The icon is a courtesy.
                }

                Controls.Add(new Label
                {
                    Text = message.Replace("\n", Environment.NewLine),
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Padding = new Padding(12),
                });
                Controls.Add(_bar);
            }

            public int Progress
            {
                set { _bar.Value = Math.Max(0, Math.Min(_bar.Maximum, value)); }
            }
        }
    }
}
