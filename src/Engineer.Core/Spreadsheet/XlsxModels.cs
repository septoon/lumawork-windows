namespace Engineer.Core.Spreadsheet;

public sealed record XlsxCell(string Reference, string Text, string? Formula = null, int StyleIndex = 0, bool HasCachedValue = true);
public sealed record XlsxSheet(string Name, IReadOnlyDictionary<string, XlsxCell> Cells)
{
    public IReadOnlyList<string> MergedRanges { get; init; } = [];
    public string Text(string reference)
    {
        reference=reference.ToUpperInvariant();
        if(Cells.TryGetValue(reference,out var cell))return cell.Text;
        static (int Col,int Row) Coordinates(string r)
        {
            var column=0;var split=0;
            while(split<r.Length&&char.IsAsciiLetterUpper(r[split])){column=column*26+r[split]-'A'+1;split++;}
            return(column,int.TryParse(r[split..],out var row)?row:0);
        }
        var at=Coordinates(reference);
        foreach(var range in MergedRanges)
        {
            var parts=range.Split(':');if(parts.Length!=2)continue;
            var start=Coordinates(parts[0]);var end=Coordinates(parts[1]);
            if(at.Col>=start.Col&&at.Col<=end.Col&&at.Row>=start.Row&&at.Row<=end.Row&&Cells.TryGetValue(parts[0],out var anchor))return anchor.Text;
        }
        return "";
    }
}
public sealed record XlsxWorkbook(IReadOnlyList<XlsxSheet> Sheets, IReadOnlyDictionary<string, string> CustomProperties, string SourceHash, bool Date1904 = false)
{
    public XlsxSheet? Sheet(string name) => Sheets.FirstOrDefault(s => Normalize(s.Name) == Normalize(name));
    private static string Normalize(string value) => value.Trim().Replace('ё', 'е').ToUpperInvariant();
}
