using WinInspector.Core.Apps;using WinInspector.Core.Evidence;using WinInspector.Core.Output;using WinInspector.Core.Settings;using Xunit;
namespace WinInspector.Core.Tests;
public class LogicTests
{
 [Fact] public void Rot13DecodesAscii(){Assert.Equal("TheQuick",Rot13.Decode("GurDhVPX"));}
 [Fact] public void DwordHighBitIsUnsignedAndEnabled(){Assert.True(TrackingRules.Interpret(-2147483648L).Enabled);}
 [Fact] public void FormulaCellProtected(){Assert.Equal("\"'=1+1\"",CsvWriter.Cell("=1+1",true));}
 [Fact] public void NegativeEstimateIsMissing(){Assert.Null(InstallSizePolicy.EstimatedBytes(-1));}
 [Fact] public void UnknownUserAssistData(){Assert.Null(UserAssistDecoder.Parse(new byte[20]));}
 [Fact] public void PrefetchFiltersGenericNames(){Assert.Null(PrefetchNameParser.Parse("setup-12345678.pf"));Assert.Equal("editor.exe",PrefetchNameParser.Parse("editor-12345678.pf"));}
 [Fact] public void KnownFolderMapHasSevenEntries(){Assert.Equal(7,KnownFolderMap.KnownGuids.Count);Assert.Equal("{UNKNOWN}\\x",new KnownFolderMap().Resolve("{UNKNOWN}\\x"));}
 [Fact] public void PsdReaderAcceptsLimitedConfig(){var r=PsdReader.Parse("@{ Mode='Files'; Target=@('C:\\Temp','D:\\Data'); UnusedDays=20; IncludeRawLastAccess=$false }");Assert.Null(r.Warning);Assert.Equal("Files",r.Values["Mode"]);Assert.Equal(20,r.Values["UnusedDays"]);}
 [Fact] public void SummaryKeepsExeImplementationRows(){var s=SummaryBuilder.Build(new(DateTime.UnixEpoch,"Files",[],"out",false,"user","setting ()","tracking / ","UTC",0,0,TimeSpan.Zero,0));Assert.Contains("実行形式: WinInspector.exe",s);Assert.Contains("言語モード: 該当なし（exe 版）",s);}
}
