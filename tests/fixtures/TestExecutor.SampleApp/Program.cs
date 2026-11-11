using System.Windows.Automation;

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
        _name = Identified.TextBox("sample-name", new Point(24, 24), 240);
        var ok = Identified.Button("sample-ok", "确定", new Point(24, 72));
        _value = Identified.Result("sample-value", new Point(24, 120));
        var secret = Identified.TextBox("sample-secret", new Point(24, 168), 240);
        secret.UseSystemPasswordChar = true;
        secret.Text = "synthetic";
        var browse = Identified.Button("sample-browse", "浏览", new Point(160, 72));
        browse.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = "打开" };
            if (dialog.ShowDialog(this) == DialogResult.OK) _value.Text = Path.GetFileName(dialog.FileName);
        };
        var combo = Identified.Combo("sample-expand", new Point(24, 212));
        combo.Items.AddRange(new object[] { "甲", "乙" });
        combo.SelectedIndex = 0;
        ok.Click += (_, _) => _value.Text = _name.Text;
        Controls.AddRange(new Control[] { _name, ok, browse, _value, secret, combo });
    }
}

internal static class Identified
{
    public static TextBox TextBox(string id, Point location, int width) => new IdTextBox(id) { Name = id, Location = location, Width = width };
    public static Button Button(string id, string text, Point location) => new IdButton(id) { Name = id, Text = text, Location = location, Width = 120 };
    public static Label Result(string id, Point location) => new IdLabel(id) { Name = id, AutoSize = true, Location = location, Text = "" };
    public static ComboBox Combo(string id, Point location) => new IdCombo(id) { Name = id, DropDownStyle = ComboBoxStyle.DropDownList, Location = location, Width = 160 };
}

internal class IdAccessibleObject(Control owner, string id) : Control.ControlAccessibleObject(owner)
{
    public override string? Name => Owner is Label or Button ? Owner.Text : base.Name;

    public override object? GetPropertyValue(int propertyID)
        => propertyID == AutomationElementIdentifiers.AutomationIdProperty.Id ? id : base.GetPropertyValue(propertyID);
}

internal sealed class IdTextBox(string id) : TextBox
{
    protected override AccessibleObject CreateAccessibilityInstance() => new IdAccessibleObject(this, id);
}

internal sealed class IdButton(string id) : Button
{
    protected override AccessibleObject CreateAccessibilityInstance() => new IdAccessibleObject(this, id);
}

internal sealed class IdLabel(string id) : Label
{
    protected override AccessibleObject CreateAccessibilityInstance() => new IdAccessibleObject(this, id);
}

internal sealed class IdCombo(string id) : ComboBox
{
    protected override AccessibleObject CreateAccessibilityInstance() => new IdAccessibleObject(this, id);
}
