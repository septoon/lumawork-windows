using System.IO.Compression;
using System.Text;
using Engineer.Core.Spreadsheet;
using Xunit;

namespace Engineer.Core.Tests;

public sealed class XlsxReaderTests
{
    [Fact]
    public void UsesWorkbookRelationshipsSharedStringsAndDirectReferences()
    {
        using var input = Book("""<row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="inlineStr"><is><t>  Русский текст  </t></is></c><c r="C1"><f>A1</f></c><c r="D1"><f>SUM(1,2)</f></c><c r="E1" s="1"><v>123</v></c></row>""");
        var result = XlsxReader.Read(input, "fixture.xlsx");
        var sheet = Assert.Single(result.Sheets);
        Assert.Equal("ПЛ-ТК", sheet.Name);
        Assert.Equal("ФИО", sheet.Text("A1")); Assert.Equal("Русский текст", sheet.Text("B1"));
        Assert.Equal("ФИО", sheet.Text("C1")); Assert.False(sheet.Cells["D1"].HasCachedValue);
        Assert.Equal("000123", sheet.Text("E1")); Assert.Equal(64, result.SourceHash.Length);
    }
    [Fact]
    public void ReadsBeyondIncorrectDimensionAndHonorsMergedAnchor()
    {
        using var input = Book("""<row r="1"><c r="A1" t="inlineStr"><is><t>Адрес</t></is></c><c r="H1"><v>44</v></c></row><row r="10"><c r="B10"><v>99</v></c></row>""", "<mergeCells><mergeCell ref=\"A1:D1\"/></mergeCells>");
        var sheet = XlsxReader.Read(input,"fixture.xlsx").Sheets[0];
        Assert.Equal("44",sheet.Text("H1")); Assert.Equal("99",sheet.Text("B10")); Assert.Equal("Адрес",sheet.Text("C1"));
    }
    [Theory]
    [InlineData("../escape.xml")]
    [InlineData("/absolute.xml")]
    public void RejectsUnsafeArchiveNames(string name)
    {
        using var input = Book("", extraEntry:name);
        Assert.Throws<InvalidDataException>(()=>XlsxReader.Read(input,"fixture.xlsx"));
    }
    [Fact]
    public void AcceptsPackageRootRelativeRelationships()
    {
        using var input=Book("<row><c r=\"A1\"><v>1</v></c></row>",rootRelative:true);
        Assert.Equal("1",XlsxReader.Read(input,"fixture.xlsx").Sheets[0].Text("A1"));
    }
    [Fact]
    public void RejectsXmlEntities()
    {
        using var input = Book("", sheetOverride:"<!DOCTYPE worksheet [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><worksheet><x>&x;</x></worksheet>");
        Assert.Throws<InvalidDataException>(()=>XlsxReader.Read(input,"fixture.xlsx"));
    }
    [Fact]
    public void LimitsAreEnforcedBeforeReadingCells()
    {
        using var input=Book("<row><c r=\"A1\"><v>1</v></c><c r=\"B1\"><v>2</v></c></row>");
        Assert.Throws<InvalidDataException>(()=>XlsxReader.Read(input,"fixture.xlsx",default,new XlsxLimits(MaxCells:1)));
    }
    [Fact]
    public void CancellationIsObserved()
    {
        using var input=Book(""); using var cts=new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(()=>XlsxReader.Read(input,"fixture.xlsx",cts.Token));
    }
    internal static MemoryStream Book(string rows,string suffix="",string? extraEntry=null,string? sheetOverride=null,bool rootRelative=false)
    {
        var stream=new MemoryStream();
        using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true))
        {
            void Entry(string name,string text){ using var writer=new StreamWriter(zip.CreateEntry(name).Open(),Encoding.UTF8);writer.Write(text); }
            Entry("xl/workbook.xml","""<workbook xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="ПЛ-ТК" sheetId="9" r:id="rId9"/></sheets></workbook>""");
            Entry("xl/_rels/workbook.xml.rels", """<Relationships><Relationship Id="rId9" Target="worksheets/custom.xml" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"/></Relationships>""".Replace("Target=\"worksheets/",rootRelative?"Target=\"/xl/worksheets/":"Target=\"worksheets/"));
            Entry("xl/worksheets/custom.xml",sheetOverride??$"<worksheet><dimension ref=\"A1\"/><sheetData>{rows}</sheetData>{suffix}</worksheet>");
            Entry("xl/sharedStrings.xml","<sst><si><r><t>ФИ</t></r><r><t>О</t></r></si></sst>");
            Entry("xl/styles.xml","<styleSheet><numFmts><numFmt numFmtId=\"170\" formatCode=\"000000\"/></numFmts><cellXfs><xf numFmtId=\"0\"/><xf numFmtId=\"170\"/></cellXfs></styleSheet>");
            if(extraEntry!=null)Entry(extraEntry,"bad");
        }
        stream.Position=0;return stream;
    }
}
