namespace RoguePyra.UI;

using System.IO;
using System.Threading.Tasks;
using System.Drawing;
using System.Windows.Forms;

// Displays the game logo on client startup.
internal class LogoScreen : UserControl {
	private readonly MainForm _mainForm;
	private readonly PictureBox _logo;
	private readonly int _logoDisplayDuration;

	public LogoScreen(MainForm mainForm, string logoFilepath, int logoDisplayDuration) {
		_mainForm = mainForm;
		_logoDisplayDuration = logoDisplayDuration;
		Image? logoImage = null;
		try { logoImage = Image.FromFile(logoFilepath); }
		catch (FileNotFoundException) { /* This could be extended to error, but for now it just doesn't display an image. */ }
		_logo = new PictureBox() {
			Image = logoImage,
			SizeMode = PictureBoxSizeMode.Zoom,
			Width = mainForm.Width / 4,
			Height = mainForm.Height / 4,
			Location = new Point(mainForm.Width * 3/8, mainForm.Height * 2/8)
		};
		Controls.Add(_logo);
		LoadMenuDeferred();
	}

	private async Task LoadMenuDeferred() {
		await Task.Delay(_logoDisplayDuration);
		_mainForm.SwitchScreen(MainForm.Screen.MainMenu);
	}
}