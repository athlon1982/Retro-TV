using System.Text;

namespace Jellyfin.Plugin.RetroTv.Catalog;

/// <summary>
/// Writes the .nfo sidecar for one catalog item. Same shape as buildNfo()
/// in src/catalog_builder.mjs, so a plugin build and a Node build produce
/// identical NFOs for the same item.
/// </summary>
public static class NfoWriter
{
    public static string BuildNfo(CatalogEntry entry)
    {
        var runtimeMin = entry.RuntimeSeconds.HasValue
            ? (entry.RuntimeSeconds.Value > 0 ? System.Math.Max(1, (int)System.Math.Round(entry.RuntimeSeconds.Value / 60.0)) : 0)
            : 0;

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.AppendLine("<movie>");
        sb.AppendLine($"  <title>{EscXml(entry.Title)}</title>");
        sb.AppendLine($"  <originaltitle>{EscXml(entry.Title)}</originaltitle>");
        if (entry.Year.HasValue)
        {
            sb.AppendLine($"  <year>{entry.Year.Value}</year>");
        }

        if (!string.IsNullOrEmpty(entry.Description))
        {
            sb.AppendLine($"  <plot>{EscXml(entry.Description)}</plot>");
        }

        if (runtimeMin > 0)
        {
            sb.AppendLine($"  <runtime>{runtimeMin}</runtime>");
        }

        sb.AppendLine($"  <genre>{EscXml(entry.Folder)}</genre>");
        if (!string.IsNullOrEmpty(entry.PosterUrl))
        {
            sb.AppendLine($"  <thumb>{EscXml(entry.PosterUrl)}</thumb>");
        }

        sb.AppendLine("  <tag>Retro TV</tag>");
        sb.AppendLine(entry.Tier == "pd"
            ? "  <tag>public domain</tag>"
            : "  <tag>user-uploaded - rights unverified</tag>");
        sb.AppendLine($"  <uniqueid type=\"archive.org\" default=\"true\">{EscXml(entry.Identifier)}</uniqueid>");
        sb.AppendLine("</movie>");
        return sb.ToString();
    }

    internal static string EscXml(string? s)
    {
        return (s ?? string.Empty)
            .Replace("&", "&amp;", System.StringComparison.Ordinal)
            .Replace("<", "&lt;", System.StringComparison.Ordinal)
            .Replace(">", "&gt;", System.StringComparison.Ordinal)
            .Replace("\"", "&quot;", System.StringComparison.Ordinal)
            .Replace("'", "&apos;", System.StringComparison.Ordinal);
    }
}
