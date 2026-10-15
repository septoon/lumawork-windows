using Engineer.Core.Gsm;
using Engineer.Core.Spreadsheet;
using System.Text.Json.Nodes;
using Xunit;

namespace Engineer.Core.Tests;
public sealed class GsmImportTests
{
    [Fact]
    public void ImportsJuneStyleWithoutCommaBeforePhoneAndWithoutSignaturePrefix()
    {
        var book=Sample();var preview=GsmProfileImporter.Parse(book);
        Assert.Equal("2026-06",preview.SourceMonth);
        var draft=preview.Merge(new JsonObject { ["vehicleId"]="vehicle-A",["posProjectId"]="project-A",["reportStartMonth"]="2026-04" });
        Assert.Equal("Инженер",GsmValues.Text(draft,"employeeJobTitle")); Assert.Equal("АО Тест",GsmValues.Text(draft,"employeeCompany"));
        Assert.Equal("Город, ул. Тестовая, 12",GsmValues.Text(draft,"employeeAddress")); Assert.Equal("+7(999)000-00-00",GsmValues.Text(draft,"employeePhone"));
        Assert.Equal("0012345",GsmValues.Text(draft,"driverLicenseNumber"));
        Assert.Equal("Иванов Иван Иванович",GsmValues.Text(draft,"employeeReportSignName"));
        Assert.Equal("vehicle-A",GsmValues.Text(draft,"vehicleId")); Assert.Equal("project-A",GsmValues.Text(draft,"posProjectId"));
        Assert.Equal("2026-04",GsmValues.Text(draft,"reportStartMonth")); Assert.Null(draft["defaultStartOdometer"]);
        Assert.Equal("АИ-95",draft["fuelTypes"]![0]!.GetValue<string>());
        Assert.Equal("10000",preview.SourceStartOdometer); Assert.Equal("10100",preview.SourceEndOdometer);
    }
    [Fact]
    public void ConflictingCardNumbersAreNotAppliedSilently()
    {
        var b=Sample();var fuel=b.Sheets[2];var cells=new Dictionary<string,XlsxCell>(fuel.Cells){["D3"]=new("D3","other-card")};
        b=b with { Sheets=[b.Sheets[0],b.Sheets[1],fuel with { Cells=cells }] };
        var preview=GsmProfileImporter.Parse(b);
        Assert.True(preview.Fields.Single(f=>f.Key=="fuelCardNumber").Conflict);
        Assert.Equal("existing-card",GsmValues.Text(preview.Merge(new JsonObject{["fuelCardNumber"]="existing-card"}),"fuelCardNumber"));
    }
    [Fact]
    public void FindsCompactedSignaturesAndFuelMetadata()
    {
        var b=Sample();var fuel=b.Sheets[2];var cells=new Dictionary<string,XlsxCell>(fuel.Cells){["A8"]=new("A8","ФИО Иванов И. И.")};
        b=b with { Sheets=[b.Sheets[0],b.Sheets[1],fuel with { Cells=cells }], CustomProperties=new Dictionary<string,string>{["LumaWorkGsmFuelTypes"]="[\"ДТ\",\"АИ-95\"]"} };
        var result=GsmProfileImporter.Parse(b);
        Assert.Equal("Иванов И. И.",GsmValues.Text(result.Merge(null),"employeeReportSignName"));
        Assert.Equal(2,result.FuelTypes.Count); Assert.Contains("Отчет по ТК!A8",result.Fields.Single(f=>f.Key=="employeeReportSignName").Sources);
    }
    [Fact]
    public void RejectsRequestWorkbookAsProfile()
    {
        Assert.Throws<InvalidDataException>(()=>GsmProfileImporter.Parse(new([new("Sheet1",new Dictionary<string,XlsxCell>())],new Dictionary<string,string>(),"synthetic")));
    }
    private static XlsxWorkbook Sample()
    {
        static XlsxSheet Sheet(string name,Dictionary<string,string> cells)=>new(name,cells.ToDictionary(p=>p.Key,p=>new XlsxCell(p.Key,p.Value)));
        return new([Sheet("ПЛ-ТК",new(){["M3"]="июнь",["Q3"]="2026",["C44"]="Иванов Иван Иванович",["C47"]="Тестов Пётр Петрович",["E5"]="Иванов Иван Иванович, Инженер, АО Тест",["E6"]="Город, ул. Тестовая, 12 +7(999)000-00-00",["AC5"]="0012345",["AC6"]="00042",["J7"]="Тест авто",["AE7"]="А000АА00",["N8"]="10000",["AE8"]="10100"}),Sheet("СЗ-ТК",new(){["C29"]="Сотрудник",["Q29"]="Иванов И.И."}),Sheet("Отчет по ТК ",new(){["A3"]="Иванов Иван Иванович",["B3"]="Тест авто",["C3"]="9,4",["D3"]="00042",["B6"]="АИ-95",["A8"]="Иванов Иван Иванович"})],new Dictionary<string,string>(),"synthetic-hash");
    }
}
