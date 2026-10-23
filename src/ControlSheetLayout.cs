namespace Ror2Common
{
    public static class ControlSheetLayout
    {
        // Add space between actions without changing line spacing inside a wrapped action.
        public static void SpaceRows(ControlSheet sheet)
        {
            foreach (var sections in new[] { sheet.Gamepad, sheet.Keyboard })
                foreach (var section in sections)
                    for (int i = 0; i < section.Rows.Count - 1; i++)
                    {
                        var row = section.Rows[i];
                        row.Action += "\n<size=50%> </size>";
                        section.Rows[i] = row;
                    }
        }
    }
}
