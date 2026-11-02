using System.IO.Compression;
using System.Xml;

namespace ItsTool.Infrastructure.Import;

/// <summary>
/// Reads the cell values of an .xlsx workbook without an Excel library: a workbook is a zip of XML files. A formula
/// cell gives the value Excel last calculated. Each part is capped in size, so a crafted file cannot use up memory,
/// and the XML reader refuses DTDs.
/// </summary>
public static class XlsxReader
{
    public const long MaxPartBytes = 20L * 1024 * 1024;
    private const string RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>A sheet's non-empty cells: row number to (column number to text), both counted from 1.</summary>
    public sealed record Sheet(string Name, IReadOnlyDictionary<int, IReadOnlyDictionary<int, string>> Rows)
    {
        public string Cell(int row, int column) =>
            Rows.TryGetValue(row, out var cells) && cells.TryGetValue(column, out var value) ? value : string.Empty;
    }

    /// <exception cref="ArgumentException">The file is not an .xlsx workbook.</exception>
    public static IReadOnlyList<Sheet> Read(Stream workbook)
    {
        try
        {
            using var zip = new ZipArchive(workbook, ZipArchiveMode.Read, leaveOpen: true);
            var shared = ReadSharedStrings(zip);
            var targets = ReadRelationships(zip);
            var sheets = new List<Sheet>();
            foreach (var (name, relationshipId) in ReadSheetList(zip))
            {
                if (!targets.TryGetValue(relationshipId, out var target)) continue;
                var entry = zip.GetEntry(target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target);
                if (entry != null) sheets.Add(new Sheet(name, ReadCells(entry, shared)));
            }
            return sheets;
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or FormatException or OverflowException)
        {
            throw new ArgumentException("The file is not an Excel workbook (.xlsx) that can be read.", ex);
        }
    }

    /// <summary>"B5" as column 2; "AA3" as 27.</summary>
    public static int ColumnOf(string cellReference)
    {
        var column = 0;
        foreach (var ch in cellReference)
        {
            if (!char.IsAsciiLetter(ch)) break;
            column = column * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
        return column;
    }

    private static int? RowOf(string? cellReference) =>
        cellReference == null ? null : int.TryParse(new string(cellReference.SkipWhile(char.IsAsciiLetter).ToArray()), out var row) ? row : null;

    private static XmlReader Open(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxPartBytes) throw new InvalidDataException($"{entry.FullName} is too large.");
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = false };
        return XmlReader.Create(new CappedStream(entry.Open(), MaxPartBytes), settings);
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var strings = new List<string>();
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry == null) return strings;
        using var xml = Open(entry);
        while (xml.Read())
        {
            if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "si") strings.Add(ReadText(xml));
        }
        return strings;
    }

    /// <summary>
    /// The text of the si or is element the reader is on: every t inside it, except phonetic runs (rPh). The reader
    /// is left on the element's end, so the caller's next Read moves on to what follows it.
    /// </summary>
    private static string ReadText(XmlReader element)
    {
        var text = new System.Text.StringBuilder();
        using var sub = element.ReadSubtree();
        sub.Read();
        while (!sub.EOF)
        {
            // Skip and ReadElementContentAsString both move past the element
            if (sub.NodeType == XmlNodeType.Element && sub.LocalName == "rPh") sub.Skip();
            else if (sub.NodeType == XmlNodeType.Element && sub.LocalName == "t") text.Append(sub.ReadElementContentAsString());
            else sub.Read();
        }
        return text.ToString();
    }

    private static Dictionary<string, string> ReadRelationships(ZipArchive zip)
    {
        var targets = new Dictionary<string, string>();
        var entry = zip.GetEntry("xl/_rels/workbook.xml.rels") ?? throw new InvalidDataException("No workbook relationships.");
        using var xml = Open(entry);
        while (xml.Read())
        {
            if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "Relationship" && xml.GetAttribute("Id") is { } id && xml.GetAttribute("Target") is { } target)
                targets[id] = target;
        }
        return targets;
    }

    private static List<(string Name, string RelationshipId)> ReadSheetList(ZipArchive zip)
    {
        var sheets = new List<(string, string)>();
        var entry = zip.GetEntry("xl/workbook.xml") ?? throw new InvalidDataException("No workbook.");
        using var xml = Open(entry);
        while (xml.Read())
        {
            if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "sheet" && xml.GetAttribute("name") is { } name && xml.GetAttribute("id", RelationshipNs) is { } id)
                sheets.Add((name, id));
        }
        return sheets;
    }

    private static IReadOnlyDictionary<int, IReadOnlyDictionary<int, string>> ReadCells(ZipArchiveEntry entry, List<string> shared)
    {
        var rows = new Dictionary<int, IReadOnlyDictionary<int, string>>();
        Dictionary<int, string>? cells = null;
        int row = 0, column = 0;
        using var xml = Open(entry);
        while (xml.Read())
        {
            if (xml.NodeType != XmlNodeType.Element) continue;
            if (xml.LocalName == "row")
            {
                row = int.TryParse(xml.GetAttribute("r"), out var r) ? r : row + 1;
                column = 0;
                cells = new Dictionary<int, string>();
                rows[row] = cells;
            }
            else if (xml.LocalName == "c" && cells != null)
            {
                var reference = xml.GetAttribute("r");
                column = reference != null ? ColumnOf(reference) : column + 1;
                if (RowOf(reference) is { } cellRow && cellRow != row) continue;
                var type = xml.GetAttribute("t");
                var value = ReadCellValue(xml, type, shared);
                if (!string.IsNullOrWhiteSpace(value)) cells[column] = value.Trim();
            }
        }
        return rows;
    }

    /// <summary>The value of the c element the reader is on; the reader is left on the element's end.</summary>
    private static string ReadCellValue(XmlReader cell, string? type, List<string> shared)
    {
        var value = string.Empty;
        using (var sub = cell.ReadSubtree())
        {
            sub.Read();
            while (!sub.EOF)
            {
                if (sub.NodeType == XmlNodeType.Element && sub.LocalName == "v") value = sub.ReadElementContentAsString();
                else if (sub.NodeType == XmlNodeType.Element && sub.LocalName == "is" && type == "inlineStr") { value = ReadText(sub); sub.Read(); }
                else sub.Read();
            }
        }
        if (type == "s") return int.TryParse(value, out var index) && index >= 0 && index < shared.Count ? shared[index] : string.Empty;
        return value;
    }

    /// <summary>Stops reading past a size limit, whatever size the zip claims for the part.</summary>
    private sealed class CappedStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _limit;
        private long _read;

        public CappedStream(Stream inner, long limit) { _inner = inner; _limit = limit; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = _inner.Read(buffer, offset, count);
            _read += n;
            if (_read > _limit) throw new InvalidDataException("A part of the workbook is too large.");
            return n;
        }

        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
