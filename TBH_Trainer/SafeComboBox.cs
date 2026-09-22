namespace TBH_Trainer;

/// <summary>
/// ComboBox that survives a WinForms bug: in DropDownList style, keys such as
/// Ctrl+Backspace make ProcessCmdKey call Select() with a garbage (negative)
/// SelectionStart, which throws ArgumentOutOfRangeException and kills the app.
/// </summary>
internal sealed class SafeComboBox : ComboBox
{
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        try
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }
        catch (ArgumentOutOfRangeException) when (DropDownStyle == ComboBoxStyle.DropDownList)
        {
            return true; // no editable text to act on — swallow the key
        }
    }
}
