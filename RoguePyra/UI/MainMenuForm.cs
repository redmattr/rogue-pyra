// MainMenuForm.cs
// Very simple main menu → opens HostListForm.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace RoguePyra.UI
{
    public sealed class MainMenuForm : Form
    {
        private Button _btnPlay;
        private Button _btnExit;
        private Label _title;

        public MainMenuForm()
        {
            Text = "RoguePyra — Main Menu";
            ClientSize = new Size(520, 300);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;

            _title = new Label
            {
                Text = "RoguePyra",
                Font = new Font("Segoe UI", 24, FontStyle.Bold),
                AutoSize = true,
                Location = new Point((ClientSize.Width - 220) / 2, 40)
            };
            Controls.Add(_title);

            _btnPlay = new Button
            {
                Text = "Play",
                Font = new Font("Segoe UI", 12),
                Size = new Size(180, 44),
                Location = new Point((ClientSize.Width - 180) / 2, 120)
            };
            _btnPlay.Click += (_, __) =>
            {
                var hosts = new HostListForm();
                hosts.Show();
            };
            Controls.Add(_btnPlay);

            _btnExit = new Button
            {
                Text = "Exit",
                Font = new Font("Segoe UI", 12),
                Size = new Size(180, 44),
                Location = new Point((ClientSize.Width - 180) / 2, 180)
            };
            _btnExit.Click += (_, __) => Close();
            Controls.Add(_btnExit);
        }
    }
}
