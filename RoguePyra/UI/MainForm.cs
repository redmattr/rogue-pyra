using System.Drawing;
using System.Windows.Forms;

namespace RoguePyra.UI {
	public sealed class MainForm : Form {
		public enum Screen { Logo, MainMenu, HostList }

		private const string LogoFilepath = "Assets\\Images\\RoguePyraLogo.png"; // Where to look for the logo file. (This may not work in debug mode, but CLI should be fine.)
		private const int LogoDisplayDuration = 2345; // Time to display logo in ms.

		private readonly LogoScreen _logoScreen;			// Displays logo on startup.
		private readonly MainMenuScreen _mainMenuScreen;	// Main menu screen.
		private readonly HostListScreen _hostListScreen;	// Lobby selection screen.

		public MainForm() {
			Text = "RoguePyra — Main Menu";
			StartPosition = FormStartPosition.CenterScreen;
			FormBorderStyle = FormBorderStyle.Sizable;
			ClientSize = new Size(1680, 1050);
			MaximizeBox = true;
			BackColor = Color.White;
			KeyPreview = true;

			// Logo
			_logoScreen = new LogoScreen(this, LogoFilepath, LogoDisplayDuration) {
				BackColor = Color.FromArgb(25,25,25),
				Width = ClientSize.Width,
				Height = ClientSize.Height,
				Dock = DockStyle.Fill
			};

			// Main Menu
			_mainMenuScreen = new MainMenuScreen(this) {
				Width = ClientSize.Width,
				Height = ClientSize.Height,
				Dock = DockStyle.Fill
			};
			_mainMenuScreen.ExitRequested += (_, __) => Close();

			// Host List
			_hostListScreen = new HostListScreen(this) {
				Width = ClientSize.Width,
				Height = ClientSize.Height,
				Dock = DockStyle.Fill
			};

			// Displays main menu as initial screen.
			SwitchScreen(Screen.Logo);
		}

		public void SwitchScreen(Screen newScreen) {
			Controls.Clear();
			switch (newScreen) {
				case Screen.Logo: Controls.Add(_logoScreen); return;
				case Screen.MainMenu: Controls.Add(_mainMenuScreen); return;
				case Screen.HostList: Controls.Add(_hostListScreen); return;
				default:
					// TODO: handle this (should be unreachable, but error just in case).
					return;
			}
		}
	}
}