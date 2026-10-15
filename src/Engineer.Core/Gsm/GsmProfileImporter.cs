using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Engineer.Core.Spreadsheet;

namespace Engineer.Core.Gsm;

public sealed record GsmImportField(string Key,string Value,IReadOnlyList<string> Sources,bool Conflict=false,bool Derived=false);
public sealed record GsmImportPreview(string SourceHash,string? SourceMonth,string SourceStartOdometer,string SourceEndOdometer,
    IReadOnlyList<GsmImportField> Fields,IReadOnlyList<string> FuelTypes,IReadOnlyList<string> BudgetCodes,IReadOnlyList<string> Warnings)
{
    public JsonObject Merge(JsonObject? existing)
    {
        var result=existing==null?new JsonObject():(JsonObject)existing.DeepClone();
        foreach(var field in Fields.Where(f=>!f.Conflict&&!string.IsNullOrWhiteSpace(f.Value)))
            result[field.Key]=field.Key=="fuelNorm"&&double.TryParse(field.Value.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out var n)?JsonValue.Create(n):JsonValue.Create(field.Value);
        if(FuelTypes.Count>0) { result["fuelTypes"]=new JsonArray(FuelTypes.Select(t=>(JsonNode?)JsonValue.Create(t)).ToArray());result["fuelType"]=FuelTypes[0]; }
        return result;
    }
}
public static class GsmProfileImporter
{
    private static readonly string[] KnownFuels=["АИ-92","АИ-92 Фирменное","АИ-95","АИ-95 Фирменное","АИ-98","АИ-100","ДТ","ДТ Зимнее"];
    public static GsmImportPreview Parse(XlsxWorkbook workbook)
    {
        var pl=workbook.Sheet("ПЛ-ТК");var report=workbook.Sheet("Отчет по ТК");var sz=workbook.Sheet("СЗ-ТК");
        if(pl==null||report==null)throw new InvalidDataException("Это не готовый ГСМ-отчёт: нужны листы «ПЛ-ТК» и «Отчет по ТК».");
        var found=new Dictionary<string,List<(string Value,string Source,bool Derived)>>();var warnings=new List<string>();
        void Add(string key,string value,string source,bool derived=false)
        { if(string.IsNullOrWhiteSpace(value))return; if(!found.TryGetValue(key,out var list))found[key]=list=[];list.Add((value.Trim(),source,derived)); }
        void Cell(string key,XlsxSheet sheet,string reference)
        {
            var text=sheet.Text(reference);
            if(sheet.Cells.TryGetValue(reference,out var cell)&&!cell.HasCachedValue)warnings.Add($"{sheet.Name.Trim()}!{reference}: формула не рассчитана; заполните поле вручную.");
            Add(key,text,sheet.Name.Trim()+"!"+reference);
        }
        Cell("employeeFullName",pl,"C44");Cell("employeeFullName",report,"A3");
        var compound=pl.Text("E5");var parts=compound.Split(',').Select(x=>x.Trim()).ToArray();
        if(parts.Length>=3)
        {
            Add("employeeFullName",parts[0],"ПЛ-ТК!E5");
            if(parts.Length==3){Add("employeeJobTitle",parts[1],"ПЛ-ТК!E5");Add("employeeCompany",parts[2],"ПЛ-ТК!E5");}
            else warnings.Add("Должность и компания в E5 составные: проверьте и разделите их вручную.");
        }
        Add("employeeTitleCompany",compound,"ПЛ-ТК!E5");
        var contact=pl.Text("E6");Add("employeeAddressPhone",contact,"ПЛ-ТК!E6");
        var phone=Regex.Match(contact,@"^(?<address>.+?)\s*,?\s*(?<phone>(?:\+7|8)[0-9()\s-]{9,24})$");
        if(phone.Success)
        {
            var digits=Regex.Replace(phone.Groups["phone"].Value,"[^0-9]","");
            if(digits.Length==11)
            {
                Add("employeeAddress",phone.Groups["address"].Value.TrimEnd(' ',','),"ПЛ-ТК!E6");
                Add("employeePhone",$"+7({digits[1..4]}){digits[4..7]}-{digits[7..9]}-{digits[9..]}","ПЛ-ТК!E6");
            }
        }
        else warnings.Add("Адрес и телефон в E6 требуют проверки вручную.");
        Cell("driverLicenseNumber",pl,"AC5");Cell("fuelCardNumber",pl,"AC6");Cell("fuelCardNumber",report,"D3");
        Cell("carModel",pl,"J7");Cell("carModel",report,"B3");Cell("licensePlate",pl,"AE7");Cell("fuelNorm",report,"C3");
        Cell("authorizedFullName",pl,"C47");
        static bool ShortName(string text)=>Regex.IsMatch(text,@"^[А-ЯЁA-Z][А-ЯЁа-яёA-Za-z-]+\s+[А-ЯЁA-Z]\.\s*[А-ЯЁA-Z]\.?$");
        if(sz!=null)
        {
            foreach(var label in sz.Cells.Values.Where(c=>c.Text.Trim()=="Сотрудник"))
            {
                var row=Regex.Match(label.Reference,"[0-9]+$").Value;
                foreach(var c in sz.Cells.Values.Where(c=>Regex.Match(c.Reference,"[0-9]+$").Value==row&&ShortName(c.Text)))Add("employeeShortName",c.Text,sz.Name.Trim()+"!"+c.Reference);
            }
            foreach(var label in sz.Cells.Values.Where(c=>c.Text.Trim()=="Куратор"))
            {
                var row=int.Parse(Regex.Match(label.Reference,"[0-9]+$").Value,CultureInfo.InvariantCulture);
                foreach(var c in sz.Cells.Values.Where(c=>int.Parse(Regex.Match(c.Reference,"[0-9]+$").Value,CultureInfo.InvariantCulture) is var r&&r>=row&&r<=row+1&&ShortName(c.Text))) Add("authorizedShortName",c.Text,sz.Name.Trim()+"!"+c.Reference);
            }
        }
        var fullName=found.GetValueOrDefault("employeeFullName")?.FirstOrDefault().Value;
        foreach(var c in report.Cells.Values.Where(c=>Regex.IsMatch(c.Reference,"^A[0-9]+$")&&int.Parse(c.Reference[1..],CultureInfo.InvariantCulture)>3))
        {
            var value=Regex.Replace(c.Text,@"^ФИО\s*:?\s*","",RegexOptions.IgnoreCase).Trim();
            if(ShortName(value)||value==fullName)Add("employeeReportSignName",value,report.Name.Trim()+"!"+c.Reference);
        }
        void DerivedInitials(string fullKey,string shortKey)
        {
            if(found.ContainsKey(shortKey)||!found.TryGetValue(fullKey,out var source)||source.Select(x=>x.Value).Distinct().Count()!=1)return;
            var names=source[0].Value.Split(' ',StringSplitOptions.RemoveEmptyEntries);
            if(names.Length==3)Add(shortKey,$"{names[0]} {names[1][0]}.{names[2][0]}.",source[0].Source+" → инициалы",true);
        }
        DerivedInitials("employeeFullName","employeeShortName");DerivedInitials("authorizedFullName","authorizedShortName");
        if(!found.ContainsKey("employeeReportSignName")&&found.TryGetValue("employeeShortName",out var shortNames)&&shortNames.Count>0)Add("employeeReportSignName",shortNames[0].Value,shortNames[0].Source+" → подпись",true);
        var fuels=new List<string>();
        if(workbook.CustomProperties.TryGetValue("LumaWorkGsmFuelTypes",out var json))
        {
            try{fuels=JsonSerializer.Deserialize<string[]>(json)?.Where(KnownFuels.Contains).Distinct().ToList()??[];}
            catch(JsonException){warnings.Add("Метаданные видов топлива повреждены; выберите типы вручную.");}
        }
        if(fuels.Count==0)fuels=report.Cells.Values.Where(c=>KnownFuels.Contains(c.Text)).Select(c=>c.Text).Distinct().ToList();
        string? month=null;
        var months=new[]{"январ","феврал","март","апрел","ма","июн","июл","август","сентябр","октябр","ноябр","декабр"};
        var monthText=pl.Text("M3").ToLowerInvariant();var index=Array.FindIndex(months,m=>monthText.StartsWith(m,StringComparison.Ordinal));
        if(index>=0&&int.TryParse(pl.Text("Q3"),out var year)&&year>=2000&&year<=2100)month=$"{year:D4}-{index+1:D2}";
        if(month==null)warnings.Add("Месяц исходного файла не распознан. Выберите целевой месяц отдельно.");
        var fields=found.Select(p=>new GsmImportField(p.Key,p.Value[0].Value,p.Value.Select(v=>v.Source).Distinct().ToArray(),
            p.Value.Select(v=>Regex.Replace(v.Value,"\\s+"," ").ToUpperInvariant()).Distinct().Count()>1,p.Value.All(v=>v.Derived))).ToArray();
        foreach(var field in fields.Where(f=>f.Conflict))warnings.Add($"Несовпадение поля {field.Key}: "+string.Join("; ",found[field.Key].Select(c=>c.Source+" = "+c.Value)));
        foreach(var field in fields.Where(f=>f.Derived))warnings.Add($"Поле {field.Key} получено из полного ФИО. Проверьте перед сохранением.");
        var budgetCodes=Enumerable.Range(11,31).Select(r=>pl.Text("D"+r)).Where(t=>t.Length>0&&!t.StartsWith('=')).Distinct().ToArray();
        return new(workbook.SourceHash,month,pl.Text("N8"),pl.Text("AE8"),fields,fuels,budgetCodes,warnings);
    }
}
