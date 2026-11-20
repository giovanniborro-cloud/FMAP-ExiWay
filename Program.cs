namespace SchneiderLoader;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var role = new OperatorRoleDialog();
        if (role.ShowDialog() == DialogResult.OK)
            Application.Run(new MainForm(role.Installer));
    }
}
