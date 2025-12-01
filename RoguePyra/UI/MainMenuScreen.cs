// MainMenuScreen.cs
// Very simple main menu.

namespace RoguePyra.UI;

using System;
using System.Drawing;
using System.Windows.Forms;
internal sealed class MainMenuScreen : UserControl {
	public event EventHandler ExitRequested; // Used to tell the containing form to close.
	public event EventHandler OpenHostList; // Used to tell the containing form to switch to HostListScreen.

	private readonly Button _btnPlay;   // Play button - opens HostListForm.
	private readonly Button _btnExit;   // Exit button - quits game.
	private readonly Label _title;      // Game title text.
	
	public MainMenuScreen(Form mainForm) {
		// Game Title
		_title = new Label {
			Text = "RoguePyra",
			Font = new Font("Segoe UI", 24, FontStyle.Bold),
			AutoSize = true,
			Location = new Point((mainForm.Width - Width) / 2, 40)
		};
		Controls.Add(_title);

		// Play Button
		_btnPlay = new Button {
			Text = "Play",
			Font = new Font("Segoe UI", 12),
			Size = new Size(180, 44),
			Location = new Point((mainForm.Width - Width) / 2, 120)
		};
		_btnPlay.Click += (_, __) => OpenHostList?.Invoke(this, EventArgs.Empty);
		Controls.Add(_btnPlay);

		// Exit Button
		_btnExit = new Button {
			Text = "Exit",
			Font = new Font("Segoe UI", 12),
			Size = new Size(180, 44),
			Location = new Point((mainForm.Width - Width) / 2, 180)
		};
		_btnExit.Click += (_, __) => ExitRequested?.Invoke(this, EventArgs.Empty);
		Controls.Add(_btnExit);
	}
}