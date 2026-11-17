namespace WinInspector.Core.Cli;
public sealed record CliOptions(string Mode,string[] Targets,string[] Excludes,string? OutputDir,int UnusedDays=365,int FolderDepth=3,bool NoPause=false,bool Elevate=false,string? ConfigWarning=null,bool IncludeRawLastAccess=false,bool IncludeRemovable=false,bool LowerPriority=false,bool ShowHelp=false,bool ShowVersion=false);
public static class CliParser
{
    public static CliOptions Parse(string[] args)
    {
        var mode="All";string? output=null;var targets=new List<string>();var excludes=new List<string>();var days=365;var depth=3;var pause=false;var elevate=false;var raw=false;var removable=false;var lower=false;var help=false;var version=false;
        for(var i=0;i<args.Length;i++)
        {
            string Next(){if(i+1>=args.Length)throw new ArgumentException("引数の値がありません");return args[++i];}
            switch(args[i].ToLowerInvariant())
            {
                case "-mode":mode=Next();break;
                case "-target":while(i+1<args.Length&&!args[i+1].StartsWith('-'))targets.Add(args[++i]);if(targets.Count==0)throw new ArgumentException("-Target に値がありません");break;
                case "-exclude":while(i+1<args.Length&&!args[i+1].StartsWith('-'))excludes.Add(args[++i]);if(excludes.Count==0)throw new ArgumentException("-Exclude に値がありません");break;
                case "-outputdir":output=Next();break;
                case "-unuseddays":if(!int.TryParse(Next(),System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out days))throw new ArgumentException("UnusedDays は整数で指定してください。");break;
                case "-folderdepth":if(!int.TryParse(Next(),System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out depth))throw new ArgumentException("FolderDepth は整数で指定してください。");break;
                case "-nopause":pause=true;break;case "-elevate":elevate=true;break;case "-includerawlastaccess":raw=true;break;case "-includeremovable":removable=true;break;case "-lowerpriority":lower=true;break;case "-help":case "-h":help=true;break;case "-version":version=true;break;
                default:throw new ArgumentException($"不明な引数: {args[i]}");
            }
        }
        if(!mode.Equals("Files",StringComparison.OrdinalIgnoreCase)&&!mode.Equals("Apps",StringComparison.OrdinalIgnoreCase)&&!mode.Equals("All",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Mode は Files, Apps, All のいずれかです");
        mode=char.ToUpperInvariant(mode[0])+mode[1..].ToLowerInvariant();
        return new(mode,targets.ToArray(),excludes.ToArray(),output,days,depth,pause,elevate,null,raw,removable,lower,help,version);
    }
}
