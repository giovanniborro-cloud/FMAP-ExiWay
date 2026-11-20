namespace SchneiderLoader;

internal sealed class OperatorRoleDialog : Form
{
    public bool Installer { get; private set; }

    public OperatorRoleDialog()
    {
        Text = "OVA-GEN3 · Seleziona operatore";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        var layout = new FlowLayoutPanel
        {
            AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(16)
        };
        layout.Controls.Add(new Label { Text = "Con quale ruolo vuoi operare?", AutoSize = true });
        var installer = new Button { Text = "Tecnico Installatore", Width = 230, Height = 34 };
        var expert = new Button { Text = "Operatore Esperto", Width = 230, Height = 34 };
        installer.Click += (_, _) => { Installer = true; DialogResult = DialogResult.OK; };
        expert.Click += (_, _) => { Installer = false; DialogResult = DialogResult.OK; };
        layout.Controls.AddRange([installer, expert]);
        Controls.Add(layout);
    }
}
