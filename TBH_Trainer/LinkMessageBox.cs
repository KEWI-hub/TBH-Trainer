using System.Diagnostics;
using System.Media;

namespace TBH_Trainer;

/// <summary>
/// A message box with a link in it. MessageBox renders a URL as plain text, so anyone told to
/// "download it from this address" has to retype the address; this gives them something to
/// click. Same shape of call as MessageBox.Show, minus the buttons nobody needed here.
/// </summary>
internal static class LinkMessageBox
{
    public static void Show(IWin32Window? owner, string title, string message,
                            string linkText, string url, MessageBoxIcon icon = MessageBoxIcon.Information)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(460, 10),
            BackColor = SystemColors.Control,
        };

        var text = new Label
        {
            Text = message,
            Location = new Point(16, 16),
            MaximumSize = new Size(428, 0),
            AutoSize = true,
        };

        var link = new LinkLabel
        {
            Text = linkText,
            Location = new Point(16, 0),          // placed once the text height is known
            MaximumSize = new Size(428, 0),
            AutoSize = true,
        };
        link.LinkClicked += (_, _) =>
        {
            // UseShellExecute so the user's default browser opens it rather than this process.
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
        };

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Size = new Size(88, 28) };

        form.Controls.Add(text);
        form.Controls.Add(link);
        form.Controls.Add(ok);
        form.AcceptButton = ok;
        form.CancelButton = ok;

        // Lay out top to bottom now that every label has measured itself.
        link.Top = text.Bottom + 10;
        ok.Location = new Point(form.ClientSize.Width - ok.Width - 16, link.Bottom + 14);
        form.ClientSize = new Size(form.ClientSize.Width, ok.Bottom + 14);

        if (icon == MessageBoxIcon.Error) SystemSounds.Hand.Play();
        else if (icon == MessageBoxIcon.Warning) SystemSounds.Exclamation.Play();
        else SystemSounds.Asterisk.Play();

        form.ShowDialog(owner);
    }
}
