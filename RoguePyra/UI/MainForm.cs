using System.Drawing;
using System.Windows.Forms;

namespace RoguePyra.UI {
	public sealed class MainForm : Form {
		private enum Screen { mainMenu, hostList }
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

			// Main Menu
			_mainMenuScreen = new MainMenuScreen(this) {
				Width = ClientSize.Width,
				Height = ClientSize.Height,
				Dock = DockStyle.Fill
			};
			_mainMenuScreen.ExitRequested += (_, __) => Close();
			_mainMenuScreen.OpenHostList += (_, __) => SwitchScreen(Screen.hostList);

			// Host List
			_hostListScreen = new HostListScreen() {
				Width = ClientSize.Width,
				Height = ClientSize.Height,
				Dock = DockStyle.Fill
			};
			_hostListScreen.ReturnToMainMenu += (_, __) => SwitchScreen(Screen.mainMenu);

			// Displays main menu as initial screen.
			SwitchScreen(Screen.mainMenu);
		}

		private void SwitchScreen(Screen newScreen) {
			Controls.Clear();
			switch (newScreen) {
				case Screen.mainMenu:
					Controls.Add(_mainMenuScreen);
					return;
				case Screen.hostList:
					Controls.Add(_hostListScreen);
					return;
				default:
					// TODO: handle this (should be unreachable, but error just in case).
					return;
			}
		}
	}
}