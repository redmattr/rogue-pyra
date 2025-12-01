// MainMenuForm.cs
// Very simple main menu → opens HostListForm.

using System.Drawing;
using System.Windows.Forms;

namespace RoguePyra.UI {
	public sealed class MainForm : Form {
		private enum Screen { mainMenu, hostList, game }
		private readonly MainMenuScreen _mainMenuScreen;	// Main menu screen.
		private readonly HostListScreen _hostListScreen;	// Lobby selection screen.
		//private readonly GameScreen _gameScreen;			// Game screen.

		public MainForm() {
			Text = "RoguePyra — Main Menu";
			ClientSize = new Size(1680, 1050);
			FormBorderStyle = FormBorderStyle.Sizable;
			MaximizeBox = false;
			StartPosition = FormStartPosition.CenterScreen;
			BackColor = Color.White;

			// Main Menu
			_mainMenuScreen = new MainMenuScreen {
				Width = ClientSize.Width,
				Height = ClientSize.Height,
				Dock = DockStyle.Fill
			};
			_mainMenuScreen.ExitRequested += (_, __) => Close();
			_mainMenuScreen.OpenHostList += (_, __) => SwitchScreen(Screen.hostList);

			// Host List
			_hostListScreen = new HostListScreen {
				Width = ClientSize.Width,
				Height = ClientSize.Height,
				Dock = DockStyle.Fill
			};
			_hostListScreen.ReturnToMainMenu += (_, __) => SwitchScreen(Screen.mainMenu);

			// Game


			// Displays main menu as default screen. Switch screns by removing current screen with Controls.Remove(), then add new screen.
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
				/*case Screen.game:
					Controls.Add(_gameScreen);
					return;*/
				default:
					// TODO: handle this (should be impossible).
					return;
			}
		}
	}
}