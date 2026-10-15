using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Engineer.Core.Spreadsheet;

public sealed record XlsxLimits(int MaxCompressedBytes=6*1024*1024,long MaxUnpackedBytes=64*1024*1024,int MaxEntryBytes=16*1024*1024,int MaxEntries=512,int MaxSheets=32,int MaxCells=50000)
{
    public static XlsxLimits ClosedRequests { get; } = new(MaxCompressedBytes:24*1024*1024,MaxUnpackedBytes:128*1024*1024,MaxEntryBytes:32*1024*1024,MaxCells:250000);
}

public static class XlsxReader
{
    public static XlsxWorkbook Read(Stream stream,string fileName,CancellationToken cancellationToken=default,XlsxLimits? limits=null)
    {
        limits ??= new();
        if (!string.Equals(Path.GetExtension(fileName),".xlsx",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Выберите файл XLSX. Старый XLS не поддерживается.");
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var bytes=ReadBounded(stream,limits.MaxCompressedBytes,cancellationToken);
            using var memory=new MemoryStream(bytes,false);
            using var zip=new ZipArchive(memory,ZipArchiveMode.Read);
            if(zip.Entries.Count>limits.MaxEntries) throw Invalid("слишком много частей");
            var entries=new Dictionary<string,ZipArchiveEntry>(StringComparer.Ordinal);
            long total=0;
            foreach(var entry in zip.Entries)
            {
                var path=entry.FullName;
                if(path.StartsWith('/')||path.Contains('\\')||path.Split('/').Any(p=>p is ".." or ".")||!entries.TryAdd(path,entry)) throw Invalid("некорректная структура ZIP");
                total=checked(total+entry.Length);
                if(total>limits.MaxUnpackedBytes||entry.Length>limits.MaxEntryBytes) throw Invalid("превышен допустимый размер распакованного файла");
            }
            XDocument Xml(string path)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if(!entries.TryGetValue(path,out var entry)) throw Invalid("отсутствует необходимая часть книги");
                using var data=entry.Open(); using var bounded=new MemoryStream(ReadBounded(data,limits.MaxEntryBytes,cancellationToken));
                using var reader=XmlReader.Create(bounded,new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=limits.MaxEntryBytes });
                return XDocument.Load(reader);
            }
            var workbook=Xml("xl/workbook.xml");
            var relationships=Xml("xl/_rels/workbook.xml.rels").Descendants().Where(e=>e.Name.LocalName=="Relationship").ToDictionary(e=>Attr(e,"Id"),e=>e);
            var shared=entries.ContainsKey("xl/sharedStrings.xml") ? Xml("xl/sharedStrings.xml").Descendants().Where(e=>e.Name.LocalName=="si").Select(e=>string.Concat(e.Descendants().Where(t=>t.Name.LocalName=="t").Select(t=>t.Value))).ToArray() : [];
            var formats=new Dictionary<int,string>();var styles=new List<int>();
            if(entries.ContainsKey("xl/styles.xml"))
            {
                var styleXml=Xml("xl/styles.xml");
                foreach(var f in styleXml.Descendants().Where(e=>e.Name.LocalName=="numFmt")) if(int.TryParse(Attr(f,"numFmtId"),out var id)) formats[id]=Attr(f,"formatCode");
                styles=styleXml.Descendants().Where(e=>e.Name.LocalName=="cellXfs").Elements().Select(e=>int.TryParse(Attr(e,"numFmtId"),out var i)?i:0).ToList();
            }
            var sheetNodes=workbook.Descendants().Where(e=>e.Name.LocalName=="sheet").ToArray();
            if(sheetNodes.Length is 0||sheetNodes.Length>limits.MaxSheets) throw Invalid("некорректное число листов");
            var sheets=new List<XlsxSheet>();var count=0;
            foreach(var node in sheetNodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relId=node.Attributes().FirstOrDefault(a=>a.Name.LocalName=="id")?.Value;
                if(relId==null||!relationships.TryGetValue(relId,out var rel)||Attr(rel,"TargetMode")=="External") throw Invalid("лист ссылается за пределы книги");
                var target=Attr(rel,"Target");
                if(target.Contains('\\')||target.Contains('#')||target.Contains('?')||target.StartsWith("//")||(!target.StartsWith('/')&&Uri.TryCreate(target,UriKind.Absolute,out _))) throw Invalid("некорректный путь листа");
                var resolved=new Uri(new Uri("https://xlsx.invalid/xl/workbook.xml"),target).AbsolutePath.TrimStart('/');
                var xml=Xml(Uri.UnescapeDataString(resolved));
                var cells=new Dictionary<string,XlsxCell>(StringComparer.OrdinalIgnoreCase);
                foreach(var c in xml.Descendants().Where(e=>e.Name.LocalName=="c"))
                {
                    if(++count>limits.MaxCells) throw Invalid("слишком много ячеек");
                    if(count%256==0)cancellationToken.ThrowIfCancellationRequested();
                    var reference=Attr(c,"r").ToUpperInvariant();
                    if(!Regex.IsMatch(reference,"^[A-Z]{1,3}[1-9][0-9]{0,6}$")) throw Invalid("некорректный адрес ячейки");
                    var type=Attr(c,"t");var raw=c.Elements().FirstOrDefault(e=>e.Name.LocalName=="v")?.Value;
                    var formula=c.Elements().FirstOrDefault(e=>e.Name.LocalName=="f")?.Value;
                    var style=int.TryParse(Attr(c,"s"),out var index)?index:0;
                    var text=type switch
                    {
                        "s" => int.TryParse(raw,out var n)&&n>=0&&n<shared.Length ? shared[n] : throw Invalid("повреждён словарь строк"),
                        "inlineStr" => string.Concat(c.Descendants().Where(e=>e.Name.LocalName=="t").Select(e=>e.Value)),
                        "b" => raw=="1"?"true":"false",
                        "e" => "",
                        _ => raw ?? ""
                    };
                    if(type is "" or "n" && decimal.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var number))
                    {
                        var format=style>=0&&style<styles.Count?formats.GetValueOrDefault(styles[style]):null;
                        if(format!=null&&Regex.IsMatch(format,"^0{1,32}$")&&number>=0&&decimal.Truncate(number)==number)
                            text=number.ToString("0",CultureInfo.InvariantCulture).PadLeft(format.Length,'0');
                        else text=number.ToString(CultureInfo.InvariantCulture);
                    }
                    if(!cells.TryAdd(reference,new(reference,text.Trim(),formula,style,formula==null||!string.IsNullOrEmpty(raw)))) throw Invalid("повторяющаяся ячейка");
                }
                var merges=xml.Descendants().Where(e=>e.Name.LocalName=="mergeCell").Select(e=>Attr(e,"ref")).ToArray();
                sheets.Add(new(Attr(node,"name"),cells) { MergedRanges=merges });
            }
            string Resolve(XlsxSheet sheet,string reference,HashSet<string> path)
            {
                if(!sheet.Cells.TryGetValue(reference,out var cell))return "";
                if(cell.HasCachedValue)return cell.Text;
                if(cell.Formula==null||path.Count>=16||!path.Add(sheet.Name+"!"+reference))return "";
                var match=Regex.Match(cell.Formula.Trim(),"^=?((?:'(?<quoted>(?:[^']|'')+)'|(?<sheet>[^'!]+))!)?\\$?(?<col>[A-Za-z]{1,3})\\$?(?<row>[1-9][0-9]{0,6})$");
                if(!match.Success)return "";
                var sourceName=match.Groups["quoted"].Success?match.Groups["quoted"].Value.Replace("''","'"):match.Groups["sheet"].Success?match.Groups["sheet"].Value:sheet.Name;
                var source=sheets.FirstOrDefault(s=>s.Name==sourceName);
                return source==null?"":Resolve(source,(match.Groups["col"].Value+match.Groups["row"].Value).ToUpperInvariant(),path);
            }
            foreach(var sheet in sheets)
            {
                var cells=(Dictionary<string,XlsxCell>)sheet.Cells;
                foreach(var (key,value) in cells.ToArray())
                    if(!value.HasCachedValue)
                    {
                        var resolved=Resolve(sheet,key,[]);
                        if(resolved.Length>0) cells[key]=value with { Text=resolved,HasCachedValue=true };
                    }
            }
            var properties=new Dictionary<string,string>();
            if(entries.ContainsKey("docProps/custom.xml"))
                foreach(var property in Xml("docProps/custom.xml").Descendants().Where(e=>e.Name.LocalName=="property")) properties[Attr(property,"name")]=property.Elements().FirstOrDefault()?.Value??"";
            var date1904=workbook.Descendants().Any(e=>e.Name.LocalName=="workbookPr"&&Attr(e,"date1904") is "1" or "true");
            return new(sheets,properties,Convert.ToHexString(SHA256.HashData(bytes)),date1904);
        }
        catch(XmlException e){throw new InvalidDataException("XLSX содержит повреждённый или запрещённый XML.",e);}
        catch(OverflowException e){throw new InvalidDataException("Превышен допустимый размер XLSX.",e);}
    }
    private static string Attr(XElement e,string name)=>e.Attribute(name)?.Value??"";
    private static InvalidDataException Invalid(string reason)=>new("Не удалось прочитать XLSX: "+reason+".");
    private static byte[] ReadBounded(Stream stream,int maximum,CancellationToken ct)
    {
        using var result=new MemoryStream();var buffer=new byte[81920];int read;
        while((read=stream.Read(buffer,0,buffer.Length))>0){ct.ThrowIfCancellationRequested();if(result.Length+read>maximum)throw Invalid("превышен допустимый размер файла");result.Write(buffer,0,read);}
        return result.ToArray();
    }
}
