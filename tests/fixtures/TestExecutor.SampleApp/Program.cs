namespace TestExecutor.SampleApp;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new SampleForm());
    }
}

internal sealed class SampleForm : Form
{
    private readonly TextBox _name;
    private readonly Label _value;

    public SampleForm()
    {
        Text = "合成被测窗口";
        StartPosition = FormStartPosition.Manual;
        Location = new Point(40, 40);
        ClientSize = new Size(420, 280);
        _name = new TextBox { Name = "sample-name", Location = new Point(24, 24), Width = 240 };
        var ok = new Button { Name = "sample-ok", Text = "确定", Location = new Point(24, 72), Width = 120 };
        _value = new Label { Name = "sample-value", AutoSize = true, Location = new Point(24, 120), Text = "" };
        var secret = new TextBox { Name = "sample-secret", UseSystemPasswordChar = true, Location = new Point(24, 168), Width = 240, Text = "synthetic" };
        var browse = new Button { Name = "sample-browse", Text = "浏览", Location = new Point(160, 72), Width = 120 };
        browse.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = "打开" };
            if (dialog.ShowDialog(this) == DialogResult.OK) _value.Text = Path.GetFileName(dialog.FileName);
        };
        var combo = new ComboBox { Name = "sample-expand", DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(24, 212), Width = 160 };
        combo.Items.AddRange(new object[] { "甲", "乙" });
        combo.SelectedIndex = 0;
        ok.Click += (_, _) => _value.Text = _name.Text;
        Controls.AddRange(new Control[] { _name, ok, browse, _value, secret, combo });
    }
}
