namespace CUE4Mcp.Tools;

public static class Cursor
{
    public static int Parse(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return 0;

        try
        {
            byte[] cursorBytes = Convert.FromBase64String(cursor);
            string cursorText = System.Text.Encoding.UTF8.GetString(cursorBytes);
            return int.Parse(cursorText);
        }
        catch (Exception ex)
        {
            throw new ArgumentException("Invalid cursor", nameof(cursor), ex);
        }
    }

    public static string Encode(int position)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(position.ToString());
        return Convert.ToBase64String(bytes);
    }
}
