using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using HRDashboard.Models;
using Microsoft.VisualBasic.FileIO;

namespace HRDashboard.Services;

public sealed class EmployeeCsvService
{
    public static readonly string[] Headers=["사업장","상위부서","부서","사번","성명","직위","근무조","직책","직군","사원구분","성별","생년월일","입사일자","퇴사일자","월임금"];
    private static readonly string[] RequiredHeaders=["사번"];
    public byte[] Export(IReadOnlyCollection<Employee> employees)
    {
        var csv=new StringBuilder(); csv.AppendLine(string.Join(',',Headers.Select(Escape)));
        foreach(var x in employees) { string[] values=[x.Workplace??"",x.ParentDepartment??"",x.Department??"",x.EmployeeNumber,x.Name??"",x.Position??"",x.WorkShift??"",x.Duty??"",x.JobGroup??"",x.EmploymentType??"",x.Gender??"",Date(x.BirthDate),Date(x.HireDate),Date(x.TerminationDate),x.MonthlyWage?.ToString(CultureInfo.InvariantCulture)??""]; csv.AppendLine(string.Join(',',values.Select(Escape))); }
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
        while(!parser.EndOfData){n++;try{var f=parser.ReadFields()??[];if(f.All(string.IsNullOrWhiteSpace))continue;string V(string h)=>map.TryGetValue(h,out var i)&&i<f.Length?f[i].Trim():"";string? O(string h,int max){var v=V(h);if(v.Length>max)throw new EmployeeCsvException($"{n}행 [{h}]: {max}자를 초과했습니다.");return v.Length==0?null:v;}var no=V("사번");if(no.Length==0)throw new EmployeeCsvException($"{n}행 [사번]: 값이 비어 있습니다.");if(no.Length>50)throw new EmployeeCsvException($"{n}행 [사번]: 50자를 초과했습니다.");rows.Add(new(no,O("사업장",100),O("상위부서",100),O("부서",100),O("성명",100),O("직위",50),O("근무조",50),O("직책",50),O("직군",50),O("사원구분",50),O("성별",20),ParseDate(V("생년월일"),n,"생년월일"),ParseDate(V("입사일자"),n,"입사일자"),ParseDate(V("퇴사일자"),n,"퇴사일자"),ParseWage(V("월임금"),n)));}catch(Exception e)when(e is EmployeeCsvException or MalformedLineException){errors.Add(e.Message);}}
        if(errors.Count>0)throw new EmployeeCsvException(string.Join("\n",errors.Take(20)));if(rows.Count==0)throw new EmployeeCsvException("반영할 사원 데이터가 없습니다.");var dup=rows.GroupBy(x=>x.EmployeeNumber,StringComparer.OrdinalIgnoreCase).Where(x=>x.Count()>1).Select(x=>x.Key).ToArray();if(dup.Length>0)throw new EmployeeCsvException($"사번이 중복되었습니다: {string.Join(", ",dup.Take(20))}");return new(rows,Headers.Where(map.ContainsKey).ToHashSet(StringComparer.OrdinalIgnoreCase));
    }
    private static DateTime? ParseDate(string v,int row,string col){if(v.Length==0)return null;string[] formats=["yyyy-MM-dd","yyyy.MM.dd","yyyy/MM/dd","yyyyMMdd","M/d/yyyy","MM/dd/yyyy"];if(DateTime.TryParseExact(v,formats,CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)||DateTime.TryParse(v,CultureInfo.GetCultureInfo("ko-KR"),DateTimeStyles.None,out d))return d.Date;throw new EmployeeCsvException($"{row}행 [{col}]: 날짜 형식을 확인하세요 ({v}).");}
    private static long? ParseWage(string v,int row){if(v.Length==0)return null;v=v.Replace(",","").Replace("원","").Trim();if(long.TryParse(v,NumberStyles.Integer,CultureInfo.InvariantCulture,out var wage)&&wage>=0)return wage;throw new EmployeeCsvException($"{row}행 [월임금]: 0 이상의 원 단위 숫자를 입력하세요.");}
    private static string Date(DateTime? d)=>d?.ToString("yyyy-MM-dd")??"";
    private static string Escape(string v)=>v.IndexOfAny([',','"','\r','\n'])>=0?$"\"{v.Replace("\"","\"\"")}\"":v;
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
        writer.WriteStartElement("cols");double[] widths=[14,18,18,13,12,12,12,12,12,14,10,13,13,13,16];for(var i=0;i<widths.Length;i++){writer.WriteStartElement("col");writer.WriteAttributeString("min",(i+1).ToString());writer.WriteAttributeString("max",(i+1).ToString());writer.WriteAttributeString("width",widths[i].ToString(CultureInfo.InvariantCulture));writer.WriteAttributeString("customWidth","1");writer.WriteEndElement();}writer.WriteEndElement();
        writer.WriteStartElement("sheetData");writer.WriteStartElement("row");writer.WriteAttributeString("r","1");for(var i=0;i<Headers.Length;i++)TextCell(writer,Cell(i,1),displayNames!=null&&displayNames.TryGetValue(Headers[i],out var name)?name:Headers[i],1);writer.WriteEndElement();
        var row=2;foreach(var x in employees){writer.WriteStartElement("row");writer.WriteAttributeString("r",row.ToString());string?[] text=[x.Workplace,x.ParentDepartment,x.Department,x.EmployeeNumber,x.Name,x.Position,x.WorkShift,x.Duty,x.JobGroup,x.EmploymentType,x.Gender];for(var i=0;i<text.Length;i++)TextCell(writer,Cell(i,row),text[i]??"");DateCell(writer,Cell(11,row),x.BirthDate);DateCell(writer,Cell(12,row),x.HireDate);DateCell(writer,Cell(13,row),x.TerminationDate);NumberCell(writer,Cell(14,row),x.MonthlyWage);writer.WriteEndElement();row++;}
        writer.WriteEndElement();writer.WriteStartElement("autoFilter");writer.WriteAttributeString("ref",$"A1:O{Math.Max(1,row-1)}");writer.WriteEndElement();writer.WriteEndElement();
    }
    private static string Cell(int column,int row){var name="";for(var n=column+1;n>0;n=(n-1)/26)name=(char)('A'+(n-1)%26)+name;return name+row;}
    private static void TextCell(XmlWriter writer,string reference,string value,int style=0){writer.WriteStartElement("c");writer.WriteAttributeString("r",reference);writer.WriteAttributeString("t","inlineStr");if(style>0)writer.WriteAttributeString("s",style.ToString());writer.WriteStartElement("is");writer.WriteElementString("t",value);writer.WriteEndElement();writer.WriteEndElement();}
    private static void DateCell(XmlWriter writer,string reference,DateTime? value){if(value==null)return;writer.WriteStartElement("c");writer.WriteAttributeString("r",reference);writer.WriteAttributeString("s","2");writer.WriteElementString("v",value.Value.ToOADate().ToString(CultureInfo.InvariantCulture));writer.WriteEndElement();}
    private static void NumberCell(XmlWriter writer,string reference,long? value){if(value==null)return;writer.WriteStartElement("c");writer.WriteAttributeString("r",reference);writer.WriteAttributeString("s","3");writer.WriteElementString("v",value.Value.ToString(CultureInfo.InvariantCulture));writer.WriteEndElement();}
}
public sealed record EmployeeImportResult(IReadOnlyList<EmployeeImportRow> Rows,IReadOnlySet<string> PresentHeaders);
public sealed record EmployeeImportRow(string EmployeeNumber,string? Workplace,string? ParentDepartment,string? Department,string? Name,string? Position,string? WorkShift,string? Duty,string? JobGroup,string? EmploymentType,string? Gender,DateTime? BirthDate,DateTime? HireDate,DateTime? TerminationDate,long? MonthlyWage);
public sealed class EmployeeCsvException(string message):Exception(message);
