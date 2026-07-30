using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ExcelDataReader;
using HRDashboard.Models;
using Microsoft.VisualBasic.FileIO;

namespace HRDashboard.Services;

public sealed class EmployeeCsvService
{
    public static readonly string[] Headers=["사업장","상위부서","부서","사번","성명","직위","근무조","직책","직군","사원구분","성별","생년월일","입사일자","퇴사일자","책정연봉","월임금","최종학력","학교명","전공"];
    private static readonly string[] RequiredHeaders=["사번"];
    public byte[] Export(IReadOnlyCollection<Employee> employees)
    {
        var csv=new StringBuilder(); csv.AppendLine(string.Join(',',Headers.Select(Escape)));
        foreach(var x in employees) { string[] values=[x.Workplace??"",x.ParentDepartment??"",x.Department??"",x.EmployeeNumber,x.Name??"",x.Position??"",x.WorkShift??"",x.Duty??"",x.JobGroup??"",x.EmploymentType??"",x.Gender??"",Date(x.BirthDate),Date(x.HireDate),Date(x.TerminationDate),Amount(x.AnnualSalary),Amount(x.MonthlyWage),x.Education??"",x.SchoolName??"",x.Major??""]; csv.AppendLine(string.Join(',',values.Select(Escape))); }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }
    public byte[] ExportExcel(IReadOnlyCollection<Employee> employees,IReadOnlyDictionary<string,string>? displayNames=null)
    {
        using var output=new MemoryStream();
        using(var archive=new ZipArchive(output,ZipArchiveMode.Create,true))
        {
            WriteXml(archive,"[Content_Types].xml",writer=>{writer.WriteStartElement("Types","http://schemas.openxmlformats.org/package/2006/content-types");writer.WriteStartElement("Default");writer.WriteAttributeString("Extension","rels");writer.WriteAttributeString("ContentType","application/vnd.openxmlformats-package.relationships+xml");writer.WriteEndElement();writer.WriteStartElement("Default");writer.WriteAttributeString("Extension","xml");writer.WriteAttributeString("ContentType","application/xml");writer.WriteEndElement();ContentType(writer,"/xl/workbook.xml","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");ContentType(writer,"/xl/worksheets/sheet1.xml","application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");ContentType(writer,"/xl/styles.xml","application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");writer.WriteEndElement();});
            WriteXml(archive,"_rels/.rels",writer=>{writer.WriteStartElement("Relationships","http://schemas.openxmlformats.org/package/2006/relationships");Relationship(writer,"rId1","http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument","xl/workbook.xml");writer.WriteEndElement();});
            WriteXml(archive,"xl/workbook.xml",writer=>{writer.WriteStartElement("workbook","http://schemas.openxmlformats.org/spreadsheetml/2006/main");writer.WriteAttributeString("xmlns","r",null,"http://schemas.openxmlformats.org/officeDocument/2006/relationships");writer.WriteStartElement("sheets");writer.WriteStartElement("sheet");writer.WriteAttributeString("name","사원 현황");writer.WriteAttributeString("sheetId","1");writer.WriteAttributeString("r","id","http://schemas.openxmlformats.org/officeDocument/2006/relationships","rId1");writer.WriteEndElement();writer.WriteEndElement();writer.WriteEndElement();});
            WriteXml(archive,"xl/_rels/workbook.xml.rels",writer=>{writer.WriteStartElement("Relationships","http://schemas.openxmlformats.org/package/2006/relationships");Relationship(writer,"rId1","http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet","worksheets/sheet1.xml");Relationship(writer,"rId2","http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles","styles.xml");writer.WriteEndElement();});
            WriteXml(archive,"xl/styles.xml",WriteStyles);
            WriteXml(archive,"xl/worksheets/sheet1.xml",writer=>WriteSheet(writer,employees,displayNames));
        }
        return output.ToArray();
    }
    public EmployeeImportResult Parse(Stream stream,IReadOnlyDictionary<string,string>? headerAliases=null) { using var p=new TextFieldParser(stream,Encoding.UTF8,true,true){TextFieldType=FieldType.Delimited,HasFieldsEnclosedInQuotes=true,TrimWhiteSpace=false};p.SetDelimiters(",");return Parse(p,"CSV",headerAliases); }
    public EmployeeImportResult ParseClipboard(string text,IReadOnlyDictionary<string,string>? headerAliases=null) { if(string.IsNullOrWhiteSpace(text))throw new EmployeeCsvException("붙여넣은 표가 비어 있습니다.");using var p=new TextFieldParser(new StringReader(text)){TextFieldType=FieldType.Delimited,HasFieldsEnclosedInQuotes=true,TrimWhiteSpace=false};p.SetDelimiters("\t");return Parse(p,"붙여넣은 표",headerAliases); }
    public EmployeeImportResult ParseExcel(Stream stream,IReadOnlyDictionary<string,string>? headerAliases=null)
    {
        try
        {
            using var archive=new ZipArchive(stream,ZipArchiveMode.Read,true);
            if(archive.Entries.Sum(x=>x.Length)>100*1024*1024)throw new EmployeeCsvException("압축 해제된 Excel 파일이 너무 큽니다.");
            const string spreadsheetNs="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace ns=spreadsheetNs;
            var sharedEntry=archive.GetEntry("xl/sharedStrings.xml");
            var shared=sharedEntry==null
                ?Array.Empty<string>()
                :LoadXml(sharedEntry).Descendants(ns+"si").Select(x=>string.Concat(x.Descendants(ns+"t").Select(t=>t.Value))).ToArray();
            var dateStyles=ReadExcelDateStyles(archive,ns);
            var sheetEntry=archive.Entries
                .Where(x=>x.FullName.StartsWith("xl/worksheets/sheet",StringComparison.OrdinalIgnoreCase)&&x.FullName.EndsWith(".xml",StringComparison.OrdinalIgnoreCase))
                .OrderBy(x=>x.FullName,StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault()??throw new EmployeeCsvException("Excel 파일에서 워크시트를 찾을 수 없습니다.");
            var rows=new List<string>();
            foreach(var row in LoadXml(sheetEntry).Descendants(ns+"row"))
            {
                var cells=new SortedDictionary<int,string>();
                foreach(var cell in row.Elements(ns+"c"))
                {
                    var reference=(string?)cell.Attribute("r")??"";
                    var column=ExcelColumnIndex(reference);
                    if(column<0||column>200)continue;
                    var type=(string?)cell.Attribute("t");
                    var value=type=="inlineStr"
                        ?string.Concat(cell.Descendants(ns+"t").Select(x=>x.Value))
                        :(string?)cell.Element(ns+"v")??"";
                    if(type=="s"&&int.TryParse(value,out var sharedIndex)&&sharedIndex>=0&&sharedIndex<shared.Length)value=shared[sharedIndex];
                    else if(type=="b")value=value=="1"?"TRUE":"FALSE";
                    else if(type is null&&int.TryParse((string?)cell.Attribute("s"),out var styleIndex)&&dateStyles.Contains(styleIndex)
                        &&double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var serial))
                        value=DateTime.FromOADate(serial).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                    cells[column]=value;
                }
                if(cells.Count==0)continue;
                var values=new string[cells.Keys.Max()+1];
                foreach(var (column,value) in cells)values[column]=value;
                rows.Add(string.Join('\t',values.Select(EscapeTab)));
            }
            if(rows.Count==0)throw new EmployeeCsvException("Excel 파일에 데이터가 없습니다.");
            return ParseClipboard(string.Join(Environment.NewLine,rows),headerAliases);
        }
        catch(EmployeeCsvException){throw;}
        catch(Exception e)when(e is InvalidDataException or XmlException or IOException or FormatException or ArgumentException)
        {
            throw new EmployeeCsvException($"Excel 파일을 읽을 수 없습니다: {e.Message}");
        }
    }
    public EmployeeImportResult ParseLegacyExcel(Stream stream,IReadOnlyDictionary<string,string>? headerAliases=null)
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            using var reader=ExcelReaderFactory.CreateBinaryReader(stream,new ExcelReaderConfiguration { FallbackEncoding=Encoding.GetEncoding(1252) });
            var rows=new List<string>();
            while(reader.Read())
            {
                var values=new string[reader.FieldCount];
                for(var column=0;column<reader.FieldCount;column++)
                {
                    var value=reader.GetValue(column);
                    values[column]=value switch
                    {
                        null=>"",
                        DateTime date=>date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
                        double number=>number.ToString("G17",CultureInfo.InvariantCulture),
                        float number=>number.ToString("G9",CultureInfo.InvariantCulture),
                        IFormattable formattable=>formattable.ToString(null,CultureInfo.InvariantCulture)??"",
                        _=>value.ToString()??""
                    };
                }
                if(values.Any(x=>!string.IsNullOrWhiteSpace(x)))rows.Add(string.Join('\t',values.Select(EscapeTab)));
            }
            if(rows.Count==0)throw new EmployeeCsvException("Excel 파일에 데이터가 없습니다.");
            return ParseClipboard(string.Join(Environment.NewLine,rows),headerAliases);
        }
        catch(EmployeeCsvException){throw;}
        catch(Exception e)when(e is InvalidDataException or IOException or FormatException or ArgumentException or NotSupportedException)
        {
            throw new EmployeeCsvException($"Excel 파일을 읽을 수 없습니다: {e.Message}");
        }
    }
    private static EmployeeImportResult Parse(TextFieldParser parser,string source,IReadOnlyDictionary<string,string>? headerAliases)
    {
        var headers=parser.ReadFields()??throw new EmployeeCsvException($"{source}가 비어 있습니다.");if(headers.Length>0)headers[0]=headers[0].TrimStart('\uFEFF');
        string Canonical(string value)=>headerAliases!=null&&headerAliases.TryGetValue(value.Trim(),out var canonical)?canonical:value.Trim();
        var mappedHeaders=headers.Select((v,i)=>(v:Canonical(v),i)).ToArray();
        var duplicateHeaders=mappedHeaders.GroupBy(x=>x.v,StringComparer.OrdinalIgnoreCase).Where(x=>x.Count()>1).Select(x=>x.Key).ToArray();
        if(duplicateHeaders.Length>0)throw new EmployeeCsvException($"머리글이 중복되었습니다: {string.Join(", ",duplicateHeaders)}");
        var map=mappedHeaders.ToDictionary(x=>x.v,x=>x.i,StringComparer.OrdinalIgnoreCase);
        var missing=RequiredHeaders.Where(x=>!map.ContainsKey(x)).ToArray();if(missing.Length>0)throw new EmployeeCsvException($"필수 머리글이 없습니다: {string.Join(", ",missing)}");
        var rows=new List<EmployeeImportRow>();var errors=new List<string>();var n=1;
        while(!parser.EndOfData){n++;try{var f=parser.ReadFields()??[];if(f.All(string.IsNullOrWhiteSpace))continue;string V(string h)=>map.TryGetValue(h,out var i)&&i<f.Length?f[i].Trim():"";string? O(string h,int max){var v=V(h);return v.Length is 0||v.Length>max?null:v;}var no=V("사번");if(no.Length==0)throw new EmployeeCsvException($"{n}행 [사번]: 값이 비어 있습니다.");if(no.Length>50)throw new EmployeeCsvException($"{n}행 [사번]: 50자를 초과했습니다.");rows.Add(new(no,O("사업장",100),O("상위부서",100),O("부서",100),O("성명",100),O("직위",50),O("근무조",50),O("직책",50),O("직군",50),O("사원구분",50),O("성별",20),ParseDate(V("생년월일")),ParseDate(V("입사일자")),ParseDate(V("퇴사일자")),ParseAmount(V("책정연봉")),ParseAmount(V("월임금")),O("최종학력",100),O("학교명",150),O("전공",100)));}catch(Exception e)when(e is EmployeeCsvException or MalformedLineException){errors.Add(e.Message);}}
        if(errors.Count>0)throw new EmployeeCsvException(string.Join("\n",errors.Take(20)));if(rows.Count==0)throw new EmployeeCsvException("반영할 사원 데이터가 없습니다.");var dup=rows.GroupBy(x=>x.EmployeeNumber,StringComparer.OrdinalIgnoreCase).Where(x=>x.Count()>1).Select(x=>x.Key).ToArray();if(dup.Length>0)throw new EmployeeCsvException($"사번이 중복되었습니다: {string.Join(", ",dup.Take(20))}");return new(rows,Headers.Where(map.ContainsKey).ToHashSet(StringComparer.OrdinalIgnoreCase));
    }
    private static DateTime? ParseDate(string v){if(v.Length==0)return null;string[] formats=["yyyy-MM-dd","yyyy.MM.dd","yyyy/MM/dd","yyyyMMdd","M/d/yyyy","MM/dd/yyyy"];return DateTime.TryParseExact(v,formats,CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)||DateTime.TryParse(v,CultureInfo.GetCultureInfo("ko-KR"),DateTimeStyles.None,out d)?d.Date:null;}
    private static long? ParseAmount(string v){if(v.Length==0)return null;v=v.Replace(",","").Replace("원","").Trim();return long.TryParse(v,NumberStyles.Integer,CultureInfo.InvariantCulture,out var amount)&&amount>=0?amount:null;}
    private static string Date(DateTime? d)=>d?.ToString("yyyy-MM-dd")??"";
    private static string Amount(long? value)=>value?.ToString(CultureInfo.InvariantCulture)??"";
    private static string Escape(string v)=>v.IndexOfAny([',','"','\r','\n'])>=0?$"\"{v.Replace("\"","\"\"")}\"":v;
    private static string EscapeTab(string v)=>v.IndexOfAny(['\t','"','\r','\n'])>=0?$"\"{v.Replace("\"","\"\"")}\"":v;
    private static XDocument LoadXml(ZipArchiveEntry entry){using var input=entry.Open();return XDocument.Load(input,LoadOptions.None);}
    private static int ExcelColumnIndex(string reference)
    {
        var column=0;var found=false;
        foreach(var c in reference)
        {
            if(!char.IsLetter(c))break;
            found=true;column=column*26+(char.ToUpperInvariant(c)-'A'+1);
        }
        return found?column-1:-1;
    }
    private static HashSet<int> ReadExcelDateStyles(ZipArchive archive,XNamespace ns)
    {
        var entry=archive.GetEntry("xl/styles.xml");
        if(entry==null)return [];
        var document=LoadXml(entry);
        var customFormats=document.Descendants(ns+"numFmt")
            .Select(x=>(Id:(int?)x.Attribute("numFmtId"),Code:(string?)x.Attribute("formatCode")))
            .Where(x=>x.Id!=null&&x.Code!=null)
            .ToDictionary(x=>x.Id!.Value,x=>x.Code!,EqualityComparer<int>.Default);
        bool IsDateFormat(int id)
        {
            if((id>=14&&id<=22)||(id>=45&&id<=47))return true;
            if(!customFormats.TryGetValue(id,out var code))return false;
            var normalized=code.ToLowerInvariant();
            return normalized.Contains('y')&&(normalized.Contains('m')||normalized.Contains('d'));
        }
        return document.Descendants(ns+"cellXfs").Elements(ns+"xf")
            .Select((xf,index)=>(index,Format:(int?)xf.Attribute("numFmtId")??0))
            .Where(x=>IsDateFormat(x.Format))
            .Select(x=>x.index)
            .ToHashSet();
    }
    private static void WriteXml(ZipArchive archive,string path,Action<XmlWriter> write)
    {
        var entry=archive.CreateEntry(path,CompressionLevel.Fastest);
        using var stream=entry.Open();
        using var writer=XmlWriter.Create(stream,new XmlWriterSettings { Encoding=new UTF8Encoding(false),Indent=false });
        writer.WriteStartDocument();write(writer);writer.WriteEndDocument();
    }
    private static void ContentType(XmlWriter writer,string part,string type){writer.WriteStartElement("Override");writer.WriteAttributeString("PartName",part);writer.WriteAttributeString("ContentType",type);writer.WriteEndElement();}
    private static void Relationship(XmlWriter writer,string id,string type,string target){writer.WriteStartElement("Relationship");writer.WriteAttributeString("Id",id);writer.WriteAttributeString("Type",type);writer.WriteAttributeString("Target",target);writer.WriteEndElement();}
    private static void WriteStyles(XmlWriter writer)
    {
        const string ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        writer.WriteStartElement("styleSheet",ns);
        writer.WriteStartElement("fonts");writer.WriteAttributeString("count","2");Font(writer,false);Font(writer,true);writer.WriteEndElement();
        writer.WriteStartElement("fills");writer.WriteAttributeString("count","3");Fill(writer,"none");Fill(writer,"gray125");Fill(writer,"solid","D9EAF7");writer.WriteEndElement();
        writer.WriteStartElement("borders");writer.WriteAttributeString("count","1");writer.WriteStartElement("border");foreach(var side in new[]{"left","right","top","bottom","diagonal"})writer.WriteElementString(side,ns,"");writer.WriteEndElement();writer.WriteEndElement();
        writer.WriteStartElement("cellStyleXfs");writer.WriteAttributeString("count","1");Xf(writer,0,0,0);writer.WriteEndElement();
        writer.WriteStartElement("cellXfs");writer.WriteAttributeString("count","4");Xf(writer,0,0,0);Xf(writer,1,2,0,true);Xf(writer,0,0,14);Xf(writer,0,0,3);writer.WriteEndElement();
        writer.WriteStartElement("cellStyles");writer.WriteAttributeString("count","1");writer.WriteStartElement("cellStyle");writer.WriteAttributeString("name","Normal");writer.WriteAttributeString("xfId","0");writer.WriteAttributeString("builtinId","0");writer.WriteEndElement();writer.WriteEndElement();
        writer.WriteEndElement();
    }
    private static void Font(XmlWriter writer,bool bold){writer.WriteStartElement("font");writer.WriteStartElement("sz");writer.WriteAttributeString("val","11");writer.WriteEndElement();writer.WriteStartElement("name");writer.WriteAttributeString("val","맑은 고딕");writer.WriteEndElement();if(bold)writer.WriteElementString("b","");writer.WriteEndElement();}
    private static void Fill(XmlWriter writer,string pattern,string? color=null){writer.WriteStartElement("fill");writer.WriteStartElement("patternFill");writer.WriteAttributeString("patternType",pattern);if(color!=null){writer.WriteStartElement("fgColor");writer.WriteAttributeString("rgb","FF"+color);writer.WriteEndElement();writer.WriteStartElement("bgColor");writer.WriteAttributeString("indexed","64");writer.WriteEndElement();}writer.WriteEndElement();writer.WriteEndElement();}
    private static void Xf(XmlWriter writer,int font,int fill,int numberFormat,bool alignment=false){writer.WriteStartElement("xf");writer.WriteAttributeString("numFmtId",numberFormat.ToString(CultureInfo.InvariantCulture));writer.WriteAttributeString("fontId",font.ToString(CultureInfo.InvariantCulture));writer.WriteAttributeString("fillId",fill.ToString(CultureInfo.InvariantCulture));writer.WriteAttributeString("borderId","0");writer.WriteAttributeString("xfId","0");if(font>0)writer.WriteAttributeString("applyFont","1");if(fill>0)writer.WriteAttributeString("applyFill","1");if(numberFormat>0)writer.WriteAttributeString("applyNumberFormat","1");if(alignment){writer.WriteAttributeString("applyAlignment","1");writer.WriteStartElement("alignment");writer.WriteAttributeString("horizontal","center");writer.WriteAttributeString("vertical","center");writer.WriteEndElement();}writer.WriteEndElement();}
    private static void WriteSheet(XmlWriter writer,IReadOnlyCollection<Employee> employees,IReadOnlyDictionary<string,string>? displayNames)
    {
        const string ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        writer.WriteStartElement("worksheet",ns);
        writer.WriteStartElement("sheetViews");writer.WriteStartElement("sheetView");writer.WriteAttributeString("workbookViewId","0");writer.WriteStartElement("pane");writer.WriteAttributeString("ySplit","1");writer.WriteAttributeString("topLeftCell","A2");writer.WriteAttributeString("activePane","bottomLeft");writer.WriteAttributeString("state","frozen");writer.WriteEndElement();writer.WriteEndElement();writer.WriteEndElement();
        writer.WriteStartElement("cols");double[] widths=[14,18,18,13,12,12,12,12,12,14,10,13,13,13,16,16,14,18,18];for(var i=0;i<widths.Length;i++){writer.WriteStartElement("col");writer.WriteAttributeString("min",(i+1).ToString());writer.WriteAttributeString("max",(i+1).ToString());writer.WriteAttributeString("width",widths[i].ToString(CultureInfo.InvariantCulture));writer.WriteAttributeString("customWidth","1");writer.WriteEndElement();}writer.WriteEndElement();
        writer.WriteStartElement("sheetData");writer.WriteStartElement("row");writer.WriteAttributeString("r","1");for(var i=0;i<Headers.Length;i++)TextCell(writer,Cell(i,1),displayNames!=null&&displayNames.TryGetValue(Headers[i],out var name)?name:Headers[i],1);writer.WriteEndElement();
        var row=2;foreach(var x in employees){writer.WriteStartElement("row");writer.WriteAttributeString("r",row.ToString());string?[] text=[x.Workplace,x.ParentDepartment,x.Department,x.EmployeeNumber,x.Name,x.Position,x.WorkShift,x.Duty,x.JobGroup,x.EmploymentType,x.Gender];for(var i=0;i<text.Length;i++)TextCell(writer,Cell(i,row),text[i]??"");DateCell(writer,Cell(11,row),x.BirthDate);DateCell(writer,Cell(12,row),x.HireDate);DateCell(writer,Cell(13,row),x.TerminationDate);NumberCell(writer,Cell(14,row),x.AnnualSalary);NumberCell(writer,Cell(15,row),x.MonthlyWage);TextCell(writer,Cell(16,row),x.Education??"");TextCell(writer,Cell(17,row),x.SchoolName??"");TextCell(writer,Cell(18,row),x.Major??"");writer.WriteEndElement();row++;}
        writer.WriteEndElement();writer.WriteStartElement("autoFilter");writer.WriteAttributeString("ref",$"A1:S{Math.Max(1,row-1)}");writer.WriteEndElement();writer.WriteEndElement();
    }
    private static string Cell(int column,int row){var name="";for(var n=column+1;n>0;n=(n-1)/26)name=(char)('A'+(n-1)%26)+name;return name+row;}
    private static void TextCell(XmlWriter writer,string reference,string value,int style=0){writer.WriteStartElement("c");writer.WriteAttributeString("r",reference);writer.WriteAttributeString("t","inlineStr");if(style>0)writer.WriteAttributeString("s",style.ToString());writer.WriteStartElement("is");writer.WriteElementString("t",value);writer.WriteEndElement();writer.WriteEndElement();}
    private static void DateCell(XmlWriter writer,string reference,DateTime? value){if(value==null)return;writer.WriteStartElement("c");writer.WriteAttributeString("r",reference);writer.WriteAttributeString("s","2");writer.WriteElementString("v",value.Value.ToOADate().ToString(CultureInfo.InvariantCulture));writer.WriteEndElement();}
    private static void NumberCell(XmlWriter writer,string reference,long? value){if(value==null)return;writer.WriteStartElement("c");writer.WriteAttributeString("r",reference);writer.WriteAttributeString("s","3");writer.WriteElementString("v",value.Value.ToString(CultureInfo.InvariantCulture));writer.WriteEndElement();}
}
public sealed record EmployeeImportResult(IReadOnlyList<EmployeeImportRow> Rows,IReadOnlySet<string> PresentHeaders);
public sealed record EmployeeImportRow(string EmployeeNumber,string? Workplace,string? ParentDepartment,string? Department,string? Name,string? Position,string? WorkShift,string? Duty,string? JobGroup,string? EmploymentType,string? Gender,DateTime? BirthDate,DateTime? HireDate,DateTime? TerminationDate,long? AnnualSalary,long? MonthlyWage,string? Education,string? SchoolName,string? Major);
public sealed class EmployeeCsvException(string message):Exception(message);
