using System.Windows.Forms;

namespace WinFormsApp;

public sealed class MainForm : Form
{
    public Button Go { get; } = new() { Text = "Go", Location = new System.Drawing.Point(12, 12) };
    public bool Clicked { get; private set; }

    public MainForm()
    {
        Text = "WinForms fixture";
        ClientSize = new System.Drawing.Size(240, 80);
        Controls.Add(Go);
        Go.Click += OnGoClick;
    }

    private void OnGoClick(object? sender, EventArgs e) => Clicked = true;
}
