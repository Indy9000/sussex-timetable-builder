open System
open System.IO
 
Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

let dst = ".paket\paket.exe"
if not (File.Exists dst) then
    //let url = "https://github.com/fsprojects/Paket/releases/download/1.26.1/paket.exe"
    let urlRef = @"https://fsprojects.github.io/Paket/stable"
    use wc = new Net.WebClient()
    let url = wc.DownloadString(urlRef)
    let tmp = Path.GetTempFileName()
    wc.DownloadFile(url, tmp)
    Directory.CreateDirectory(".paket") |> ignore
    File.Move(tmp, dst)
 
// Step 1. Resolve and install the packages
 
#r ".paket\paket.exe"
 
Paket.Dependencies.Install """
source https://nuget.org/api/v2
nuget FSharp.Data
nuget Newtonsoft.Json
"""

// Step 2. Use the packages
 
#r @"packages\Newtonsoft.Json\lib\net45\Newtonsoft.Json.dll"
#r @"packages\FSharp.Data\lib\net40\FSharp.Data.dll"

open FSharp.Data
open System.Web
open System.Globalization

let GetHtmlDocument (tokens:(string*string)[]) = 
    let baseUrl = @"http://www.sussex.ac.uk/students/timetable/search?"
    let formData = 
        tokens 
        |> Seq.map(fun (a,b) -> HttpUtility.UrlEncode(a), HttpUtility.UrlEncode(b))
        |> Seq.map(fun (a,b) -> sprintf "%s=%s" a b) 
        |> String.concat("&")
    printfn "%s" formData
    HtmlDocument.Load(baseUrl + formData)

//test GetWebPage
let CourseStartDate = DateTime(2015,09,21)
type CourseEvent =
    {
        Code : string;
        Name:string;
        Tutor:string;
        StartTime:DateTime;
        FinishTime:DateTime;
        Location:string;
    }

let Weekdays = [|"mon";"tue";"wed";"thu";"fri";"sat";"sun"|]
let ParseTime (s_t:string(*9:00*)) =
    let toks = s_t.Split([|':'|])
    let h = toks.[0].Trim().AsInteger()
    let m = toks.[1].Trim().AsInteger()
    TimeSpan(h,m,0)

let DecodeDate (s:string) (*Tue @ 9:00-11:00*)= 
    printfn "%s" s
    let tokens = s.Split([|'@'|])
    let weekday = tokens.[0].Trim()
    let start_s = tokens.[1].Trim().Split([|'-'|]).[0]
    let finish_s = tokens.[1].Trim().Split([|'-'|]).[1]
    let start = ParseTime(start_s)
    let finish = ParseTime(finish_s)

    let weekOffset = Weekdays|> Array.findIndex(fun k-> k = weekday.ToLowerInvariant())
    (weekOffset,start,finish)

let GenerateCourseDates (cols:string[]) = 
    cols.[5].Split([|' ';'\r';'\n'|],StringSplitOptions.RemoveEmptyEntries)
    |> Array.mapi(fun i s -> i, (s.Trim() = "1") )
    |> Array.choose(fun (i,b) -> 
        //printfn "%b" b
        if b then
            let (od,st,ft) = DecodeDate(cols.[3].Trim())
            Some{
                    Code = cols.[0].Trim();
                    Name = cols.[1].Trim();
                    Tutor = cols.[2].Trim();
                    StartTime = CourseStartDate.Add(new TimeSpan(i*7 + od,st.Hours,st.Minutes,0));
                    FinishTime = CourseStartDate.Add(new TimeSpan(i*7 + od,ft.Hours,ft.Minutes,0));
                    Location = cols.[4].Trim()
                }
        else
            None
        )


let ParseCategory (name:string) = 
    if name.Contains("(Lec") then "Lec" else
        if name.Contains("(Lab") then "Lab" else
            if name.Contains("(Sem") then "Sem" else ""


let GenerateiCalEvent (c:CourseEvent) =
    //DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mmzzz")
    sprintf "BEGIN:VEVENT
CATEGORIES:MEETING
STATUS:BUSY
DTSTART:%s
DTEND:%s
SUMMARY:%s
DESCRIPTION:%s
CLASS:PRIVATE
END:VEVENT"
            (c.StartTime.ToUniversalTime().ToString("yyyyMMddTHHmmssZ"))
            (c.FinishTime.ToUniversalTime().ToString("yyyyMMddTHHmmssZ"))
            (c.Code + "    " + (ParseCategory c.Name)  + "    " + c.Location)
            (c.Name + " " + c.Location + " " + c.Tutor)

let ParseTableGenerateSchedule (courseCode:string) = 
    let html = [|("keyword",courseCode);("term","1:tb1")|] |> GetHtmlDocument 

    html.Descendants[@"table"] //get tables
    |> Seq.head |> fun t-> t.Descendants[@"tr"] //get rows
    |> Seq.choose( fun row ->
        row.Descendants["td"] //columns
        |> Seq.map(fun col -> 
            let colText = col.InnerText()
            colText
            )
        |> Seq.toArray 
        |> fun k-> k
        |> fun t -> if t.Length > 5 then
                        Some (GenerateCourseDates t)
                    else
                        None
        )
    |> Seq.toArray
    |> Array.concat

//================================
[|"819G5";"826G5";"817G5";"955G5";"802G5";"823G5"|] // autumn 2015 schedule
|>Array.map ParseTableGenerateSchedule
|>Array.concat
|>Array.sortBy(fun k-> k.StartTime)
//|>Array.take 20
|>Array.map GenerateiCalEvent
|> String.concat("\n")
|> sprintf "BEGIN:VCALENDAR
VERSION:1.0
%s
END:VCALENDAR"
|> fun output -> File.WriteAllText(@"c:\temp\msc.ics",output)
